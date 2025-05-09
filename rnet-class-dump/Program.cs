using CommandLine;
using System;
using System.Reflection;

namespace rnet_class_dump
{
    public class Options
    {
        [Option('l', "filters_list", Required = false, HelpText = "Path to the type identifiers filters file. Can't be used with -f.")]
        public string? FiltersListPath { get; set; }

        [Option('f', "filter", Required = false, HelpText = "Single filter to use. Can't be used with -l.")]
        public string? Filter { get; set; }

        [Option('t', "target", Required = true, HelpText = "Target process name. Partial names are allowed but a single match is expected. e.g. \"notep\" for notepad")]
        public string TargetProcess { get; set; } = string.Empty;

        [Option('u', "unmanaged", Required = false, HelpText = "Whether the target is an native app")]
        public bool Unmanaged { get; set; }

        [Option('v', "verbose", Required = false, HelpText = "Enable verbose output.")]
        public bool Verbose { get; set; }
    }

    internal class Program
    {
        static int Main(string[] args)
        {
            return Parser.Default.ParseArguments<Options>(args)
                .MapResult(
                    RunDump,
                    HandleParseError);
        }

        static int RunDump(Options opts)
        {
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

            // Pass the verbose flag to ClassDumper
            return ClassDumper.DumpClasses(filters, opts.TargetProcess, opts.Unmanaged, opts.Verbose);
        }

        static int HandleParseError(IEnumerable<Error> errs)
        {
            // Handle errors or return 1
            // CommandLineParser library automatically prints help text on error
            return 1;
        }
    }
}
