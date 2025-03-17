using RemoteNET;
using RemoteNET.Access;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace remotenet_dump
{
    public static class Common
    {
        public static RemoteApp Connect(string targetQuery, bool unmanaged)
        {
            RuntimeType runtime = RuntimeType.Managed;
            if (unmanaged)
                runtime = RuntimeType.Unmanaged;

            Process targetProc = null;
            if (int.TryParse(targetQuery, out int pid))
            {
                try
                {
                    targetProc = Process.GetProcessById(pid);
                }
                catch
                {
                    // ignored
                }
            }

            return targetProc != null ?
                RemoteAppFactory.Connect(targetProc, runtime) :
                RemoteAppFactory.Connect(targetQuery, runtime);
        }
    }
}
