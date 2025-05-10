using CommandLine;
using RemoteNET;
using RemoteNET.Access;
using System.Diagnostics;

namespace rnet_inject
{
    public class Options
    {
        [Option('t', "target", Required = true, HelpText = "Target process name. Partial names are allowed but a single match is expected. e.g. \"notep\" for notepad")]
        public string? TargetProcess { get; set; }

        [Option('u', "unmanaged", Required = false, HelpText = "Whether the target is an native app")]
        public bool Unmanaged { get; set; }

        [Option('d', "dll_path", Required = true, HelpText = "Path to the DLL to inject.")]
        public string? DllPath { get; set; }
    }

    internal class Program
    {
        static int Main(string[] args)
        {
            return Parser.Default.ParseArguments<Options>(args)
                .MapResult(
                    (Options opts) => RunInject(opts),
                    errors => HandleParseError(errors));
        }

        static int RunInject(Options opts)
        {
            if (string.IsNullOrEmpty(opts.TargetProcess) || string.IsNullOrEmpty(opts.DllPath))
            {
                Console.WriteLine("Target process and DLL path are required.");
                return 1;
            }

            RemoteApp app = Connect(opts.TargetProcess, opts.Unmanaged);
            Console.WriteLine($"Injecting: {opts.DllPath}");
            bool res = opts.Unmanaged ? app.InjectDll(opts.DllPath) : app.InjectAssembly(opts.DllPath);
            Console.WriteLine(res ? "Injection succeeded." : "Injection failed.");
            return res ? 0 : 1;
        }

        public static RemoteApp Connect(string targetQuery, bool unmanaged)
        {
            RuntimeType runtime = RuntimeType.Managed;
            if (unmanaged)
                runtime = RuntimeType.Unmanaged;

            Process? targetProc = null;
            if (int.TryParse(targetQuery, out int pid))
            {
                try
                {
                    targetProc = Process.GetProcessById(pid);
                }
                catch
                {
                    // ignored
                }
            }

            return targetProc != null ?
                RemoteAppFactory.Connect(targetProc, runtime) :
                RemoteAppFactory.Connect(targetQuery, runtime);
        }

        static int HandleParseError(IEnumerable<Error> errs)
        {
            // Handle errors or return 1
            // CommandLineParser library automatically prints help text on error
            return 1;
        }
    }
}
