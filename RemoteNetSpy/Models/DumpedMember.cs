using System.Diagnostics;

namespace RemoteNetSpy.Models;

[DebuggerDisplay("DumpedMember: {RawName}")]
public class DumpedMember
{
    public string MemberType => RawName.Substring(1, RawName.IndexOf(']') - 1);
    public string RawName { get; set; }
    // This one has generic args normalized from [[System.Byte, ... ]] to <System.Byte>
    public string NormalizedName { get; set; }
}