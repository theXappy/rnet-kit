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
                    catch (Exception e)
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
                foreach (var methodTableInfo in rttiType.GetMethodTables())
                {
                    if (!opts.SkipPrintRaw)
                        Console.WriteLine($"[MethodTable] {methodTableInfo.Name} = 0x{methodTableInfo.StartAddress:x16}");

                    if (opts.PrintNormalizedGenerics)
                    {
                        string decoratedMethodTable = methodTableInfo.Name;
                        string undecoratedMethodTable = UndecorateMethodTableName(decoratedMethodTable);

                        Console.WriteLine($"[MethodTable] {undecoratedMethodTable} = 0x{methodTableInfo.StartAddress:x16}");
                    }
                }
            }
        }

        private static string UndecorateMethodTableName(string decoratedMethodTable)
        {
            string undecoratedMethodTable = UnDecorateSymbolNameWrapper(decoratedMethodTable);
            // If it's a vftable of a parent class, we need use this hack to parse the parent's name.
            // Example input: ??_7ObjectAbcde@SomNamespace@@6BParentComponent@1@@
            // Expected parent name: "ParentComponent"
            int parentNameStart = decoratedMethodTable.LastIndexOf("@@6B");
            if (parentNameStart != -1)
            {
                parentNameStart += "@@6B".Length;
                int parentNameEnd = decoratedMethodTable.IndexOf("@", parentNameStart);
                int length = parentNameEnd - parentNameStart;
                if (parentNameEnd != -1 && length > 0)
                {
                    string parent = decoratedMethodTable.Substring(parentNameStart, length);
                    undecoratedMethodTable += $"{{for {parent}}}";
                }
            }

            return undecoratedMethodTable;
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
            if (info is IRttiMethodBase mi)
                return mi.UndecoratedSignature;

            return UnDecorateSymbolNameWrapper(info.Name);
        }
    }
}
