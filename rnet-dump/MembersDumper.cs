using System.Diagnostics;
using RemoteNET.RttiReflection;
using RemoteNET;
using RnetKit.Common;
using System.Reflection;
using System.Text;
using RemoteNET.Common;

namespace remotenet_dump
{
    public class MembersDumper
    {
        public static int DumpMembers(MembersDumpOptions opts)
        {
            Console.WriteLine("Loading...");

            string target = opts.Query;

            // Convert Fix managed generic queries: SomeType<T1,T2> --to--> SomeType`2
            if (target.Contains('<') && target.EndsWith('>'))
                target = TypeNameUtils.DenormalizeShort(target);

            Type dumpedType;
            RemoteApp app;
            try
            {
                app = Common.Connect(opts.TargetProcess, opts.Unmanaged);
                dumpedType = app.GetRemoteType(target);

            }
            catch (Exception e)
            {
                Console.WriteLine("ERROR: " + e);
                return 1;
            }

            if (dumpedType == null)
            {
                Console.WriteLine("ERROR: Failed to find remote type for given query");
                app.Dispose();
                return 1;
            }

            Console.WriteLine($"Members of type {dumpedType.FullName}:");
            if (opts.Unmanaged)
            {
                DumpMembersUnmanaged(opts, dumpedType);
            }
            else
            {
                DumpMembersManaged(opts, dumpedType);
            }

            app.Dispose();
            return 0;
        }

        private static void DumpMembersManaged(MembersDumpOptions opts, Type dumpedType)
        {
            foreach (MemberInfo member in dumpedType.GetMembers())
            {
                if (!opts.SkipPrintRaw)
                    Console.WriteLine($"[{member.MemberType}] {member}");

                if (opts.PrintNormalizedGenerics)
                {
                    string memberString;
                    try
                    {
                        memberString = TypeNameUtils.Normalize(member);
                    }
                    catch(Exception e) 
                    {
                        Debug.WriteLine($"ERROR when normalizing this member: {member}. Exception:\n{e}");
                        memberString = member.ToString();
                    }
                    Console.WriteLine($"[{member.MemberType}] {memberString}");
                }
            }
        }

        private static void DumpMembersUnmanaged(MembersDumpOptions opts, Type dumpedType)
        {
            foreach (MemberInfo member in dumpedType.GetMembers())
            {
                if (!opts.SkipPrintRaw)
                    Console.WriteLine($"[{member.MemberType}] {member}");

                if (opts.PrintNormalizedGenerics)
                {
                    string memberString = UnDecorateSymbolNameWrapper(member);
                    Console.WriteLine($"[{member.MemberType}] {memberString}");
                }
            }

            if (dumpedType is RemoteRttiType rttiType)
            {
                foreach (string member in rttiType.UnresolvedMembers)
                {
                    if (!opts.SkipPrintRaw)
                        Console.WriteLine($"[Unknown Member] {member}");

                    if (opts.PrintNormalizedGenerics)
                    {
                        string memberString = UnDecorateSymbolNameWrapper(member);
                        Console.WriteLine($"[Unknown Member] {memberString}");
                    }
                }
                foreach (KeyValuePair<string, IntPtr> kvp in rttiType.MethodTables)
                {
                    if (!opts.SkipPrintRaw)
                        Console.WriteLine($"[MethodTable] {kvp.Key} = 0x{kvp.Value:x16}");

                    if (opts.PrintNormalizedGenerics)
                    {
                        string undecoratedMethodTable = UnDecorateSymbolNameWrapper(kvp.Key);
                        Console.WriteLine($"[MethodTable] {undecoratedMethodTable} = 0x{kvp.Value:x16}");
                    }
                }
            }
        }

        private const int BUFFER_SIZE = 256;
        public static string UnDecorateSymbolNameWrapper(string buffer)
        {
            unsafe
            {
                byte* target = stackalloc byte[BUFFER_SIZE];
                uint len = Windows.Win32.PInvoke.UnDecorateSymbolName(buffer, new Windows.Win32.Foundation.PSTR(target), BUFFER_SIZE, 0x1800);
                return len != 0 ? Encoding.UTF8.GetString(target, (int)len) : null;
            }
        }
        public static string UnDecorateSymbolNameWrapper(MemberInfo info)
        {
            if(info is IRttiMethodBase mi)
                    return mi.UndecoratedSignature;

            return UnDecorateSymbolNameWrapper(info.Name);
        }
    }
}
