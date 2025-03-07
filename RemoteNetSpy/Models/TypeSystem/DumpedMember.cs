using RemoteNET.Common;
using RemoteNET.Internal.Reflection;
using System.Diagnostics;
using System.Reflection;

namespace RemoteNetSpy.Models;

[DebuggerDisplay("DumpedMember: {RawName}")]
public class DumpedMember
{
    private MemberInfo mi;

    public DumpedMember(MemberInfo mi)
    {
        this.mi = mi;
    }

    public string MemberType => mi.MemberType.ToString();
    public string RawName => (mi as RemoteRttiMethodInfo)?.MangledName;
    // This one has generic args normalized from [[System.Byte, ... ]] to <System.Byte>
    public string NormalizedName => (mi as IRttiMethodBase)?.UndecoratedSignature ?? mi.Name;
}