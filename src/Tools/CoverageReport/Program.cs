﻿#region Copyright 2009 by Roger Knapp, Licensed under the Apache License, Version 2.0
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
using CSharpTest.Net.CoverageReport.Reader;
using CSharpTest.Net.Utils;
using System.Xml;
using System.Collections.Generic;
using System.IO;
using CSharpTest.Net.CoverageReport.Counters;
using System.Text;

namespace CSharpTest.Net.CoverageReport
{
	static class Program
	{
		public static int DoHelp()
		{
			Console.WriteLine("");
			Console.WriteLine("Usage:");
			Console.WriteLine("    CoverageReport.exe [/s] [/wait] /module=[outputfile] /namespace=[outputfile]");
			Console.WriteLine("        /combine=[outputfile] /class=[outputfile] /exclude=[regex pattern] ");
			Console.WriteLine("        coverage.xml [coverage2.xml] [coverage*.xml]");
			Console.WriteLine("");
			Console.WriteLine("        /module will write a module-based summary of the coverage data");
			Console.WriteLine("        /namespace will write a namespace-based summary of the coverage data");
			Console.WriteLine("        /class will write a class-based summary of the coverage data");
			Console.WriteLine("        /combine will rewrite a combined coverage data file");
			Console.WriteLine("");
			Console.WriteLine("        Always list one or more coverage xml files generated by NCover 1.x");
			Console.WriteLine("        You may supply a relative or qualified file path and simple wildcards");
			Console.WriteLine("        are allowed (* and ?).");
			Console.WriteLine("");
			Console.WriteLine("        /exclude will ignore classes/methods whos full name matches the provided");
			Console.WriteLine("        regex pattern.  This argument can be specified multiple times.");
			Console.WriteLine("");
			Console.WriteLine("        /s will recurse directories named on the command-line to match the files");
			Console.WriteLine("");
			Console.WriteLine("        /nologo Hide the startup message");
			Console.WriteLine("");
			Console.WriteLine("        /wait after processing wait for user input");
			return 0;
		}

		[STAThread]
		static int Main(string[] raw)
		{
			ArgumentList args = new ArgumentList(raw);
			using (Log.AppStart(Environment.CommandLine))
			{
				if (args.Count == 0 || args.Unnamed.Count == 0 || args.Contains("?"))
					return DoHelp();
				if (args.Contains("nologo") == false)
				{
					Console.WriteLine("CSharpTest.Net.CoverageReport.exe");
					Console.WriteLine("Copyright 2009 by Roger Knapp, Licensed under the Apache License, Version 2.0");
					Console.WriteLine("");
				}

				try
				{
					List<string> filesFound = new List<string>();
					FileList files = new FileList();
					files.RecurseFolders = args.Contains("s");
					files.Add(new List<string>(args.Unnamed).ToArray());

					XmlParser parser = new XmlParser( args.SafeGet("exclude").Values );
					foreach (System.IO.FileInfo file in files)
					{
						filesFound.Add(file.FullName);
						using(Log.Start("parsing file: {0}", file.FullName))
							parser.Parse(file.FullName);
					}

					parser.Complete();

					if (args.Contains("module"))
					{
						using (Log.Start("Creating module report."))
						using (XmlReport rpt = new XmlReport(OpenText(args["module"]), parser, "Module Summary", filesFound.ToArray()))
							new MetricReport(parser.ByModule).Write(rpt);
					}

					if (args.Contains("namespace"))
					{
						using (Log.Start("Creating namespace report."))
						using (XmlReport rpt = new XmlReport(OpenText(args["namespace"]), parser, "Namespace Summary", filesFound.ToArray()))
							new MetricReport(parser.ByNamespace).Write(rpt);
					}

					if (args.Contains("class"))
					{
						using (Log.Start("Creating class report."))
						using (XmlReport rpt = new XmlReport(OpenText(args["class"]), parser, "Module Class Summary", filesFound.ToArray()))
							new MetricReport(parser.ByModule, parser.ByNamespace, parser.ByClass).Write(rpt);
					}

					if (args.Contains("combine"))
					{
						using (Log.Start("Creating combined coverage file."))
						using (XmlCoverageWriter wtr = new XmlCoverageWriter(OpenText(args["combine"]), parser))
							new MetricReport(parser.ByModule, parser.ByMethod).Write(wtr);
					}
					//foreach (ModuleInfo mi in parser)
					//    Console.WriteLine("{0} hit {1} of {2} for {3}", mi.Name, mi.VisitedPoints, mi.SequencePoints, mi.CoveragePercent);

				}
				catch (Exception e)
				{
					Log.Error(e);
					Console.Error.WriteLine();
					Console.Error.WriteLine("Exception: {0}", e.Message);
					Console.Error.WriteLine();
					Environment.ExitCode = -1;
				}
			}

			if (args.Contains("wait"))
			{
				Console.WriteLine("Press [Enter] to continue...");
				Console.ReadLine();
			}

			return Environment.ExitCode;
		}

		static TextWriter OpenText(string filename)
		{
			try
			{
				filename = Path.GetFullPath(filename);
				string dir = Path.GetDirectoryName(filename);
				Directory.CreateDirectory(dir);

				string xslFile = Path.Combine(dir, "CoverageReport.xsl");
				if (!File.Exists(xslFile))
				{
					using (Stream sin = typeof(Program).Assembly.GetManifestResourceStream(typeof(Program).Namespace + ".CoverageReport.xsl"))
					using (Stream sout = File.Create(xslFile))
					{
						byte[] buffer = new byte[short.MaxValue];
						int len;
						while (0 != (len = sin.Read(buffer, 0, buffer.Length)))
							sout.Write(buffer, 0, len);
						sout.Flush();
					}
				}

				return File.CreateText(filename);
			}
			catch(Exception e)
			{	
				Log.Error(e);
				Console.Error.WriteLine("Unable to generate report file: '{0}', reason = {1}.", filename, e.Message);
				return new StringWriter();
			}
		}
	}
}