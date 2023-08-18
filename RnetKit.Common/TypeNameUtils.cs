using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;

namespace RnetKit.Common
{
    public static class TypeNameUtils
    {
        public static string Normalize(MemberInfo mi)
        {
            switch (mi)
            {
                case FieldInfo fi:
                    return $"{Normalize(fi.FieldType)} {fi.Name}";
                case PropertyInfo pi:
                    return $"{Normalize(pi.PropertyType)} {pi.Name}";
                case EventInfo ei:
                    return $"{Normalize(ei.EventHandlerType)} {ei.Name}";
                case MethodInfo mtd:
                    return NormalizeMethodBase(mtd as MethodBase);
            }
            return mi.ToString();
        }

        public static string NormalizeMethodBase(MethodBase mi)
        {
            switch (mi)
            {
                case MethodInfo mtd:
                    string normalizedParams = Normalize(mtd.GetParameters());
                    return $"{Normalize(mtd.ReturnType)} {mtd.Name}({normalizedParams})";
            }
            return mi.ToString();
        }

        public static string Normalize(ParameterInfo[] pis)
        {
            string NormalizeParam(ParameterInfo pi)
            {
                return $"{Normalize(pi.ParameterType)} {pi.Name}";
            }
            return string.Join(", ", pis.Select(NormalizeParam));
        }

        public static string Normalize(Type t)
        {
            if (t == typeof(Int16))
                return "short";
            else if (t == typeof(UInt16))
                return "ushort";
            else if (t == typeof(Int32))
                return "int";
            else if (t == typeof(UInt32))
                return "uint";
            else if (t == typeof(Int64))
                return "long";
            else if (t == typeof(UInt64))
                return "ulong";
            else if (t == typeof(Double))
                return "double";
            else if (t == typeof(Single))
                return "float";
            else if (t == typeof(Decimal))
                return "decimal";
            else if (t == typeof(String))
                return "string";
            else if (t == typeof(Byte))
                return "byte";
            else if (t == typeof(Char))
                return "char";
            else if (t == typeof(Boolean))
                return "bool";
            else if (t == typeof(Object))
                return "object";
            else if (t == typeof(void))
                return "void";

            // For generic placeholders: T
            if (t.IsGenericParameter)
                return t.Name;
            // For IEnumerable`1
            if (t.IsGenericType)
                return NormalizeShort(t.Name);
            // For T[]
            if (t.FullName == null)
                return t.Name;

            return Normalize(t.FullName);
        }

        public static string NormalizeShort(string shortMangled)
        {
            // TODO: This function is a heurstic solution.
            // the real one should query the Type's generic params at the Diver and forward them.

            int backtickIndex = shortMangled.IndexOf('`');
            if (backtickIndex == -1)
                return shortMangled;

            if (shortMangled.Contains("`1"))
                return shortMangled.Replace("`1", "<T>");
            string argAmountStr = shortMangled.Substring(backtickIndex + 1);
            if (!int.TryParse(argAmountStr, out int argAmount))
            {
                return shortMangled;
            }

            string mainPart = shortMangled.Substring(0, backtickIndex);
            IEnumerable<string> madeUpGenericParamNames = Enumerable.Range(1, argAmount).Select(num => $"T{num}");
            return $"{mainPart}<{string.Join(", ", madeUpGenericParamNames)}>";
        }

        /// <summary>
        /// MainAssembly.MainType`1[[SubAssembly.SubTypeA, SubAssembly.AssemblyName, Version=6.0.0.0, Culture=neutral, PublicKeyToken=7cec85d7bea7798e],[SubAssembly.SubTypeB, SubAssembly.AssemblyName, Version=6.0.0.0, Culture=neutral, PublicKeyToken=7cec85d7bea7798e]]
        ///  to
        /// MainAssembly.MainType<SubAssembly.SubTypeA, SubAssembly.SubTypeB>
        /// </summary>
        public static string Normalize(string mangledName)
        {
            if (!mangledName.Contains('`'))
                return mangledName;

            // Use a regular expression to match and replace the extra type information
            string parsedFullName = Regex.Replace(mangledName, @",\s*[^,]+\s*,\s*Version=\d+\.\d+\.\d+\.\d+\s*,\s*Culture=\w+\s*,\s*PublicKeyToken=\w+", "");

            // Use another regular expression to match and replace the square brackets
            parsedFullName = parsedFullName.Replace("[[", "<");
            parsedFullName = parsedFullName.Replace("]]", ">");
            parsedFullName = Regex.Replace(parsedFullName, @"<([^<>]*)\],\[([^<>]*)>", "<$1, $2>");

            // Use another regular expression to match and remove the backtick and number after it
            parsedFullName = Regex.Replace(parsedFullName, @"\`\d", "");

            // Output the parsed full name of the generic type, which should be "System.Collections.Generic.List<System.Threading.Tasks.Task<System.Collections.Generic.Dictionary<System.Int32, System.String>>>"
            return parsedFullName;
        }

        /// <summary>
        /// MainType<T1, T2> to MainType`2
        /// </summary>
        public static string DenormalizeShort(string demangledName)
        {
            int index = demangledName.IndexOf('<');
            if (index == -1)
                return demangledName;

            string genericPart = demangledName.Substring(index);
            int numGenericArgs = genericPart.Count(c => c == ',') + 1;
            return demangledName.Substring(0, demangledName.IndexOf('<')) + $"`{numGenericArgs}";
        }

    }
}