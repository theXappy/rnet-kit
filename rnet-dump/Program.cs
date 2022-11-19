using System.Reflection;
using System.Text.RegularExpressions;
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
        [Option('r', "raw_generics", Default = true, HelpText = "Whether to print the un-normalize types names. This mean generic types are less readable.")]
        public bool PrintRaw { get; set; }
        [Option('n', "normalized_generics", Default = false, HelpText = "Whether to print normalize inner generic types ([[System.Byte, ..., PublicKey=...]] to System.Byte)")]
        public bool PrintNormalizedGenerics { get; set; }
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

        string target = opts.Query;

        // Convert SomeType<T1,T2> --to--> SomeType`2
        if (target.Contains('<') && target.EndsWith('>'))
        {
            string genericPart = target.Substring(target.IndexOf('<'));
            int numGenericArgs = genericPart.Count(c => c == ',') + 1;
            target = target.Substring(0, target.IndexOf('<')) + $"`{numGenericArgs}";
        }

        Type dumpedType;
        try
        {
            using var app = RemoteApp.Connect(opts.TargetProcess);
            dumpedType = app.GetRemoteType(target);
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

        Regex genericPartRegex = new Regex(@"`\d+\[\[.*?\]\]");
        Regex r = new Regex(@"\[(.*?), .*?\]");
        Console.WriteLine($"Members of type {dumpedType.FullName}:");
        foreach (var member in dumpedType.GetMembers())
        {
            if(opts.PrintRaw)
                Console.WriteLine($"[{member.MemberType}] {member}");

            if (opts.PrintNormalizedGenerics)
            {
                string memberString = member.ToString();
                var matches = genericPartRegex.Matches(memberString);
                while (matches.Any())
                {
                    Match match = matches.First();
                    string matchData = match.Groups[0].ToString();
                    // This line will give us "`2[System.String, System.Byte]
                    string withTypesNormalized = r.Replace(matchData, $"$1");

                    // This line will give us "<System.String, System.Byte>"
                    string withTriangles = "<" +
                                        withTypesNormalized[(withTypesNormalized.IndexOf('[') + 1)..^1]
                                        + ">";

                    memberString = memberString.Replace(matchData, withTriangles);

                    matches = genericPartRegex.Matches(memberString);
                }
                Console.WriteLine($"[{member.MemberType}] {memberString}");
            }
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