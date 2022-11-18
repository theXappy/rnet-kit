using System.Text;

namespace RemotenetTrace;

public class HandlerGlobals
{
    public TraceContext Context { get; set; }
    public dynamic Instance { get; set; }
    public dynamic[] Args { get; set; }
    public StringBuilder Output { get; set; }
}