using CommandLine;
using System;
using System.Diagnostics;
using System.Reflection;

namespace rnet_class_dump
{
    public class Options
    {
        [Option('l', "filters_list", Required = false, HelpText = "Path to the type identifiers filters file. Can't be used with -f.")]
        public string? FiltersListPath { get; set; }

        [Option('f', "filter", Required = false, HelpText = "Single filter to use. Can't be used with -l.")]
        public string? Filter { get; set; }

        [Option('t', "target", Required = false, HelpText = "Target process name. Partial names are allowed but a single match is expected. e.g. \"notep\" for notepad. Can't be used with -s.")]
        public string? TargetProcess { get; set; }

        [Option("vessel", Required = false, HelpText = "Use vessel mode: launch rnet-vessel.exe as the target process. Can't be used with -t.")]
        public bool UseVessel { get; set; }

        [Option('u', "unmanaged", Required = false, HelpText = "Whether the target is an native app")]
        public bool Unmanaged { get; set; }

        [Option('v', "verbose", Required = false, HelpText = "Enable verbose output.")]
        public bool Verbose { get; set; }

        [Option('d', "dll", Required = false, HelpText = "Path to DLL to inject before dumping. Can be specified multiple times.")]
        public IEnumerable<string> DllPaths { get; set; } = new List<string>();

        [Option("launchdebugger", Required = false, HelpText = "Launch debugger and wait for it to attach.")]
        public bool LaunchDebugger { get; set; }
    }

    internal class ClassDumpProgram
    {
        static int Main(string[] args)
        {
            if (args?.Any(a => a == "--launchdebugger") ?? false)
            {
                Debugger.Launch();
            }


            Parser p = new Parser((settings) =>
            {
                settings.AllowMultiInstance = true;
                settings.AutoHelp = true;
                settings.AutoVersion = true;
                settings.HelpWriter = Parser.Default.Settings.HelpWriter;
            });
            return p.ParseArguments<Options>(args)
                .MapResult(
                    RunDump,
                    HandleParseError);
        }

        static int RunDump(Options opts)
        {
            // Validate mutually exclusive options
            if (opts.UseVessel && !string.IsNullOrEmpty(opts.TargetProcess))
            {
                Console.WriteLine("ERROR: Cannot use both -t (target) and --vessel options.");
                return 1;
            }

            if (!opts.UseVessel && string.IsNullOrEmpty(opts.TargetProcess))
            {
                Console.WriteLine("ERROR: Either -t (target) or --vessel must be specified.");
                return 1;
            }

            string[] filters;
            if (opts.FiltersListPath != null)
            {
                // Read filters from file
                filters = File.ReadAllLines(opts.FiltersListPath);
            }
            else if (opts.Filter != null) {
                // Use single filter
                filters = new[] { opts.Filter };
            }
            else
            {
                Console.WriteLine("ERROR: Either -l or -f must be specified.");
                return 1;
            }

            Process? vesselProcess = null;
            string targetProcess = opts.TargetProcess ?? string.Empty;

            try
            {
                // Launch vessel if requested
                if (opts.UseVessel)
                {
                    vesselProcess = LaunchVessel(opts.Verbose);
                    if (vesselProcess == null)
                    {
                        return 1;
                    }
                    targetProcess = vesselProcess.Id.ToString();
                    
                    if (opts.Verbose)
                    {
                        Console.Error.WriteLine($"Launched vessel process with PID: {vesselProcess.Id}");
                    }
                }

                // Inject DLLs if specified
                if (opts.DllPaths != null && opts.DllPaths.Any())
                {
                    foreach (string dllPath in opts.DllPaths)
                    {
                        if (opts.Verbose)
                        {
                            Console.Error.WriteLine($"Injecting DLL: {dllPath}");
                        }

                        int injectResult = InjectDll(dllPath, targetProcess, opts.Unmanaged, opts.Verbose);
                        if (injectResult != 0)
                        {
                            Console.WriteLine($"ERROR: Failed to inject DLL: {dllPath}");
                            return injectResult;
                        }

                        if (opts.Verbose)
                        {
                            Console.Error.WriteLine($"Successfully injected: {dllPath}");
                        }
                    }
                }

                // Pass the verbose flag to ClassDumper
                ClassDumper dumper = new ClassDumper(opts.Verbose);
                return dumper.DumpClasses(filters, targetProcess, opts.Unmanaged);
            }
            finally
            {
                // Clean up vessel process if we launched it
                if (vesselProcess != null)
                {
                    try
                    {
                        if (!vesselProcess.HasExited)
                        {
                            if (opts.Verbose)
                            {
                                Console.Error.WriteLine($"Killing vessel process (PID: {vesselProcess.Id})");
                            }
                            vesselProcess.Kill();
                            vesselProcess.WaitForExit(5000);
                        }
                    }
                    catch (Exception ex)
                    {
                        if (opts.Verbose)
                        {
                            Console.Error.WriteLine($"Warning: Failed to kill vessel process: {ex.Message}");
                        }
                    }
                    finally
                    {
                        vesselProcess.Dispose();
                    }
                }
            }
        }

        static int InjectDll(string dllPath, string targetProcess, bool unmanaged, bool verbose)
        {
            // Find rnet-inject.exe in the same directory as this executable
            string exeDirectory = AppDomain.CurrentDomain.BaseDirectory;
            string injectExePath = Path.Combine(exeDirectory, "rnet-inject.exe");

            if (!File.Exists(injectExePath))
            {
                Console.WriteLine($"ERROR: rnet-inject.exe not found at: {injectExePath}");
                return 1;
            }

            // Build command line arguments for rnet-inject
            string unmanagedFlag = unmanaged ? " -u" : "";
            string debuggerFlag = Debugger.IsAttached ? " --launchdebugger" : "";
            string arguments = $"-t \"{targetProcess}\" -d \"{dllPath}\"{unmanagedFlag}{debuggerFlag}";

            if (verbose)
            {
                Console.Error.WriteLine($"Executing: {injectExePath} {arguments}");
            }

            // Start rnet-inject process
            var processStartInfo = new ProcessStartInfo
            {
                FileName = injectExePath,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using (var process = Process.Start(processStartInfo))
            {
                if (process == null)
                {
                    Console.WriteLine("ERROR: Failed to start rnet-inject.exe");
                    return 1;
                }

                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();
                process.WaitForExit();

                if (verbose || process.ExitCode != 0)
                {
                    if (!string.IsNullOrEmpty(output))
                    {
                        Console.Error.WriteLine($"rnet-inject output: {output}");
                    }
                    if (!string.IsNullOrEmpty(error))
                    {
                        Console.Error.WriteLine($"rnet-inject error: {error}");
                    }
                }

                return process.ExitCode;
            }
        }

        static Process? LaunchVessel(bool verbose)
        {
            // Find rnet-vessel.exe in the same directory as this executable
            string exeDirectory = AppDomain.CurrentDomain.BaseDirectory;
            string vesselExePath = Path.Combine(exeDirectory, "rnet-vessel.exe");

            if (!File.Exists(vesselExePath))
            {
                Console.WriteLine($"ERROR: rnet-vessel.exe not found at: {vesselExePath}");
                return null;
            }

            if (verbose)
            {
                Console.Error.WriteLine($"Launching: {vesselExePath}");
            }

            // Start rnet-vessel process
            var processStartInfo = new ProcessStartInfo
            {
                FileName = vesselExePath,
                UseShellExecute = false,
                RedirectStandardOutput = false,
                RedirectStandardError = false,
                CreateNoWindow = false
            };

            try
            {
                Process? process = Process.Start(processStartInfo);
                if (process == null)
                {
                    Console.WriteLine("ERROR: Failed to start rnet-vessel.exe");
                    return null;
                }

                // Give the vessel process a moment to initialize
                System.Threading.Thread.Sleep(500);

                return process;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR: Failed to launch vessel: {ex.Message}");
                return null;
            }
        }

        static int HandleParseError(IEnumerable<Error> errs)
        {
            // Handle errors or return 1
            // CommandLineParser library automatically prints help text on error
            return 1;
        }
    }
}
