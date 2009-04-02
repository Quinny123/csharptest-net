#region Copyright 2008 by Roger Knapp, Licensed under the Apache License, Version 2.0
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
using System.Diagnostics;
using System.IO;

/// <summary>
/// Quick and dirty logging for components that do not have dependencies, BTW, the
/// additional try/finally pair in the one-liner methods prevents the optimizer from
/// removing the method call and thus keeps the stack accurate at 2 levels.
/// </summary>
[System.Diagnostics.DebuggerNonUserCode]
[System.Diagnostics.DebuggerStepThrough]
internal static partial class Log
{
	#region static Log() -- Opens Log file for writting
	static Log()
	{
		Open();
	}

	static TextWriterTraceListener _traceWriter = null;

	/// <summary>
	/// Allows you to close/open the writer
	/// </summary>
	public static void Open()
	{
		try
		{
			string fullName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), String.Format("{0}\\LogFile.txt", Process.GetCurrentProcess().ProcessName));
			Directory.CreateDirectory(Path.GetDirectoryName(fullName));

			string back = Path.ChangeExtension(fullName, ".bak");
			if (File.Exists(back))
				File.Delete(back);
			if (File.Exists(fullName))
				File.Move(fullName, back);

			FileStream fsStream = File.Open(fullName, FileMode.Append, FileAccess.Write, FileShare.Read | FileShare.Delete);
			StreamWriter sw = new StreamWriter(fsStream);
			sw.AutoFlush = true;
			Trace.Listeners.Add(_traceWriter = new TextWriterTraceListener(sw));
		}
		catch (Exception e)
		{ Trace.WriteLine(e.ToString(), "CSharpTest.Net.QuickLog.Open()"); }
	}

	/// <summary>
	/// Allows you to close/open the writer
	/// </summary>
	public static void Close()
	{
		if (_traceWriter != null)
		{
			Trace.Listeners.Remove(_traceWriter);
			_traceWriter.Dispose();
			_traceWriter = null;
		}
	}
	#endregion

	public static void Error(Exception e) { try { InternalWrite(TraceLevel.Error, "{0}", e); } finally { } }
	public static void Warning(Exception e) { try { InternalWrite(TraceLevel.Warning, "{0}", e); } finally { } }

	public static void Error(string format, params object[] args) { try { InternalWrite(TraceLevel.Error, format, args); } finally { } }
	public static void Warning(string format, params object[] args) { try { InternalWrite(TraceLevel.Warning, format, args); } finally { } }
	public static void Info(string format, params object[] args) { try { InternalWrite(TraceLevel.Info, format, args); } finally { } }
	public static void Verbose(string format, params object[] args) { try { InternalWrite(TraceLevel.Verbose, format, args); } finally { } }
	public static void Write(string format, params object[] args) { try { InternalWrite(TraceLevel.Off, format, args); } finally { } }
	public static void Write(TraceLevel level, string format, params object[] args) { try { InternalWrite(level, format, args); } finally { } }
	
	public static IDisposable Start(string format, params object[] args)
	{
		try 
		{
			if( args.Length > 0 ) format = String.Format( format, args );
			InternalWrite(TraceLevel.Verbose,  "Start {0}", format);
		}
		catch (Exception e) { Trace.WriteLine(e.ToString(), "CSharpTest.Net.QuickLog.Write()"); }
		return new TaskInfo( format );
	}

	public static IDisposable AppStart(string format, params object[] args)
	{
		try
		{
			if (args.Length > 0) format = String.Format(format, args);
			InternalWrite(TraceLevel.Verbose, "Start {0}", format);
		}
		catch (Exception e) { Trace.WriteLine(e.ToString(), "CSharpTest.Net.QuickLog.Write()"); }
		return new TaskInfo(format);
	}

	[System.Diagnostics.DebuggerNonUserCode]
	[System.Diagnostics.DebuggerStepThrough]
	private class TaskInfo : MarshalByRefObject, IDisposable
	{
		private readonly DateTime _start;
		private readonly string _task;
		public TaskInfo(string task) { _task = task; _start = DateTime.Now; }
		void IDisposable.Dispose() 
		{ 
			try  { InternalWrite(TraceLevel.Verbose, "End {0} ({1} ms)", _task, (DateTime.Now - _start).TotalMilliseconds); } 
			finally { } 
		}
	}

	private static void InternalWrite( TraceLevel level, string format, params object[] args )
	{
		try
		{
			int depth = 2;
			if (args.Length > 0)
				format = String.Format(format, args);
			
			StackFrame frame;
			System.Reflection.MethodBase method;

			do
			{
				frame = new StackFrame(depth++);
				method = frame.GetMethod();
			}
			while (method.ReflectedType.GetCustomAttributes(typeof(System.Diagnostics.DebuggerNonUserCodeAttribute), true).Length > 0);

			string methodName, callingType;
			methodName = String.Format("{0}", method);
			callingType = String.Format("{0}", method.ReflectedType);

			string full = String.Format("{0:D2}{1,8} - {2}   at {3}", 
				System.Threading.Thread.CurrentThread.ManagedThreadId, 
				level == TraceLevel.Off ? "None" : level.ToString(),
				format, methodName);

			Trace.WriteLine(full, callingType);
			if (LogWrite != null)
				LogWrite(method, level, format);
		}
		catch(Exception e)
		{ Trace.WriteLine(e.ToString(), "CSharpTest.Net.QuickLog.Write()"); }
	}

	public delegate void LogEventHandler(System.Reflection.MethodBase method, TraceLevel level, string message);
	public static event LogEventHandler LogWrite;
	
	#region Remoting Able Version
	public interface ILog
	{
		void Error(Exception e);
		void Warning(Exception e);

		void Error(string format, params object[] args);
		void Warning(string format, params object[] args);
		void Info(string format, params object[] args);
		void Verbose(string format, params object[] args);
		void Write(string format, params object[] args);

		IDisposable Start(string format, params object[] args);
		IDisposable AppStart(string format, params object[] args);
	}

	/// <summary>
	/// Returns a remoteable version of the Log interface for writing across AppDomains
	/// into a single log
	/// </summary>
	public static ILog RemoteLog = new LogWrapper();

	[System.Diagnostics.DebuggerNonUserCode]
	[System.Diagnostics.DebuggerStepThrough]
	private class LogWrapper : MarshalByRefObject, ILog
	{
		void ILog.Error(Exception e) { try { InternalWrite(TraceLevel.Error, "{0}", e); } finally { } }
		void ILog.Warning(Exception e) { try { InternalWrite(TraceLevel.Warning, "{0}", e); } finally { } }

		void ILog.Error(string format, params object[] args) { try { InternalWrite(TraceLevel.Error, format, args); } finally { } }
		void ILog.Warning(string format, params object[] args) { try { InternalWrite(TraceLevel.Warning, format, args); } finally { } }
		void ILog.Info(string format, params object[] args) { try { InternalWrite(TraceLevel.Info, format, args); } finally { } }
		void ILog.Verbose(string format, params object[] args) { try { InternalWrite(TraceLevel.Verbose, format, args); } finally { } }
		void ILog.Write(string format, params object[] args) { try { InternalWrite(TraceLevel.Off, format, args); } finally { } }

		IDisposable ILog.Start(string format, params object[] args)
		{
			try
			{
				if (args.Length > 0) format = String.Format(format, args);
				InternalWrite(TraceLevel.Verbose, "Start {0}", format);
			}
			catch (Exception e) { Trace.WriteLine(e.ToString(), "CSharpTest.Net.QuickLog.Write()"); }
			return new TaskInfo(format);
		}

		IDisposable ILog.AppStart(string format, params object[] args)
		{
			try
			{
				if (args.Length > 0) format = String.Format(format, args);
				InternalWrite(TraceLevel.Verbose, "Start {0}", format);
			}
			catch (Exception e) { Trace.WriteLine(e.ToString(), "CSharpTest.Net.QuickLog.Write()"); }
			return new TaskInfo(format);
		}
	}
	#endregion
}
