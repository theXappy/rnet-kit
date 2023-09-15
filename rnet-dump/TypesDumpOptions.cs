using CommandLine;

[Verb("types", HelpText = "Dump all types matching a query")]
public class TypesDumpOptions
{
    [Option('t', "target", Required = true, HelpText = "Target process name. Partial names are allowed but a single match is expected. " +
                                                       "e.g. \"notep\" for notepad")]
    public string? TargetProcess { get; set; }
    [Option('q', "type-query", Required = true, HelpText = "Query for the Full Name of the types to find")]
    public string Query { get; set; }
    [Option('u', "unmanaged", Required = false, HelpText = "Whether the target is an native app")]
    public bool Unmanaged { get; set; }
}