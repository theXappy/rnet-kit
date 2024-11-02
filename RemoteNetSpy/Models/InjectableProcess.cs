namespace RemoteNetSpy.Models;

public class InjectableProcess
{
    public int Pid { get; set; }
    public string Name { get; set; }
    public string DotNetVersion { get; set; }
    public string DiverState { get; set; }

    public InjectableProcess(int pid, string name, string dotNetVersion, string diverState)
    {
        Pid = pid;
        Name = name;
        DotNetVersion = dotNetVersion;
        DiverState = diverState;
    }
}