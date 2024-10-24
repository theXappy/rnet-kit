using RemoteNET;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ScubaDiver.API.Interactions.Dumps;

namespace remotenet_dump
{
    internal class DomainsDumper
    {
        public static int DumpDomains(DomainsDumpOptions opts)
        {
            Console.WriteLine("Loading...");
            DomainsDump domains = null;
            try
            {
                using RemoteApp app = Common.Connect(opts.TargetProcess, opts.Unmanaged);
                domains = app.Communicator.DumpDomains();
            }
            catch (Exception e)
            {
                Console.WriteLine("ERROR: " + e);
                return 1;
            }

            if (domains == null || !domains.AvailableDomains.Any())
            {
                Console.WriteLine("ERROR: Failed to find any domains for the given query");
                return 1;
            }

            StringBuilder sb = new StringBuilder();
            Console.WriteLine($"Matches:");
            foreach (DomainsDump.AvailableDomain domain in domains.AvailableDomains)
            {
                sb.AppendLine($"[domain] {domain.Name}");
                foreach (string module in domain.AvailableModules)
                {
                    sb.AppendLine($"[module] {module}");
                }
            }

            Console.WriteLine(sb.ToString());
            return 0;
        }
    }
}
