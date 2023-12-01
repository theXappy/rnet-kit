namespace RemoteNetGui;

public class DumpedMember
{
    public string MemberType => RawName.Substring(0, RawName.IndexOf(']') + 1);
    public string RawName { get; set; }
    // This one has generic args normalized from [[System.Byte, ... ]] to <System.Byte>
    public string NormalizedName { get; set; }
}