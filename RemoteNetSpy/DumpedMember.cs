namespace RemoteNetGui;

public class DumpedMember
{
    public string RawName { get; set; }
    // This one has generic args normalized from [[System.Byte, ... ]] to <System.Byte>
    public string NormalizedName { get; set; }
}