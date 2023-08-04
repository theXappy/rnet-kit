using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using CommandLine;
using RemoteNET;
using RemoteNET.Internal.Reflection;
using RemoteNET.RttiReflection;
using RnetKit.Common;

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
        [Option('u', "unmanaged", Required = false, HelpText = "Whether the target is an native app")]
        public bool Unmanaged { get; set; }
    }
    [Verb("types", HelpText = "Dump all types matching a query")]
    class TypesDumpOptions
    {
        [Option('t', "target", Required = true, HelpText = "Target process name. Partial names are allowed but a single match is expected. " +
    "e.g. \"notep\" for notepad")]
        public string? TargetProcess { get; set; }
        [Option('q', "type-query", Required = true, HelpText = "Query for the Full Name of the types to find")]
        public string Query { get; set; }
        [Option('u', "unmanaged", Required = false, HelpText = "Whether the target is an native app")]
        public bool Unmanaged { get; set; }
    }
    [Verb("members", HelpText = "Dump members (properties, methods, fields, events, and so on) for a type")]
    class MembersDumpOptions
    {
        [Option('t', "target", Required = true, HelpText = "Target process name. Partial names are allowed but a single match is expected. " +
            "e.g. \"notep\" for notepad")]
        public string? TargetProcess { get; set; }
        [Option('q', "type-query", Required = true, HelpText = "Query for the Full Name of the type to dump")]
        public string Query { get; set; }
        [Option('r', "disable_raw_generics", Default = false, HelpText = "Whether to skip printing the un-normalize types names. Those are less readable than normalized.")]
        public bool SkipPrintRaw { get; set; }
        [Option('n', "normalized_generics", Default = false, HelpText = "Whether to print normalize inner generic types ([[System.Byte, ..., PublicKey=...]] to System.Byte)")]
        public bool PrintNormalizedGenerics { get; set; }
        [Option('u', "unmanaged", Required = false, HelpText = "Whether the target is an native app")]
        public bool Unmanaged { get; set; }
    }


    private static RemoteApp Connect(string targetQuery, bool unmanaged)
    {
        RuntimeType runtime = RuntimeType.Managed;
        if (unmanaged)
            runtime = RuntimeType.Unmanaged;

        Process targetProc = null;
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

    static int DumpTypes(TypesDumpOptions opts)
    {
        bool IsReasonableUnmanagedTypeName(string str)
        {
            if (str.Length < 2)
                return false;
            bool valid = !str.Any(Char.IsControl);
            if (!valid)
                return false;
            valid = !str.Any(c => c > 0xff || c == '\r' || c == '\n' || c == '"');
            if (!valid)
                return false;
            return valid;
        }

        Console.WriteLine("Loading...");
        List<CandidateType>? candidates = null;
        try
        {
            using RemoteApp app = Connect(opts.TargetProcess, opts.Unmanaged);
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

        StringBuilder sb = new StringBuilder();
        Console.WriteLine($"Matches:");
        foreach (var type in candidates)
        {
            // Skip types with unreasonable names. Our unmanaged types discovery logic is *very* permissive.
            if (type.Runtime == RuntimeType.Unmanaged && !IsReasonableUnmanagedTypeName(type.TypeFullName))
                continue;

            sb.AppendLine($"[{type.Runtime}][{type.Assembly}] {type.TypeFullName}");
        }

        Console.WriteLine(sb.ToString());
        return 0;
    }


    static int DumpMembers(MembersDumpOptions opts)
    {
        Console.WriteLine("Loading...");

        string target = opts.Query;

        // Convert SomeType<T1,T2> --to--> SomeType`2
        if (target.Contains('<') && target.EndsWith('>'))
        {
            target = TypeNameUtils.DenormalizeShort(target);
        }

        Type dumpedType;
        try
        {
            using RemoteApp app = Connect(opts.TargetProcess, opts.Unmanaged);
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

        Console.WriteLine($"Members of type {dumpedType.FullName}:");
        foreach (MemberInfo member in dumpedType.GetMembers())
        {
            if (!opts.SkipPrintRaw)
                Console.WriteLine($"[{member.MemberType}] {member}");

            if (opts.PrintNormalizedGenerics)
            {
                string memberString;
                if (opts.Unmanaged)
                    memberString = UnDecorateSymbolNameWrapper(member);
                else
                    memberString = TypeNameUtils.Normalize(member);
                Console.WriteLine($"[{member.MemberType}] {memberString}");
            }
        }

        if (dumpedType is RemoteRttiType rttiType)
        {
            foreach (string member in rttiType.UnresolvedMembers)
            {
                if (!opts.SkipPrintRaw)
                    Console.WriteLine($"[Unknown Member] {member}");

                if (opts.PrintNormalizedGenerics)
                {
                    string memberString = UnDecorateSymbolNameWrapper(member);
                    Console.WriteLine($"[Unknown Member] {memberString}");
                }
            }
        }

        return 0;
    }

    private const int BUFFER_SIZE = 256;
    public static string UnDecorateSymbolNameWrapper(string buffer)
    {
        unsafe
        {
            byte* target = stackalloc byte[BUFFER_SIZE];
            uint len = Windows.Win32.PInvoke.UnDecorateSymbolName(buffer, new Windows.Win32.Foundation.PSTR(target), BUFFER_SIZE, 0x1800);
            return len != 0 ? Encoding.UTF8.GetString(target, (int)len) : null;
        }
    }
    public static string UnDecorateSymbolNameWrapper(MemberInfo info)
    {
        switch (info)
        {
            case RemoteRttiMethodInfo mi:
                return mi.UndecoratedSignature;
        }

        return UnDecorateSymbolNameWrapper(info.Name);
    }

    static int DumpHeap(HeapOptions opts)
    {
        Console.WriteLine("Loading...");
        try
        {
            using var app = Connect(opts.TargetProcess, opts.Unmanaged);
            var matches = app.QueryInstances(opts.TypeQuery, false).ToList();
            Console.WriteLine($"Found {matches.Count} objects.");
            foreach (var candidate in matches)
            {
                if (candidate.TypeFullName.Contains('\n') ||
                    candidate.TypeFullName.Contains('\r'))
                {
                    continue;
                }

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