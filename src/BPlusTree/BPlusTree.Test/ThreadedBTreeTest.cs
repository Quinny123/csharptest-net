﻿#region Copyright 2011 by Roger Knapp, Licensed under the Apache License, Version 2.0
/* Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 * 
 *   http://www.apache.org/licenses/LICENSE-2.0
 * 
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */
#endregion
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using CSharpTest.Net.BPlusTree.Test.SampleTypes;
using CSharpTest.Net.IO;
using CSharpTest.Net.Serialization;
using CSharpTest.Net.Synchronization;
using CSharpTest.Net.Threading;
using NUnit.Framework;
using CSharpTest.Net.Collections;

namespace CSharpTest.Net.BPlusTree.Test
{
    [TestFixture]
    public class ThreadedBTreeTest
    {
        protected static int RecordsCreated;
        protected TempFile TempFile;
        #region TestFixture SetUp/TearDown
        [TestFixtureSetUp]
        public virtual void Setup()
        {
            TempFile = new TempFile();
        }

        [TestFixtureTearDown]
        public virtual void Teardown()
        {
            TempFile.Dispose();
        }
        #endregion

        [SetUp]
        public void ClearRecords()
        { RecordsCreated = 0; }

        [Test]
        public void TestAtomicUpdate()
        {
            int threads = Environment.ProcessorCount;
            const int updates = 10000;

            Converter<int, int> fnIncrement = delegate(int i) { return i + 1; };
            Action<BPlusTree<int, int>> fnDoUpdates = delegate(BPlusTree<int, int> t) { for (int i = 0; i < updates; i++) t.Update(1, fnIncrement); };

            Stopwatch time = new Stopwatch();
            time.Start();
            using (BPlusTree<int, int> tree = new BPlusTree<int, int>(new BPlusTree<int, int>.Options(PrimitiveSerializer.Instance, PrimitiveSerializer.Instance)))
            {
                tree[1] = 0;
                using (WorkQueue w = new WorkQueue(threads))
                {
                    for (int i = 0; i < threads; i++)
                        w.Enqueue(fnDoUpdates, tree);

                    Assert.IsTrue(w.Complete(true, 60000));
                }

                Assert.AreEqual(updates * threads, tree[1]);
            }

            Trace.TraceInformation("Updated {0} times on each of {1} threads in {2}ms", updates, threads, time.ElapsedMilliseconds);
        }

        [Test]
        public void TestCallLevelLocking()
        {
            BPlusTree<int, int>.Options options = new BPlusTree<int, int>.Options(
                new PrimitiveSerializer(), new PrimitiveSerializer());
            options.LockTimeout = 100;
            options.CallLevelLock = new ReaderWriterLocking();
        
            using (BPlusTree<int, int> dictionary = new BPlusTree<int, int>(options))
            {
                bool canwrite = false, canread = false;
                ThreadStart proc = delegate()
                {
                    try { dictionary.Add(1, 1); canwrite = true; } catch { canwrite = false; }
                    try { int i; dictionary.TryGetValue(1, out i); canread = true; } catch { canread = false; }
                };

                Assert.IsTrue(proc.BeginInvoke(null, null).AsyncWaitHandle.WaitOne(1000, false));
                Assert.IsTrue(canwrite);
                Assert.IsTrue(canread);

                //now we lock the entire btree:
                using (dictionary.CallLevelLock.Write())
                {
                    //they can't read or write
                    Assert.IsTrue(proc.BeginInvoke(null, null).AsyncWaitHandle.WaitOne(1000, false));
                    Assert.IsFalse(canwrite);
                    Assert.IsFalse(canread);
                    //but we can
                    proc();
                    Assert.IsTrue(canwrite);
                    Assert.IsTrue(canread);
                }
                //lock release all is well
                Assert.IsTrue(proc.BeginInvoke(null, null).AsyncWaitHandle.WaitOne(1000, false));
                Assert.IsTrue(canwrite);
                Assert.IsTrue(canread);

                //We can also make sure noone else gains exclusive access with a read lock
                using (dictionary.CallLevelLock.Read())
                {
                    Assert.IsTrue(proc.BeginInvoke(null, null).AsyncWaitHandle.WaitOne(1000, false));
                    Assert.IsTrue(canwrite);
                    Assert.IsTrue(canread);
                }
            }
        }

        [Test]
        public void TestAbortWritersAndRecover()
        {
            BPlusTree<KeyInfo, DataValue>.Options options = new BPlusTree<KeyInfo, DataValue>.Options(
                new KeyInfoSerializer(), new DataValueSerializer(), new KeyInfoComparer());
            options.CalcBTreeOrder(32, 300);
            options.FileName = TempFile.TempPath;
            options.CreateFile = CreatePolicy.Always;

            using (TempFile copy = new TempFile())
            {
                copy.Delete();
                RecordsCreated = 0;
                int minRecordCreated;
                using (BPlusTree<KeyInfo, DataValue> dictionary = new BPlusTree<KeyInfo, DataValue>(options))
                using (WorkQueue work = new WorkQueue(options.ConcurrentWriters))
                {
                    Exception lastError = null;
                    work.OnError += delegate(object o, ErrorEventArgs e) { lastError = e.GetException(); };

                    Thread.Sleep(1);
                    for (int i = 0; i < options.ConcurrentWriters; i++)
                        work.Enqueue(new ThreadedTest(dictionary, 10000000).Run);

                    while (RecordsCreated < 1000)
                        Thread.Sleep(1);

                    minRecordCreated = Interlocked.CompareExchange(ref RecordsCreated, 0, 0);
                    File.Copy(options.FileName, copy.TempPath);//just grab a copy any old time.
                    work.Complete(false, 0); //hard-abort all threads
                }

                using (TempFile.Attach(copy.TempPath + ".recovered")) //used to create the new copy
                using (TempFile.Attach(copy.TempPath + ".deleted"))  //renamed existing file
                {
                    options.CreateFile = CreatePolicy.Never;
                    options.FileName = copy.TempPath;

                    int recoveredRecords = BPlusTree<KeyInfo, DataValue>.RecoverFile(options);
                    Assert.IsTrue(recoveredRecords >= minRecordCreated);

                    using (BPlusTree<KeyInfo, DataValue> dictionary = new BPlusTree<KeyInfo, DataValue>(options))
                    {
                        dictionary.EnableCount();
                        Assert.AreEqual(recoveredRecords, dictionary.Count);

                        foreach (KeyValuePair<KeyInfo, DataValue> kv in dictionary)
                        {
                            Assert.AreEqual(kv.Key.UID, kv.Value.Key.UID);
                            dictionary.Remove(kv.Key);
                        }

                        Assert.AreEqual(0, dictionary.Count);
                    }
                }
            }
        }

        [Test]
        public void TestConcurrentCreateReadUpdateDelete8000()
        {
            BPlusTree<KeyInfo, DataValue>.Options options = new BPlusTree<KeyInfo, DataValue>.Options(
                new KeyInfoSerializer(), new DataValueSerializer(), new KeyInfoComparer());
            
            const int keysize = 16 + 4;
            const int valuesize = keysize + 256 + 44;

            options.CalcBTreeOrder(keysize, valuesize); 
            options.FileName = TempFile.TempPath;
            options.CreateFile = CreatePolicy.Always;
            options.FileBlockSize = 8192;
            options.FileGrowthRate = 50;
            options.FileOpenOptions = FileOptions.RandomAccess;
            options.StorageType = StorageType.Disk;
            
            options.CacheKeepAliveTimeout = 10000;
            options.CacheKeepAliveMinimumHistory = 0;
            options.CacheKeepAliveMaximumHistory = 200;
            
            options.CallLevelLock = new ReaderWriterLocking();
            options.LockingFactory = new LockFactory<SimpleReadWriteLocking>();
            options.LockTimeout = 10000;
            options.ConcurrentWriters = 8;

            using(BPlusTree<KeyInfo, DataValue> dictionary = new BPlusTree<KeyInfo, DataValue>(options))
            using(WorkQueue work = new WorkQueue(options.ConcurrentWriters))
            {
                Exception lastError = null;
                work.OnError += delegate(object o, ErrorEventArgs e) { lastError = e.GetException(); };

                for( int i=0; i < options.ConcurrentWriters; i++ )
                    work.Enqueue(new ThreadedTest(dictionary, 1000).Run);

                Assert.IsTrue(work.Complete(true, 60000));
                Assert.IsNull(lastError, "Exception raised in worker: {0}", lastError);
            }
        }

        class KeyInfoEquality : IEqualityComparer<KeyInfo>
        {
            static readonly KeyInfoComparer Comparer = new KeyInfoComparer();
            public bool  Equals(KeyInfo x, KeyInfo y)
            {
                return Comparer.Compare(x, y) == 0;
            }

            public int  GetHashCode(KeyInfo obj)
            {
                return obj.UID.GetHashCode();
            }
        }

        class ThreadedTest
        {
            private readonly int _recordCount;
            private readonly IDictionary<KeyInfo, DataValue> _data;

            public ThreadedTest(IDictionary<KeyInfo, DataValue> data, int recordCount)
            {
                _data = data;
                _recordCount = recordCount;
            }

            public void Run()
            {
                byte[] bytes = new byte[255];
                Random rand = new Random();
                int create = 0, read = 0, write = 0, delete = 0;
                Stopwatch time = new Stopwatch();
                time.Start();

                try
                {
                    Dictionary<KeyInfo, DataValue> values = new Dictionary<KeyInfo, DataValue>(new KeyInfoEquality());
                    for (int i = 0; i < _recordCount; i++)
                    {
                        create++;
                        KeyInfo k = new KeyInfo();
                        rand.NextBytes(bytes);
                        DataValue v = new DataValue(k, bytes);

                        _data.Add(k, v);
                        Interlocked.Increment(ref RecordsCreated);
                        values.Add(k, v);
                    }

                    Dictionary<KeyInfo, DataValue> found = new Dictionary<KeyInfo,DataValue>(values, new KeyInfoEquality());
                    foreach (KeyValuePair<KeyInfo, DataValue> kv in _data)
                    {
                        read++;
                        found.Remove(kv.Key);
                    }

                    Assert.AreEqual(0, found.Count);

                    foreach (KeyValuePair<KeyInfo, DataValue> kv in values)
                    {
                        read++;
                        DataValue test;
                        Assert.IsTrue(_data.TryGetValue(kv.Key, out test));
                        Assert.AreEqual(kv.Key.UID, test.Key.UID);
                        Assert.AreEqual(kv.Value.Hash, test.Hash);
                        Assert.AreEqual(kv.Value.Bytes, test.Bytes);
                    }

                    foreach (KeyValuePair<KeyInfo, DataValue> kv in values)
                    {
                        bytes = kv.Value.Bytes;
                        Array.Reverse(bytes);

                        write++;
                        _data[kv.Key] = new DataValue(kv.Key, bytes);
                        read++;
                        Assert.AreEqual(bytes, _data[kv.Key].Bytes);
                    }

                    foreach (KeyInfo key in new List<KeyInfo>(values.Keys))
                    {
                        delete++;
                        Assert.IsTrue(_data.Remove(key));
                        values.Remove(key);
                    }
                }
                finally
                {
                    time.Stop();
                    Trace.TraceInformation("thread {0,3} complete ({1}c/{2}r/{3}w/{4}d) in {5:n0}ms",
                        Thread.CurrentThread.ManagedThreadId,
                        create, read, write, delete, time.ElapsedMilliseconds);
                }
            }
        }
    }
}