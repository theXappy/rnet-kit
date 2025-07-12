using RemoteNET.Common;
using RemoteNET.Internal.Reflection;
using System.Diagnostics;
using System.Reflection;

namespace RemoteNetSpy.Models;

[DebuggerDisplay("DumpedMember: {RawName}")]
public class DumpedMember
{
    public MemberInfo MemberInfo { get; set; }

    public DumpedMember(MemberInfo mi)
    {
        MemberInfo = mi;
    }

    public string MemberType => MemberInfo.MemberType.ToString();
    public string RawName => (MemberInfo as RemoteRttiMethodInfo)?.MangledName;
    // This one has generic args normalized from [[System.Byte, ... ]] to <System.Byte>
    public string NormalizedName => (MemberInfo as IRttiMethodBase)?.UndecoratedSignature ?? MemberInfo.Name;
}