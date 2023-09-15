using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace remotenet_dump
{
    public static class HeapDumper
    {
        public static int DumpHeap(HeapOptions opts)
        {
            Console.WriteLine("Loading...");
            try
            {
                using var app = Common.Connect(opts.TargetProcess, opts.Unmanaged);
                var matches = app.QueryInstances(opts.TypeQuery, false).ToList();
                Console.WriteLine($"Found {matches.Count} objects.");
                foreach (var candidate in matches)
                {
                    if (candidate.TypeFullName.Contains('\n') ||
                        candidate.TypeFullName.Contains('\r'))
                    {
                        continue;
                    }

                    Console.WriteLine($"0x{candidate.Address:X8} {candidate.TypeFullName}");
                }
                return matches.Count > 0 ? 0 : 1;
            }
            catch (Exception e)
            {
                Console.WriteLine("ERROR: " + e);
                return 1;
            }
        }
    }
}
