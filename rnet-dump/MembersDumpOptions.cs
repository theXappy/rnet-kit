using CommandLine;

[Verb("members", HelpText = "Dump members (properties, methods, fields, events, and so on) for a type")]
public class MembersDumpOptions
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