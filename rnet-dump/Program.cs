using CommandLine;
using remotenet_dump;


public class Program
{
    public static int Main(string[] args) =>
    Parser.Default.ParseArguments<TypesDumpOptions, HeapOptions, MembersDumpOptions, DomainsDumpOptions>(args)
        .MapResult(
        (HeapOptions options) => HeapDumper.DumpHeap(options),
        (TypesDumpOptions options) => TypesDumper.DumpTypes(options),
        (MembersDumpOptions options) => MembersDumper.DumpMembers(options),
        (DomainsDumpOptions options) => DomainsDumper.DumpDomains(options),
        errors => 1);
}