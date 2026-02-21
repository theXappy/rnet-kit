using RemoteNET;
using RemoteNET.Utils;
using ScubaDiver.API.Interactions.Dumps;
using System.Text;

namespace remotenet_dump;

public static class TypesDumper
{
    public static int DumpTypes(TypesDumpOptions opts)
    {
        bool IsReasonableUnmanagedTypeName(string str)
        {
            if (str.Length < 2)
                return false;
            bool valid = !str.Any(Char.IsControl);
            if (!valid)
                return false;
            valid = !str.Any(c => c > 0xff || c == '\r' || c == '\n' || c == '"');
            if (!valid)
                return false;
            return valid;
        }

        Console.WriteLine("Loading...");
        List<CandidateType> candidates = null;
        List<TypesDump.AssemblyLoadError> loadErrors = null;
        try
        {
            using RemoteApp app = Common.Connect(opts.TargetProcess, opts.Unmanaged);
            candidates = TypesDumpHelpers.QueryTypes(app, opts.Query, out loadErrors);
        }
        catch (Exception e)
        {
            Console.WriteLine("ERROR: " + e);
            return 1;
        }

        if (loadErrors != null)
        {
            foreach (TypesDump.AssemblyLoadError loadError in loadErrors)
            {
                string errorMessage = loadError?.Error?.Error ?? "Unknown error";
                Console.Error.WriteLine($"[{RuntimeType.Managed}][{loadError?.Assembly}] {errorMessage}");
            }
        }

        if (candidates == null || !candidates.Any())
        {
            Console.WriteLine("ERROR: Failed to find any remote type for the given query");
            return 1;
        }

        StringBuilder sb = new StringBuilder();
        Console.WriteLine($"Matches:");
        foreach (CandidateType type in candidates)
        {
            // Skip types with unreasonable names. Our unmanaged types discovery logic is *very* permissive.
            if (type.Runtime == RuntimeType.Unmanaged && !IsReasonableUnmanagedTypeName(type.TypeFullName))
                continue;

            string methodTableStr = "null";
            if (type.MethodTable.HasValue)
            {
                methodTableStr = $"0x{type.MethodTable.Value.ToString("x16")}";
            }
            sb.AppendLine($"[{type.Runtime}][{type.Assembly}][{methodTableStr}] {type.TypeFullName}");
        }

        Console.WriteLine(sb.ToString());
        return 0;
    }
}