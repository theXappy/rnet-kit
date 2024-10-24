using CommandLine;

[Verb("heap", HelpText = "Dump the remote heap")]
public class HeapOptions
{
    [Option('t', "target", Required = true, HelpText = "Target process name. Partial names are allowed but a single match is expected. " +
                                                       "e.g. \"notep\" for notepad")]
    public string TargetProcess { get; set; }
    [Option('q', "type-query", Required = false, Default = "*", HelpText = "Specific query for the Full Name of the types of objects to dump")]
    public string TypeQuery { get; set; }
    [Option('u', "unmanaged", Required = false, HelpText = "Whether the target is an native app")]
    public bool Unmanaged { get; set; }
}