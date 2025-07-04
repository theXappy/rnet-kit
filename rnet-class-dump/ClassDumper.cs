using RemoteNET;
using RemoteNET.Common;
using RemoteNET.Internal.Reflection;
using RemoteNET.Internal.Reflection.DotNet;
using RemoteNET.RttiReflection;
using System.Diagnostics;
using System.Reflection;
using System.Text;

namespace rnet_class_dump
{
    internal class ClassDumper
    {
        private bool _isVerbose;
        public void LogVerbose(string message)
        {
            if (_isVerbose)
                Console.Error.WriteLine(message);
        }
        public void LogError(string message)
        {
            Console.WriteLine(message);
        }

        public ClassDumper(bool isVerbose)
        {
            _isVerbose = isVerbose;
        }

        public int DumpClasses(string[] filters, string targetProcess, bool unmanaged)
        {
            RemoteApp app;
            try
            {
                LogVerbose($"Placeholder: Connecting to target '{targetProcess}'...");
                app = Common.Connect(targetProcess, unmanaged);
            }
            catch (Exception ex)
            {
                LogError($"Error connecting to target: {ex.Message}");
                return 1;
            }

            LogVerbose($"Dumping classes from target '{targetProcess}'");
            LogVerbose($"Dumping from '{targetProcess}' with those filters:");
            foreach (string filter in filters)
            {
                LogVerbose($"  - {filter}");
            }

            try
            {
                return DumpClassesInternal(app, filters);
            }
            catch (Exception ex)
            {
                LogError($"Error reading filters or querying types: {ex.Message}");
                Debugger.Launch();
                return 1;
            }
        }

        // Extracted for testability
        private int DumpClassesInternal(RemoteApp app, string[] filters)
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
            }
            Console.WriteLine($"__RemoteNET_Obj_Base|{helperClassFileName}");

            // Collect all type candidates
            List<RemoteTypeBase> queriedTypes = GetTypesToDump(filters, app);

            // Recursively resolve all 
            var allTypesToDump = RecursiveTypesSearch(queriedTypes);

            // Process each candidate group (by key)
            foreach (var kvp in allTypesToDump)
            {
                (string typeFullName, string fileName) = DumpType(app, tempDir, kvp.Value); // Pass all types for this key

                if (typeFullName != null && fileName != null)
                {
                    Console.WriteLine($"{typeFullName}|{fileName}");
                }
            }

            return 0; // Return 0 for success
        }

        // Accepts all types for a key, selects the one with the most members, and adds a comment listing all type full names
        private (string typeFullName, string fileName) DumpType(RemoteApp app, string tempDir, List<RemoteTypeBase> types)
        {
            if (types == null || types.Count == 0)
                return (null, null);

            // Select the type with the most members
            RemoteTypeBase selectedType = types
                .OrderByDescending(t => (t.GetMembers()?.Length) ?? 0)
                .First();

            // Compose a normalized filename from the key (namespace+class)
            string typeFullName = selectedType.FullName;
            Path.GetInvalidFileNameChars()
                .ToList()
                .ForEach(c => typeFullName = typeFullName.Replace(c, '_'));
            string fileName = Path.Combine(tempDir, $"{typeFullName}.cs");

            // Only write if file doesn't exist or is empty
            if (!File.Exists(fileName) || new FileInfo(fileName).Length == 0)
            {
                StringBuilder codeBuilder = new StringBuilder();
                // Add comment with all type full names
                codeBuilder.AppendLine("// All type full names for this class:");
                foreach (var t in types)
                {
                    codeBuilder.AppendLine($"//   {t.FullName}");
                }
                codeBuilder.AppendLine();
                bool worthy = WriteClassCode(selectedType, codeBuilder);
                if (!worthy)
                    return (null, null);
                File.WriteAllText(fileName, codeBuilder.ToString());
            }
            return (typeFullName, fileName);
        }

        private Dictionary<string, List<RemoteTypeBase>> RecursiveTypesSearch(List<RemoteTypeBase> queriedTypes)
        {
            // This queue will hold all types that we still need to process, queried or not
            // By NAMESPACE + CLASS NAME
            Queue<RemoteTypeBase> typesToProcess = new(queriedTypes);
            // Keep track of all types ever enqueued, by FULL TYPE NAMES
            HashSet<string> enqueuedTypes = queriedTypes
                .Select(t => t.FullName!)
                .ToHashSet();

            // This dict will hold all types that we need to dump
            // Key: <namespace + class name>, Value: List of RemoteTypeBase objects that match this key
            Dictionary<string, List<RemoteTypeBase>> nsClassToTypes = new();

            // Helper to get the key: <namespace + class name>
            static string GetNamespaceClassKey(RemoteTypeBase type)
            {
                string ns = type.Namespace ?? "";
                string name = type.Name ?? "";
                return string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}";
            }

            while (typesToProcess.Count > 0)
            {
                RemoteTypeBase currentType = typesToProcess.Dequeue();
                if (currentType == null)
                    continue;

                // Ensure the output list of types exists for this <namespace> + <class name> pair
                string key = GetNamespaceClassKey(currentType);
                if (!nsClassToTypes.TryGetValue(key, out var typeList))
                {
                    typeList = new List<RemoteTypeBase>();
                    nsClassToTypes[key] = typeList;
                }

                typeList.Add(currentType);

                // Get all members of the current type
                MemberInfo[] members = currentType.GetMembers();
                LogVerbose($"[RecursiveTypesSearch] Processing class: {currentType.FullName}");
                foreach (MemberInfo member in members)
                {
                    string debug___member_ToString = member.ToString();
                    // If this is a method, check its return type and parameters
                    if (member is MethodInfo method)
                    {
                        Type actualRetType = method.ReturnType;
                        while (actualRetType is PointerType pType)
                        {
                            actualRetType = pType.Inner;
                        }

                        // Check return type
                        if (actualRetType is RemoteTypeBase returnType)
                        {
                            Enqueue(returnType);
                        }

                        if (member is IRttiMethodBase rttiMethod)
                        {
                            // Check parameters
                            foreach (LazyRemoteParameterResolver param in rttiMethod.LazyParamInfos)
                            {
                                Type actualParamType = param.TypeResolver.Value;
                                while (actualParamType is PointerType pType)
                                {
                                    actualParamType = pType.Inner;
                                }

                                if (actualParamType is RemoteTypeBase paramType)
                                {
                                    Enqueue(paramType);
                                }
                            }
                        }
                        else
                        {
                            throw new Exception("WTF NOT AN IRttiMethodBase? " + member.GetType().FullName);
                        }
                    }
                    else if (member is PropertyInfo property)
                    {
                        // Check property type
                        if (property.PropertyType is RemoteTypeBase propertyType)
                        {
                            Enqueue(propertyType);
                        }
                    }
                    else if (member is FieldInfo field)
                    {
                        // Check field type
                        if (field.FieldType is RemoteTypeBase fieldType)
                        {
                            Enqueue(fieldType);
                        }
                    }
                }
            }

            return nsClassToTypes;


            //Local method for Enqueuing types, avoiding ALREADY processed types
            void Enqueue(RemoteTypeBase type)
            {
                if (type == null || enqueuedTypes.Contains(type.FullName))
                    return;
                LogVerbose($"Enqueuing type: {type.FullName}");
                typesToProcess.Enqueue(type);
                enqueuedTypes.Add(type.FullName);
            }
        }

        private List<RemoteTypeBase> GetTypesToDump(string[] filters, RemoteApp app)
        {
            var allCandidates = new List<CandidateType>();
            foreach (string filter in filters)
            {
                LogVerbose($"Querying types with filter: {filter}");
                var candidateTypes = app.QueryTypes(filter).ToList();
                LogVerbose($"Found {candidateTypes.Count} types for filter: {filter}");
                allCandidates.AddRange(candidateTypes);
            }

            List<RemoteTypeBase> remoteTypes = new List<RemoteTypeBase>();
            foreach (var candidate in allCandidates)
            {
                LogVerbose($"Upgrading candidate to remote type for: {candidate.TypeFullName}");
                RemoteTypeBase? remoteType = app.GetRemoteType(candidate) as RemoteTypeBase;
                if (remoteType == null)
                {
                    LogVerbose($"Failed to resolve remote type for: {candidate.TypeFullName}");
                    continue;
                }
                remoteTypes.Add(remoteType);
            }

            return remoteTypes;
        }

        private void WriteHelperClass(StringBuilder writer)
        {
            // TODO: Cache which modules we have already started GC on
            writer.AppendLine(@"using System;
using System.Linq;
using RemoteNET;

namespace RemoteNET.ClassDump.Internal
{
	public abstract class __RemoteNET_Obj_Base
	{
		public dynamic __dro;
		public Type __remoteType = null;

		public __RemoteNET_Obj_Base() => 
			throw new Exception(""Bad __RemoteNET_Obj_Base ctor called"");

		public __RemoteNET_Obj_Base(DynamicRemoteObject dro)
		{
			__dro = dro;
		}

		public __RemoteNET_Obj_Base(RemoteApp app, string fullTypeName, string dllName)
		{
			if (__remoteType == null)
			{
				__remoteType = app.GetRemoteType(app.QueryTypes(fullTypeName).Single());
				app.Communicator.StartOffensiveGC(dllName);
			}

			RemoteObject ro = app.Activator.CreateInstance(__remoteType);
			__dro = ro.Dynamify();
		}
	}
}");
        }

        private bool WriteClassCode(Type remoteType, StringBuilder writer)
        {
            try
            {
                // Get the remote type
                if (remoteType == null)
                {
                    LogVerbose($"Null Type as input?");
                    return false;
                }

                string? dllName = remoteType.Assembly.GetName().Name;
                string? fullName = remoteType.FullName;
                string className = remoteType.Name;

                // Namespace
                string? namespaceName = remoteType.Namespace;
                bool hasNamespace = !string.IsNullOrEmpty(namespaceName);

                AppendLine(writer, $"using System;", 0);
                AppendLine(writer, $"using System.Linq;", 0);
                AppendLine(writer, $"using RemoteNET;", 0);
                AppendLine(writer, $"using RemoteNET.ClassDump.Internal;", 0);
                AppendLine(writer, "", 0);

                int indentCount = 0;
                if (hasNamespace)
                {
                    // Split by '::' for C++-style nested classes, or '.' for C#-style
                    string[] nsParts = namespaceName.Split(new[] { "::" }, StringSplitOptions.None);
                    if (nsParts.Length == 1)
                    {
                        // Just a regular namespace
                        AppendLine(writer, $"namespace {nsParts[0]}", indentCount);
                        AppendLine(writer, "{", indentCount);
                        indentCount++;
                    }
                    else
                    {
                        // First part is the namespace, rest are nested classes
                        AppendLine(writer, $"namespace {nsParts[0]}", indentCount);
                        AppendLine(writer, "{", indentCount);
                        indentCount++;
                        for (int i = 1; i < nsParts.Length; i++)
                        {
                            AppendLine(writer, $"public partial class {nsParts[i]}", indentCount);
                            AppendLine(writer, "{", indentCount);
                            indentCount++;
                        }
                    }
                }
                else
                {
                    AppendLine(writer, "// No namespace", indentCount);
                }

                AppendLine(writer, $"public partial class {className} : __RemoteNET_Obj_Base", indentCount);
                AppendLine(writer, "{", indentCount);
                indentCount++;

                AppendLine(writer, "public ulong __address => (__dro as DynamicRemoteObject).__ro.RemoteToken;", indentCount);
                AppendLine(writer, "", indentCount);

                // Write a CTOR that accepts a DRO
                AppendLine(writer, $"public {className}(DynamicRemoteObject dro) : base(dro)", indentCount);
                AppendLine(writer, "{", indentCount);
                AppendLine(writer, "}", indentCount);

                // Write a CTOR that accepts a RemoteApp (Allocates remote object automatically)
                AppendLine(writer, $"public {className}(RemoteApp app) : base(app, \"{fullName}\", \"{dllName}\")", indentCount);
                AppendLine(writer, "{", indentCount);
                AppendLine(writer, "}", indentCount);
                AppendLine(writer, "", indentCount);

                // Write a CTOR that accepts a DRO. Effectively a "cast" constructor.
                // Actual cast operators are not allowed because of "casting to/from base class" limitations in C#.
                // TODO: Maybe assert (original type system's) inheritance here?
                AppendLine(writer, $"public {className}(__RemoteNET_Obj_Base obj) : this(obj.__dro as DynamicRemoteObject)", indentCount);
                AppendLine(writer, "{", indentCount);
                indentCount++;
                AppendLine(writer, $"DynamicRemoteObject objDro = obj.__dro as DynamicRemoteObject;", indentCount);
                AppendLine(writer, $"RemoteApp app = objDro.__ra;", indentCount);
                AppendLine(writer, $"__remoteType = app.GetRemoteType(app.QueryTypes(\"{fullName}\").Single());", indentCount);
                AppendLine(writer, "var objRo = objDro.__ro;", indentCount);
                AppendLine(writer, "var castedRo = objRo.Cast(__remoteType);", indentCount);
                AppendLine(writer, $"__dro = castedRo.Dynamify();", indentCount);
                indentCount--;
                AppendLine(writer, "}", indentCount);
                AppendLine(writer, "", indentCount);

                Dictionary<string, LazyRemoteTypeResolver> otherTypesUsed = new Dictionary<string, LazyRemoteTypeResolver>();
                // List all members
                if (!TryDumpMembers(remoteType, writer, className, indentCount))
                    return false;

                indentCount--;
                AppendLine(writer, "}", indentCount);

                // Close all opened scopes (nested classes and namespace)
                if (hasNamespace)
                {
                    string[] nsParts = namespaceName.Split(new[] { "::" }, StringSplitOptions.None);
                    int totalScopes = nsParts.Length; // 1 for namespace, rest for classes
                    for (int i = 1; i < nsParts.Length; i++)
                    {
                        indentCount--;
                        AppendLine(writer, "}", indentCount);
                    }
                    indentCount--;
                    AppendLine(writer, "}", indentCount); // close namespace
                }

                return true;
            }
            catch (Exception ex)
            {
                LogVerbose($"Error writing class code for {remoteType.FullName}: {ex.Message}");
                return false;
            }
        }

        private bool TryDumpMembers(Type remoteType, StringBuilder writer, string className, int indentCount)
        {
            MemberInfo[] members = remoteType.GetMembers();
            if (members.Length == 0)
                return true;

            string cppMainFullTypeName = remoteType.FullName!;
            Dictionary<string, string> memberTypes = new Dictionary<string, string>(); // Tracks member names and their types
            Dictionary<string, LazyRemoteTypeResolver> subClassesDict = new Dictionary<string, LazyRemoteTypeResolver>();
            foreach (MemberInfo member in members)
            {
                // Collected implied dependency types
                GetDependencyTypes(member, out Dictionary<string, LazyRemoteTypeResolver> otherTypesUsedTemp);
                foreach (var kvp in otherTypesUsedTemp)
                {
                    string csharpFullTypeName = kvp.Key;
                    if (subClassesDict.ContainsKey(csharpFullTypeName))
                        continue;

                    subClassesDict[csharpFullTypeName] = kvp.Value;

                    // Try to check for subclasses by comparing "Full Type Names"
                    string cppFullTypeName = kvp.Value.TypeFullName.TrimEnd('*');
                    if (!cppFullTypeName.StartsWith(cppMainFullTypeName) || cppFullTypeName.Length <= cppMainFullTypeName.Length)
                        continue;

                    // Cut just the name: everything after last dot
                    int lastDotIndex = csharpFullTypeName.LastIndexOf('.');
                    if (lastDotIndex == -1)
                        continue;

                    string typeName = csharpFullTypeName.Substring(lastDotIndex + 1);
                    memberTypes[typeName] = "SubClass";
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

        private bool IsOverlappingMethod(Dictionary<string, HashSet<string>> addedMethodsCache, MemberInfo member)
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

        private void GetDependencyTypes(MemberInfo member, out Dictionary<string, LazyRemoteTypeResolver> otherTypesUsed)
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

        private void WriteMember(StringBuilder writer, string className, MemberInfo member, int indentCount)
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
                if (rttiMethod.Name == "operator[]")
                {
                    Append(writer, $"// Operator[]: {rttiMethod.Name}({parameters});", indentCount);
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
                        if (csharpExpressionRetType == "dynamic")
                        {
                            // If the return type is dynamic, we can just return the body as is
                        }
                        else
                        {
                            // For objects, we need to wrap it in a DynamicRemoteObject
                            body = $"new {csharpExpressionRetType}((DynamicRemoteObject){body})";
                        }
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

        private void AppendLine(StringBuilder sb, string text, int indent = 0)
        {
            sb.Append(new string('\t', indent));
            sb.Append(text);
            sb.AppendLine();
        }
        private void Append(StringBuilder sb, string text, int indent = 0)
        {
            sb.Append(new string('\t', indent));
            sb.Append(text);
        }

        private (string declared, string csharpExpression) GetTypeIdentifier(LazyRemoteParameterResolver resolver, out bool isObject) => GetTypeIdentifier(resolver.TypeResolver, out isObject);
        private (string declared, string csharpExpression) GetTypeIdentifier(LazyRemoteTypeResolver resolver, out bool isObject)
        {
            string input = resolver.TypeFullName;
            // 'bool' doesn't have full type name...
            if (input == null)
                input = resolver.TypeName;
            // If we DO get a full type name, we need to remove the assembly name
            if (input.Contains("!"))
                input = input.Split('!')[1];

            var rawResults = GetTypeIdentifier(input, out isObject);

            // HACK: If RemoteNET failed to dump the type to a "RemoteBaseType",
            // we resort to dynamic
            if (isObject)
            {
                Type actual = resolver.Value;
                while (actual is PointerType pType)
                {
                    actual = pType.Inner;
                }

                if (actual is DummyRttiType)
                {
                    return (input, "dynamic");
                }
            }
            return rawResults;
        }

        private (string declared, string csharpExpression) GetTypeIdentifier(ParameterInfo parameterInfo, out bool isObject)
        {
            string? fullName = parameterInfo.ParameterType.FullName;
            if (fullName == null)
                throw new ArgumentNullException("parameterInfo.ParameterType.FullName");
            return GetTypeIdentifier(fullName, out isObject);
        }

        private (string declared, string csharpExpression) GetTypeIdentifier(string str, out bool isObject)
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

        private bool IsPrimitive(string str)
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

        string FormatTypeIdentifier(string declaredType, string csharpExpression)
        {
            var res = $"{csharpExpression}";
            if (declaredType != csharpExpression)
                res += $" /* {declaredType} */";
            return res;
        }
    }
}
