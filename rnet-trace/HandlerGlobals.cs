using System.Text;
using RemoteNET;

namespace RemotenetTrace;

public class HandlerGlobals
{
    public RemoteApp App { get; set; }
    public TraceContext Context { get; set; }
    public dynamic Instance { get; set; }
    public dynamic[] Args { get; set; }
    public StringBuilder Output { get; set; }
}