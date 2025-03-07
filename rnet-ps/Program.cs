using RemoteNET.Internal.Extensions;
using System.Diagnostics;
using RemoteNET.Internal;
using System.Net.NetworkInformation;

const string HOLLOW_SNAPSHOT = "[No Threads Found. Possibly a Hollow Snapshot (Select parent with Diver instead)]";

int ourPid = Process.GetCurrentProcess().Id;
var allProcs = Process.GetProcesses();

IPGlobalProperties ipGlobalProperties = IPGlobalProperties.GetIPGlobalProperties();
System.Net.IPEndPoint[] tcpConnInfoArray = ipGlobalProperties.GetActiveTcpListeners();
HashSet<int> usedPorts = tcpConnInfoArray.Select(conInfo => conInfo.Port).ToHashSet();

Queue<Task<string>> _tasks = new Queue<Task<string>>();
foreach (var proc in allProcs.OrderBy(proc => proc.ProcessName))
{
    // Skip our own process and the 'Idle' process (ID = 0)
    if (proc.Id == 0 || proc.Id == ourPid)
    {
        continue;
    }

    string dotNetVer;
    try
    {
        dotNetVer = proc.GetSupportedTargetFramework();
    }
    catch
    {
        dotNetVer = "<error>";
    }


    if (!usedPorts.Contains(proc.Id))
    {
        string noThreadsIndication = string.Empty;
        if (proc.Threads.Count == 0)
        {
            noThreadsIndication = HOLLOW_SNAPSHOT;
        }
        _tasks.Enqueue(Task.FromResult($"{proc.Id,6}\t{proc.ProcessName,-40}\t{dotNetVer,-10}\t{noThreadsIndication}"));
    }
    else
    {
        _tasks.Enqueue(Task.Factory.StartNew(() =>
        {
            DiverDiscovery.QueryStatus(proc, out DiverState managedState, out DiverState unmanagedDiverState);
            string diverStatusString = "";
            if (unmanagedDiverState == DiverState.Alive)
            {
                diverStatusString += "[Unmanaged Diver Injected]";
            }
            switch (managedState)
            {
                case DiverState.NoDiver:
                    break;
                case DiverState.Alive:
                    diverStatusString += "[Diver Injected]";
                    break;
                case DiverState.Corpse:
                    diverStatusString += "[Dead Diver! Restart this process before targeting]";
                    break;
                case DiverState.HollowSnapshot:
                    diverStatusString += HOLLOW_SNAPSHOT;
                    break;
            }

            return $"{proc.Id,6}\t{proc.ProcessName,-40}\t{dotNetVer,-10}\t{diverStatusString}";
        }));
    }
}

Console.WriteLine("ID\tName\t\t\t\t\t\tDetected .NET\tDiver Status");
foreach (var task in _tasks)
{
    string res = task.Result;
    Console.WriteLine(res);
}
