using RemoteNET;
using RemoteNET.Common;
using RemoteNET.Internal.Reflection;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace rnet_class_dump
{
    internal static class ClassDumper
    {
        public static int DumpClasses(string[] filters, string targetProcess, bool unmanaged, bool isVerbose)
        {
            RemoteApp app;
            try
            {
                if (isVerbose) Console.Error.WriteLine($"Placeholder: Connecting to target '{targetProcess}'...");
                app = Common.Connect(targetProcess, unmanaged);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error connecting to target: {ex.Message}");
                return 1;
            }


            if (isVerbose) Console.Error.WriteLine($"Placeholder: Dumping classes from target '{targetProcess}'");
            try
            {
                // Get app data path
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

                // Create a new temporary directory to dump all classes into
                string tempDir = Path.Combine(localAppData, "RemoteNetSourceGenCache", "RnetClassDump");
                Directory.CreateDirectory(tempDir);

                // Write the helper class
                string helperClassFileName = Path.Combine(tempDir, "__RemoteNET_Obj_Base.cs");
                if (!File.Exists(helperClassFileName) || new FileInfo(helperClassFileName).Length == 0)
                {
                    StringBuilder helperClassBuilder = new StringBuilder();
                    WriteHelperClass(helperClassBuilder);
                    File.WriteAllText(helperClassFileName, helperClassBuilder.ToString());
                    Console.WriteLine($"__RemoteNET_Obj_Base|{helperClassFileName}");
                }

                // Collect all type candidates
                List<CandidateType> allCandidates = GetTypesToDump(filters, isVerbose, app);

                // Process each candidate
                foreach (CandidateType candidate in allCandidates)
                {
                    DumpType(isVerbose, app, tempDir, candidate);
                }

                return 0; // Return 0 for success
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error reading filters or querying types: {ex.Message}");
                return 1;
            }
        }

        private static void DumpType(bool isVerbose, RemoteApp app, string tempDir, CandidateType candidate)
        {
            string typeFullName = candidate.TypeFullName;

            // Normalize typeName so it can be used as a Windows filename
            Path.GetInvalidFileNameChars()
                .ToList()
                .ForEach(c => typeFullName = typeFullName.Replace(c, '_'));

            // Create a file for the class
            string fileName = Path.Combine(tempDir, $"{typeFullName}.cs");

            // Create StreamWriter for the new file
            // if it exists and NOT EMPTY, skip it
            if (!File.Exists(fileName) || new FileInfo(fileName).Length == 0)
            {
                Type type = app.GetRemoteType(candidate);
                StringBuilder codeBuilder = new StringBuilder();
                bool worthy = WriteClassCode(type, codeBuilder, isVerbose);
                if (!worthy)
                    return;

                File.WriteAllText(fileName, codeBuilder.ToString());
            }
            Console.WriteLine($"{typeFullName}|{fileName}");
        }

        private static List<CandidateType> GetTypesToDump(string[] filters, bool isVerbose, RemoteApp app)
        {
            var allCandidates = new List<CandidateType>();
            foreach (string filter in filters)
            {
                if (isVerbose) Console.Error.WriteLine($"Querying types with filter: {filter}");
                var candidateTypes = app.QueryTypes(filter).ToList();
                if (isVerbose) Console.Error.WriteLine($"Found {candidateTypes.Count} types for filter: {filter}");
                allCandidates.AddRange(candidateTypes);
            }

            return allCandidates;
        }

        private static void WriteHelperClass(StringBuilder writer)
        {
            writer.AppendLine("using System;");
            writer.AppendLine();
            writer.AppendLine("namespace RemoteNET.ClassDump.Internal");
            writer.AppendLine("{");
            writer.AppendLine();
            writer.AppendLine("\tpublic abstract class __RemoteNET_Obj_Base");
            writer.AppendLine("\t{");
            writer.AppendLine("\t\tpublic dynamic __dro;");
            writer.AppendLine();
            writer.AppendLine("\t\tpublic __RemoteNET_Obj_Base(DynamicRemoteObject dro)");
            writer.AppendLine("\t\t{");
            writer.AppendLine("\t\t\t__dro = dro;");
            writer.AppendLine("\t\t}");
            writer.AppendLine("\t\tpublic __RemoteNET_Obj_Base()");
            writer.AppendLine("\t\t{");
            writer.AppendLine("\t\t\tthrow new Exception(\"Bad __RemoteNET_Obj_Base ctor called\");");
            writer.AppendLine("\t\t}");
            writer.AppendLine("\t}");
            writer.AppendLine("}");
        }

        private static bool WriteClassCode(Type remoteType, StringBuilder writer, bool isVerbose)
        {
            try
            {
                // Get the remote type
                if (remoteType == null)
                {
                    if (isVerbose) Console.Error.WriteLine($"Null Type as input?");
                    return false;
                }

                string? dllName = remoteType.Assembly.GetName().Name;
                string? fullName = remoteType.FullName;
                string className = remoteType.Name;

                // Namespace
                // TODO: Split by colons ??
                string? namespaceName = remoteType.Namespace;
                bool hasNamespace = !string.IsNullOrEmpty(namespaceName);


                AppendLine(writer, $"using System;", 0);
                AppendLine(writer, $"using System.Linq;", 0);
                AppendLine(writer, $"using RemoteNET;", 0);
                AppendLine(writer, $"using RemoteNET.ClassDump.Internal;", 0);
                AppendLine(writer, "", 0);

                if (hasNamespace)
                {
                    AppendLine(writer, $"namespace {namespaceName}", 0);
                    AppendLine(writer, "{", 0);
                }
                else
                {
                    AppendLine(writer, "// No namespace", 0);
                }
                int indentCount = hasNamespace ? 1 : 0;

                AppendLine(writer, $"public partial class {className} : __RemoteNET_Obj_Base", indentCount);
                AppendLine(writer, "{", indentCount);
                indentCount++;

                AppendLine(writer, "public static Type __remoteType = null;", indentCount);
                AppendLine(writer, "", indentCount);

                AppendLine(writer, "public ulong __address => (__dro as DynamicRemoteObject).__ro.RemoteToken;", indentCount);
                AppendLine(writer, "", indentCount);

                // Write a CTOR that accepts a DRO
                AppendLine(writer, $"public {className}(DynamicRemoteObject dro) : base(dro)", indentCount);
                AppendLine(writer, "{", indentCount);
                AppendLine(writer, "}", indentCount);

                // Write a CTOR that accepts a RemoteApp (Allocats remote object automatically)
                AppendLine(writer, $"public {className}(RemoteApp app)", indentCount);
                AppendLine(writer, "{", indentCount);
                AppendLine(writer, "if (__remoteType == null)", indentCount + 1);
                AppendLine(writer, "{", indentCount + 1);
                AppendLine(writer, "__remoteType = app.GetRemoteType(app.QueryTypes(\"" + fullName + "\").Single());", indentCount + 2);
                // TODO: Cache which modules we have already started GC on
                AppendLine(writer, "app.Communicator.StartOffensiveGC(\"" + dllName + "\");", indentCount + 2);
                AppendLine(writer, "}", indentCount + 1);
                AppendLine(writer, "", indentCount);
                AppendLine(writer, "RemoteObject ro = app.Activator.CreateInstance(__remoteType);", indentCount + 1);
                AppendLine(writer, "__dro = ro.Dynamify();", indentCount + 1);
                AppendLine(writer, "}", indentCount);
                AppendLine(writer, "", indentCount);

                Dictionary<string, LazyRemoteTypeResolver> otherTypesUsed = new Dictionary<string, LazyRemoteTypeResolver>();
                // List all members
                if (!TryDumpMembers(remoteType, writer, className, indentCount, otherTypesUsed))
                    return false;

                indentCount--;
                AppendLine(writer, "}", indentCount);

                // Close namespace
                if (hasNamespace)
                {
                    indentCount--;
                    AppendLine(writer, "}", indentCount);
                }

                WriteDummies(writer, otherTypesUsed);

                return true;
            }
            catch (Exception ex)
            {
                if (isVerbose) Console.Error.WriteLine($"Error writing class code for {remoteType.FullName}: {ex.Message}");
                return false;
            }
        }

        private static bool TryDumpMembers(Type remoteType, StringBuilder writer, string className, int indentCount, Dictionary<string, LazyRemoteTypeResolver> otherTypesUsed)
        {
            MemberInfo[] members = remoteType.GetMembers();
            if (members.Length == 0)
                return false;

            string cppMainFullTypeName = remoteType.FullName!;
            Dictionary<string, string> memberTypes = new Dictionary<string, string>(); // Tracks member names and their types
            foreach (MemberInfo member in members)
            {
                // Collected implied dependency types
                GetDependencyTypes(member, out Dictionary<string, LazyRemoteTypeResolver> otherTypesUsedTemp);
                foreach (var kvp in otherTypesUsedTemp)
                {
                    string csharpFullTypeName = kvp.Key;
                    if (!otherTypesUsed.ContainsKey(csharpFullTypeName))
                    {
                        otherTypesUsed[csharpFullTypeName] = kvp.Value;

                        // Try to check for subclasses by comparing "Full Type Names"
                        string cppFullTypeName = kvp.Value.TypeFullName.TrimEnd('*');
                        if (cppFullTypeName.StartsWith(cppMainFullTypeName) && cppFullTypeName.Length > cppMainFullTypeName.Length)
                        {
                            // Cut just the name: everything after last dot
                            int lastDotIndex = csharpFullTypeName.LastIndexOf('.');
                            if (lastDotIndex != -1)
                            {
                                string typeName = csharpFullTypeName.Substring(lastDotIndex + 1);
                                memberTypes[typeName] = "SubClass";
                            }
                        }
                    }
                }
            }

            HashSet<string> forbiddenMembers = new HashSet<string>();
            foreach (MemberInfo member in members)
            {
                string memberName = member.Name;

                // Check for conflicts between methods and subclasses
                if (memberTypes.ContainsKey(memberName))
                {
                    if (memberTypes[memberName] != member.MemberType.ToString())
                    {
                        // Conflict detected! We don't want to write those members.
                        forbiddenMembers.Add(memberName);
                        continue;
                    }
                }

                // Track the member type
                memberTypes[memberName] = member.MemberType.ToString();
            }


            var declaredMethods = new Dictionary<string, HashSet<string>>();
            foreach (MemberInfo member in members)
            {

                StringBuilder memberDeclaration = new StringBuilder();
                WriteMember(memberDeclaration, className, member, indentCount: 0);

                string prefix = string.Empty;
                if (IsOverlappingMethod(declaredMethods, member))
                {
                    AppendLine(writer, "// WARNING: This method's reduced signature overlaps with another one.", indentCount);
                    prefix = "// ";
                }

                bool containsReference = memberDeclaration.ToString().Contains("& ");
                if (containsReference)
                {
                    AppendLine(writer, "// WARNING: This method's reduced signature contains a reference type. Not supported yet.", indentCount);
                    prefix = "// ";
                }

                string memberName = member.Name;
                if (forbiddenMembers.Contains(memberName))
                {
                    // Write a comment
                    AppendLine(writer, $"// WARNING: {memberName} member is conflicting with another member or subclass.", indentCount);
                    prefix = "// ";
                }


                // Members text might be multiple lines (mostly lines with comments above the actual decleration)
                foreach (string memberLine in memberDeclaration.ToString().Split("\n"))
                {
                    Append(writer, prefix + memberLine, indentCount);
                }
                AppendLine(writer, string.Empty, indentCount);
            }

            return true;
        }

        private static bool IsOverlappingMethod(Dictionary<string, HashSet<string>> addedMethodsCache, MemberInfo member)
        {
            if (member is not MethodInfo method)
                return false;

            // If this is a method, compose list of *actual* parameters types that we're going to use
            // since some are "downgraded" to dynamic
            ParameterInfo[] parametersInfo = method.GetParameters();
            string restarizedParameters = string.Empty;
            for (int i = 0; i < parametersInfo.Length; i++)
            {
                ParameterInfo param = parametersInfo[i];
                (string _, string csharpExpression) = GetTypeIdentifier(param, out bool isObject);
                if (restarizedParameters.Length > 0)
                    restarizedParameters += ", ";
                restarizedParameters += csharpExpression;
            }
            HashSet<string>? existingSignaturesForCurrentMethod;
            if (!addedMethodsCache.TryGetValue(method.Name, out existingSignaturesForCurrentMethod))
            {
                existingSignaturesForCurrentMethod = new HashSet<string>();
                addedMethodsCache[method.Name] = existingSignaturesForCurrentMethod;
            }
            return !existingSignaturesForCurrentMethod.Add(restarizedParameters);
        }

        private static void WriteDummies(StringBuilder writer, Dictionary<string, LazyRemoteTypeResolver> otherTypesUsed)
        {
            foreach (KeyValuePair<string, LazyRemoteTypeResolver> kvp in otherTypesUsed)
            {
                // Multi-level pointers get dynamics
                string fullTypeName = kvp.Key;
                if (fullTypeName == "dynamic")
                    continue;

                // Split the full type name into components
                string[] components = fullTypeName.Split('.');
                if (components.Length == 0)
                    continue;

                int classIndex = 0;
                string? namespaceName = null;
                if (components.Length > 1)
                {
                    namespaceName = components[0];
                    classIndex++;
                }

                // Write the namespace if it exists
                if (!string.IsNullOrEmpty(namespaceName))
                {
                    writer.AppendLine($"namespace {namespaceName}");
                    writer.AppendLine("{");
                }

                string closingBrackets = string.Empty;
                for (; classIndex < components.Length; classIndex++)
                {
                    string className = components[classIndex];
                    // Write the partial class
                    string indent = new string('\t', classIndex);
                    writer.AppendLine($"{indent}public partial class {className} : __RemoteNET_Obj_Base");
                    writer.AppendLine(indent + "{");
                    closingBrackets += indent + "}\n";
                }
                writer.AppendLine(closingBrackets);

                // Close the namespace if it exists
                if (!string.IsNullOrEmpty(namespaceName))
                {
                    writer.AppendLine("}");
                }

                writer.AppendLine();
            }
        }


        private static void GetDependencyTypes(MemberInfo member, out Dictionary<string, LazyRemoteTypeResolver> otherTypesUsed)
        {
            otherTypesUsed = new Dictionary<string, LazyRemoteTypeResolver>();
            if (member is RemoteRttiMethodInfo rttiMethod)
            {
                // Parameters list
                foreach (var param in rttiMethod.LazyParamInfos)
                {
                    var paramType = param.TypeResolver.Value;
                    (string _, string csharpExpression) = GetTypeIdentifier(param, out bool isObject);
                    if (isObject)
                        otherTypesUsed[csharpExpression] = param.TypeResolver;
                }

                // Ret Value
                var returnType = rttiMethod.LazyRetType.Value;
                (string _, string csharpExpressionRetType) = GetTypeIdentifier(rttiMethod.LazyRetType, out bool isObjectRetType);
                if (isObjectRetType)
                    otherTypesUsed[csharpExpressionRetType] = rttiMethod.LazyRetType;
            }
        }

        private static void WriteMember(StringBuilder writer, string className, MemberInfo member, int indentCount)
        {
            if (member is PropertyInfo property)
            {
                Append(writer, $"public dynamic {property.Name} {{ get => __dro.{property.Name}; set => __dro.{property.Name} = value; }}", indentCount);
            }
            else if (member is FieldInfo field)
            {
                if (field.Name == "vftable")
                {
                    Append(writer, $"// Unsupported vftable FIELD: {member.MemberType}. Name: {member.Name}", indentCount);
                    return;
                }
                Append(writer, $"public dynamic {field.Name} {{ get => __dro.{field.Name}; set => __dro.{field.Name} = value; }}", indentCount);
            }
            else if (member is RemoteRttiMethodInfo rttiMethod)
            {
                // Skip the first argument as it's the instance itself
                IEnumerable<RemoteNET.Common.LazyRemoteParameterResolver> paramInfos = rttiMethod.LazyParamInfos.Skip(1);
                string parameters = string.Join(", ", paramInfos.Select(p =>
                {
                    (string declared, string csharpExpression) = GetTypeIdentifier(p, out bool _);
                    string formatted = FormatTypeIdentifier(declared, csharpExpression);
                    return formatted + $" {p.Name}";
                }));

                if (rttiMethod.Name == className)
                {
                    // It's a constructor
                    Append(writer, $"// Constructor: {rttiMethod.Name}({parameters});", indentCount);
                }
                else if (rttiMethod.Name.StartsWith("~"))
                {
                    // It's a destructor
                    Append(writer, $"// Destructor: {rttiMethod.Name}({parameters});", indentCount);
                }
                else
                {
                    string invocationArgs = string.Empty;
                    foreach (RemoteNET.Common.LazyRemoteParameterResolver param in paramInfos)
                    {
                        if (invocationArgs.Length > 0)
                            invocationArgs += ", ";

                        GetTypeIdentifier(param, out bool isObject);
                        invocationArgs += param.Name;
                        if (isObject)
                            invocationArgs += ".__dro";
                    }
                    (string declaredRetType, string csharpExpressionRetType) = GetTypeIdentifier(rttiMethod.LazyRetType, out bool isObjectRet);
                    string formattedRetType = FormatTypeIdentifier(declaredRetType, csharpExpressionRetType);

                    string body = $"__dro.{rttiMethod.Name}({invocationArgs})";
                    if (IsPrimitive(csharpExpressionRetType))
                    {
                        // For primitives, add a lousy cast.
                        string castToUlong = $"(ulong)(UIntPtr)({body})";
                        if (csharpExpressionRetType == "bool")
                        {
                            body = $"({castToUlong}) != 0";
                        }
                        else if (csharpExpressionRetType == "void")
                        {
                            // Do nothing. Not return type - no need to cast to anything else.
                        }
                        else
                        {
                            body = $"({csharpExpressionRetType}){castToUlong}";
                        }
                    }
                    else if (isObjectRet)
                    {
                        body = $"new {csharpExpressionRetType}((DynamicRemoteObject){body})";
                    }

                    Append(writer, $"public {formattedRetType} {rttiMethod.Name}({parameters}) => {body};", indentCount);
                }
            }
            else if (member is MethodInfo method)
            {
                // Assuming no parameters for simplicity
                string parameters = string.Join(", ", method.GetParameters().Select(p => $"dynamic /*{p.ParameterType.Name}*/ {p.Name}"));
                Append(writer, $"public dynamic /*{method.ReturnType.Name}*/ {method.Name}({parameters}) => __dro.{method.Name}({string.Join(", ", method.GetParameters().Select(p => GetTypeIdentifier(p, out _)))});", indentCount);
            }
            else
            {
                Append(writer, $"// Unsupported member type: {member.MemberType}. Name: {member.Name}", indentCount);
            }
        }

        private static void AppendLine(StringBuilder sb, string text, int indent = 0)
        {
            sb.Append(new string('\t', indent));
            sb.Append(text);
            sb.AppendLine();
        }
        private static void Append(StringBuilder sb, string text, int indent = 0)
        {
            sb.Append(new string('\t', indent));
            sb.Append(text);
        }

        private static (string declared, string csharpExpression) GetTypeIdentifier(LazyRemoteParameterResolver resolver, out bool isObject) => GetTypeIdentifier(resolver.TypeResolver, out isObject);
        private static (string declared, string csharpExpression) GetTypeIdentifier(LazyRemoteTypeResolver resolver, out bool isObject)
        {
            string input = resolver.TypeFullName;
            // 'bool' doesn't have full type name...
            if (input == null)
                input = resolver.TypeName;
            // If we DO get a full type name, we need to remove the assembly name
            if (input.Contains("!"))
                input = input.Split('!')[1];

            return GetTypeIdentifier(input, out isObject);
        }

        private static (string declared, string csharpExpression) GetTypeIdentifier(ParameterInfo parameterInfo, out bool isObject)
        {
            string? fullName = parameterInfo.ParameterType.FullName;
            if (fullName == null)
                throw new ArgumentNullException("parameterInfo.ParameterType.FullName");
            return GetTypeIdentifier(fullName, out isObject);
        }

        private static (string declared, string csharpExpression) GetTypeIdentifier(string str, out bool isObject)
        {
            string csharpExpression;
            bool isPointer = str.EndsWith("*");
            bool isPrimitive = IsPrimitive(str);
            bool isEnum = str.StartsWith("enum ");
            bool isMultiLevelPointer = str.EndsWith("**");
            bool isFunction = str.Contains("(") && str.Contains(")");

            isObject = !isPrimitive && !isEnum && !isFunction;

            if ((isPrimitive && isPointer) || isEnum)
            {
                csharpExpression = $"ulong"; // TODO: ulong is not the best choice... IntPtr didn't work. Maybe nuint?
            }
            else if (str.Contains("<") || isMultiLevelPointer)
                csharpExpression = $"dynamic";
            else if (str == "int64_t")
                csharpExpression = "long";
            else if (str == "uint64_t")
                csharpExpression = "ulong";
            else if (str == "int32_t")
                csharpExpression = "int";
            else if (str == "uint32_t")
                csharpExpression = "uint";
            else if (str == "int16_t")
                csharpExpression = "short";
            else if (str == "uint16_t")
                csharpExpression = "ushort";
            else if (str == "System.Void")
                csharpExpression = "void";
            else if (str == "System.Boolean")
                csharpExpression = "bool";
            else if (str == "System.Byte")
                csharpExpression = "byte";
            else if (str == "System.SByte")
                csharpExpression = "sbyte";
            else if (str == "System.Int8")
                csharpExpression = "sbyte";
            else if (str == "System.UInt8")
                csharpExpression = "byte";
            else if (str == "System.Int16")
                csharpExpression = "short";
            else if (str == "System.UInt16")
                csharpExpression = "ushort";
            else if (str == "System.Int32")
                csharpExpression = "int";
            else if (str == "System.UInt32")
                csharpExpression = "uint";
            else if (str == "System.Int64")
                csharpExpression = "long";
            else if (str == "System.UInt64")
                csharpExpression = "ulong";
            else if (str == "System.Single")
                csharpExpression = "float";
            else if (str == "System.Double")
                csharpExpression = "double";
            else if (str == "System.String")
                csharpExpression = "string";
            else if (str == "System.Char")
                csharpExpression = "char";
            else
            { 
                // TODO: Used to check "IsPointer" here but something went wrong and the cases combined...
                csharpExpression = str.Replace("::", ".").TrimEnd('*', '&');
            }

            return (str, csharpExpression);
        }

        private static bool IsPrimitive(string str)
        {
            str = str.TrimEnd('*');
            return str == "int" ||
                str == "uint" ||
                str == "long" ||
                str == "ulong" ||
                str == "short" ||
                str == "ushort" ||
                str == "char" ||
                str == "byte" ||
                str == "sbyte" ||
                str == "bool" ||
                str == "float" ||
                str == "double" ||
                str == "void" ||

                str == "System.Char" ||
                str == "System.Byte" ||
                str == "System.SByte" ||
                str == "System.Boolean" ||
                str == "System.Int8" ||
                str == "System.UInt8" ||
                str == "System.Int16" ||
                str == "System.UInt16" ||
                str == "System.Int32" ||
                str == "System.UInt32" ||
                str == "System.Int64" ||
                str == "System.UInt64" ||
                str == "System.Single" ||
                str == "System.Double" ||
                str == "System.Void" ||

                str == "int8_t" ||
                str == "uint8_t" ||
                str == "int16_t" ||
                str == "uint16_t" ||
                str == "int32_t" ||
                str == "uint32_t" ||
                str == "int64_t" ||
                str == "uint64_t";
        }

        static string FormatTypeIdentifier(string declaredType, string csharpExpression)
        {
            var res = $"{csharpExpression}";
            if (declaredType != csharpExpression)
                res += $" /* {declaredType} */";
            return res;
        }
    }
}
