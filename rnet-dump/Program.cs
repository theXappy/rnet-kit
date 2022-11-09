using CommandLine;
using RemoteNET;

public class Program
{

    [Verb("heap", HelpText = "Dump the remote heap")]
    class HeapOptions
    {
        [Option('t', "target", Required = true, HelpText = "Target process name. Partial names are allowed but a single match is expected. " +
            "e.g. \"notep\" for notepad")]
        public string? TargetProcess { get; set; }
        [Option('q', "type-query", Required = false, Default = "*", HelpText = "Specific query for the Full Name of the types of objects to dump")]
        public string TypeQuery { get; set; }
    }
    [Verb("types", HelpText = "Dump all types matching a query")]
    class TypesDumpOptions
    {
        [Option('t', "target", Required = true, HelpText = "Target process name. Partial names are allowed but a single match is expected. " +
    "e.g. \"notep\" for notepad")]
        public string? TargetProcess { get; set; }
        [Option('q', "type-query", Required = true, HelpText = "Query for the Full Name of the types to find")]
        public string Query { get; set; }
    }
    [Verb("members", HelpText = "Dump members (properties, methods, fields, events, and so on) for a type")]
    class MembersDumpOptions
    {
        [Option('t', "target", Required = true, HelpText = "Target process name. Partial names are allowed but a single match is expected. " +
            "e.g. \"notep\" for notepad")]
        public string? TargetProcess { get; set; }
        [Option('q', "type-query", Required = true, HelpText = "Query for the Full Name of the type to dump")]
        public string Query { get; set; }
    }

    static int DumpTypes(TypesDumpOptions opts)
    {
        Console.WriteLine("Loading...");
        List<CandidateType>? candidates = null;
        try
        {
            using var app = RemoteApp.Connect(opts.TargetProcess);
            candidates = app.QueryTypes(opts.Query).ToList();
        }
        catch (Exception e)
        {
            Console.WriteLine("ERROR: " + e);
            return 1;
        }

        if (candidates == null || !candidates.Any())
        {
            Console.WriteLine("ERROR: Failed to find any remote type for the given query");
            return 1;
        }

        Console.WriteLine($"Matches:");
        foreach (var type in candidates)
        {
            Console.WriteLine($"[{type.Assembly}] {type.TypeFullName}");
        }
        return 0;
    }

    static int DumpMembers(MembersDumpOptions opts)
    {
        Console.WriteLine("Loading...");
        Type dumpedType;
        try
        {
            using var app = RemoteApp.Connect(opts.TargetProcess);
            dumpedType = app.GetRemoteType(opts.Query);
        }
        catch (Exception e)
        {
            Console.WriteLine("ERROR: " + e);
            return 1;
        }

        if (dumpedType == null)
        {
            Console.WriteLine("ERROR: Failed to find remote type for given query");
            return 1;
        }

        Console.WriteLine($"Members of type {dumpedType.FullName}:");
        foreach (var member in dumpedType.GetMembers())
        {
            Console.WriteLine($"[{member.MemberType}] {member}");
        }
        return 0;
    }

    static int DumpHeap(HeapOptions opts)
    {
        Console.WriteLine("Loading...");
        try
        {
            using var app = RemoteApp.Connect(opts.TargetProcess);
            var matches = app.QueryInstances(opts.TypeQuery, false).ToList();
            Console.WriteLine($"Found {matches.Count} objects.");
            foreach (var candidate in matches)
            {
                Console.WriteLine($"0x{candidate.Address:X8} {candidate.TypeFullName}");
            }
            return matches.Count > 0 ? 0 : 1;
        }
        catch (Exception e)
        {
            Console.WriteLine("ERROR: " + e);
            return 1;
        }
    }

    public static int Main(string[] args) =>
    Parser.Default.ParseArguments<TypesDumpOptions, HeapOptions, MembersDumpOptions>(args)
        .MapResult(
        (HeapOptions options) => DumpHeap(options),
        (TypesDumpOptions options) => DumpTypes(options),
        (MembersDumpOptions options) => DumpMembers(options),
        errors => 1);
}