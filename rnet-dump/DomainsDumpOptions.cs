using CommandLine;

[Verb("domains", HelpText = "Dump all domains and list modules within")]
public class DomainsDumpOptions
{
    [Option('t', "target", Required = true, HelpText = "Target process name. Partial names are allowed but a single match is expected. " +
                                                       "e.g. \"notep\" for notepad")]
    public string TargetProcess { get; set; }
    [Option('u', "unmanaged", Required = false, HelpText = "Whether the target is an native app")]
    public bool Unmanaged { get; set; }
}