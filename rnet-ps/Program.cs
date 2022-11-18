using RemoteNET.Internal.Extensions;
using System.Diagnostics;
using RemoteNET.Internal;

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
        DiverState status = DiverDiscovery.QueryStatus(proc);
        string diverStatusString = "";
        switch (status)
        {
            case DiverState.NoDiver:
                break;
            case DiverState.Alive:
                diverStatusString = "[Diver Injected]";
                break;
            case DiverState.Corpse:
                diverStatusString = "[Dead Diver! Restart this process before targeting]";
                break;
            case DiverState.HollowSnapshot:
                diverStatusString = "[Hollow Snapshot. Select parent with Diver instead]";
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
