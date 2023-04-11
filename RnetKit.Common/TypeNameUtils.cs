using System.Text.RegularExpressions;

namespace RnetKit.Common
{
    public static class TypeNameUtils
    {
        /// <summary>
        /// MainAssembly.MainType`1[[SubAssembly.SubTypeA, SubAssembly.AssemblyName, Version=6.0.0.0, Culture=neutral, PublicKeyToken=7cec85d7bea7798e],[SubAssembly.SubTypeB, SubAssembly.AssemblyName, Version=6.0.0.0, Culture=neutral, PublicKeyToken=7cec85d7bea7798e]]
        ///  to
        /// MainAssembly.MainType<SubAssembly.SubTypeA, SubAssembly.SubTypeB>
        /// </summary>
        public static string Normalize(string mangledName)
        {
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
            string genericPart = demangledName.Substring(demangledName.IndexOf('<'));
            int numGenericArgs = genericPart.Count(c => c == ',') + 1;
            return demangledName.Substring(0, demangledName.IndexOf('<')) + $"`{numGenericArgs}";
        }

    }
}