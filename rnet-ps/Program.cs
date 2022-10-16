using RemoteNET.Internal.Extensions;
using System.Diagnostics;

int ourPid = Process.GetCurrentProcess().Id;
var allProcs = Process.GetProcesses();

Queue<Task<string>> _tasks = new Queue<Task<string>>();
foreach (var proc in allProcs.OrderBy(proc=>proc.ProcessName))
{
    // Skip our own process and the 'Idle' process (ID = 0)
    if(proc.Id == 0 || proc.Id == ourPid)
    {
        continue;
    }

    var dotNetVer = proc.GetSupportedTargetFramework();
    if (dotNetVer == "native")
    {
        // Not a .NET app, skip
        continue;
    }

    _tasks.Enqueue(Task.Factory.StartNew(() =>
    {
        var x = RemoteNET.Internal.Extensions.ProcessExt.GetModules(proc);
        RemoteNET.Internal.DiverState status = RemoteNET.Internal.DiverDiscovery.QueryStatus(proc);
        string diverStatusString = "";
        switch (status)
        {
            case RemoteNET.Internal.DiverState.NoDiver:
                break;
            case RemoteNET.Internal.DiverState.Alive:
                diverStatusString = "[Diver Injected]";
                break;
            case RemoteNET.Internal.DiverState.Corpse:
                diverStatusString = "[Dead Diver! Restart this process before targeting]";
                break;
        }

        return $"{proc.Id,6}\t{proc.ProcessName,-40}\t{dotNetVer,-10}\t{diverStatusString}";
    }));
}

Console.WriteLine("ID\tName\t\t\t\t\t\tDetected .NET\tDiver Status");
foreach (var task in _tasks)
{
    string res = task.Result;
    Console.WriteLine(res);
}
