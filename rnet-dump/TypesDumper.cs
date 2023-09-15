using RemoteNET;
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
        List<CandidateType>? candidates = null;
        try
        {
            using RemoteApp app = Common.Connect(opts.TargetProcess, opts.Unmanaged);
            candidates = app.QueryTypes(opts.Query).ToList();
        }
        catch (Exception e)
        {
            Console.WriteLine("ERROR: " + e);
            return 1;
        }

        if (candidates == null || !candidates.Any())
        {
            Console.WriteLine("ERROR: Failed to find any remote type for the given query");
            return 1;
        }

        StringBuilder sb = new StringBuilder();
        Console.WriteLine($"Matches:");
        foreach (var type in candidates)
        {
            // Skip types with unreasonable names. Our unmanaged types discovery logic is *very* permissive.
            if (type.Runtime == RuntimeType.Unmanaged && !IsReasonableUnmanagedTypeName(type.TypeFullName))
                continue;

            sb.AppendLine($"[{type.Runtime}][{type.Assembly}] {type.TypeFullName}");
        }

        Console.WriteLine(sb.ToString());
        return 0;
    }
}