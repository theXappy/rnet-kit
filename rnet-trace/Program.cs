using System.Collections.Concurrent;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using CommandLine;
using RemoteNET;
using RemotenetTrace;
using ScubaDiver.API.Hooking;
using Microsoft.CodeAnalysis.Scripting;
using System.Diagnostics;
using RemoteNET.Common;
using RnetKit.Common;

namespace QuickStart
{
    class Program
    {
        public class TraceOptions
        {
            [Option('t', "target", Required = true, HelpText = "Target process name. Partial names area allowed but a single match is expected. " +
                "e.g. \"notep\" for notepad")]
            public string? TargetProcess { get; set; }
            [Option('i', "include", Required = true, HelpText = "Included Method: Query for a full type identifier of class + method name to include in trace. " +
                "e.g. \"System.Text.StringBuilder.Append\", " +
                "     \"System.Text.StringBuilder.Append(String)\". " +
                "Method substring can contain '*'s as wildcardss")]
            public IEnumerable<string> IncludedMethods { get; set; }
            [Option('x', "exclude", Required = false, HelpText = "Excluded Method: Query for a full type identifier of class + method name to exclude from trace. " +
                "e.g. \"System.Text.StringBuilder.Append\", " +
                "     \"System.Text.StringBuilder.Append(String)\". " +
                "Method substring can contain '*'s as wildcardss")]
            public IEnumerable<string> ExcludedMethods { get; set; }
            [Option('u', "unmanaged", Required = false, HelpText = "Whether the target is an native app")]
            public bool Unmanaged { get; set; }
        }

        static void Main(string[] args)
        {
            Parser p = new Parser((settings) =>
            {
                settings.AllowMultiInstance = true;
                settings.AutoHelp = true;
                settings.AutoVersion = true;
                settings.HelpWriter = Parser.Default.Settings.HelpWriter;
            });
            p.ParseArguments<TraceOptions>(args).WithParsed(Run);
        }

        private static void Run(TraceOptions opts)
        {
            RuntimeType runtime = RuntimeType.Managed;
            if (opts.Unmanaged)
                runtime = RuntimeType.Unmanaged;

            RemoteApp app;
            try
            {
                Process target = null;
                if (int.TryParse(opts.TargetProcess, out int pid))
                {
                    try
                    {
                        target = Process.GetProcessById(pid);
                    }
                    catch
                    {
                    }
                }

                app = target != null
                    ? RemoteAppFactory.Connect(target, runtime)
                    : RemoteAppFactory.Connect(opts.TargetProcess, runtime);
            }
            catch (ArgumentException ex)
            {
                Console.WriteLine($"Error: " + ex.Message);
                return;
            }
            app.Communicator.CheckAliveness();

            Console.WriteLine("Loading...");

            List<MethodBase> methodsToHook = new();
            // Collect all methods answering "include" queries
            foreach (var method in opts.IncludedMethods)
            {
                string fullTypeNameQuery, methodNameQuery, encodedParametersQuery;
                ParseMethodIdentifier(method, out fullTypeNameQuery, out methodNameQuery, out encodedParametersQuery);

                // Get all matching types
                List<Type> remoteTypes = new List<Type>();
                var candidates = app.QueryTypes(fullTypeNameQuery).ToList();
                if (candidates.Count == 0)
                {
                    Console.WriteLine(
                        $"No types matched query '{fullTypeNameQuery}'");
                    return;
                }
                foreach (var candidate in candidates)
                {
                    try
                    {
                        remoteTypes.Add(app.GetRemoteType(candidate));
                    }
                    catch
                    {
                        Console.WriteLine($"WARNING: Failed to resolve one of the candidate types: {candidate.TypeFullName} from {candidate.Assembly}");
                    }
                }

                // Get all matching methods of all types
                foreach (Type? remoteType in remoteTypes)
                {
                    var allMethods = remoteType.GetMethods();
                    methodsToHook.AddRange(FindMethods(allMethods, remoteType.FullName, methodNameQuery, encodedParametersQuery));
                    var allCtors = remoteType.GetConstructors();
                    methodsToHook.AddRange(FindMethods(allCtors, remoteType.FullName, methodNameQuery, encodedParametersQuery));
                }
            }

            // Remove all "included" methods answering any of the "exclude" queries
            foreach (var method in opts.ExcludedMethods)
            {
                string fullTypeNameQuery, methodNameQuery, encodedParametersQuery;
                ParseMethodIdentifier(method, out fullTypeNameQuery, out methodNameQuery, out encodedParametersQuery);

                var methodsToRemove = FindMethods(methodsToHook, fullTypeNameQuery, methodNameQuery, encodedParametersQuery);
                foreach (MethodBase methodToRemove in methodsToRemove)
                    methodsToHook.Remove(methodToRemove);
            }

            if (!methodsToHook.Any())
            {
                Console.WriteLine("Error: Didn't find any methods to hook.");
                app.Dispose();
                return;
            }


            // Display all overloads to the user
            foreach (MethodBase methodToHook in methodsToHook)
            {
                Console.Write($"Found Method: (Class: {methodToHook.DeclaringType.FullName}) ");
                Console.WriteLine(TypeNameUtils.Normalize(methodToHook));
            }


            // Create local handler functions for all future hooks
            Dictionary<MethodBase, HooksPair> hookHandlers = CreateHookHandlers(methodsToHook);

            // Prepare clean-up code for when our program closes (gracefully)
            ManualResetEvent mre = new ManualResetEvent(false);
            bool unhooked = false;
            void Unhook(object? o, EventArgs e)
            {
                if (unhooked)
                    return;
                unhooked = true;

                Console.WriteLine("Unhooking...");
                foreach (KeyValuePair<MethodBase, HooksPair> methodAndHook in hookHandlers)
                {
                    app.HookingManager.UnhookMethod(methodAndHook.Key, methodAndHook.Value.Pre);
                    app.HookingManager.UnhookMethod(methodAndHook.Key, methodAndHook.Value.Post);
                }
                Console.WriteLine("Unhooked");
                app.Dispose();
                if (e is ConsoleCancelEventArgs cce)
                {
                    cce.Cancel = true;
                }
                mre.Set();
            }

            // Capture 'X' button click
            AppDomain.CurrentDomain.ProcessExit += Unhook;
            // Capture 'CTRL+C'
            Console.CancelKeyPress += Unhook;

            // Hook!
            HashSet<string> alreadyHookedMangledNames = new HashSet<string>();
            foreach (KeyValuePair<MethodBase, HooksPair> methodAndHook in hookHandlers)
            {
                var method = methodAndHook.Key;
                string actualFullName = $"{method.DeclaringType.FullName}.{method.Name}(";
                // Append parameters
                if (method is IRttiMethodBase rttiMethod)
                {
                    // Skipping 'this'
                    actualFullName += string.Join(",",
                        (rttiMethod).LazyParamInfos.Skip(1).Select(lpi => lpi.TypeResolver.TypeFullName));
                }
                else
                {
                    actualFullName += string.Join(",", (method).GetParameters().Select(lpi => lpi.ParameterType.FullName));
                }
                actualFullName += ")";

                if (alreadyHookedMangledNames.Contains(actualFullName))
                {
                    Console.Write($"Skipping Method: (Class: {method.DeclaringType.FullName}) {method.Name}... (Already added in base class) ");
                    continue;
                }
                alreadyHookedMangledNames.Add(actualFullName);


                Console.Write($"Hooking Method: (Class: {method.DeclaringType.FullName}) {method.Name}... (Pre) ");
                app.HookingManager.HookMethod(methodAndHook.Key, HarmonyPatchPosition.Prefix, methodAndHook.Value.Pre);
                Console.WriteLine($"OK!");
                Console.Write($"Hooking Method: (Class: {method.DeclaringType.FullName}) {method.Name}... (Post) ");
                app.HookingManager.HookMethod(methodAndHook.Key, HarmonyPatchPosition.Postfix, methodAndHook.Value.Post);
                Console.WriteLine($"OK!");
            }

            mre.WaitOne();
        }

        private static void ParseMethodIdentifier(string encodedMethodQuery, out string fullTypeNameQuery, out string methodName, out string encodedParameters)
        {
            if (!encodedMethodQuery.Contains('.'))
            {
                throw new Exception("Full method identifier must have a dot (.) in it to indicate a type and a method name.\n" +
                    "Could not parse this identifier:\n" +
                    encodedMethodQuery);
            }

            string fullMethodIdentifier = encodedMethodQuery;
            int paranthesisIndex = fullMethodIdentifier.IndexOf('(');
            if (paranthesisIndex != -1)
                fullMethodIdentifier = fullMethodIdentifier[..paranthesisIndex];


            fullTypeNameQuery = fullMethodIdentifier.Substring(0, fullMethodIdentifier.LastIndexOf('.'));
            // Note that we are looking for the index of the '.' in the TRIMMED SIGNATURE
            // and substringing from the WHOLE QUERY.
            // This will hopefully turn these:
            //      SomeNameSpace.SomeClass.SomeMethod(SomeNameSpace.AnotherClass param1)
            // Into this:
            //      SomeMethod(SomeNameSpace.AnotherClass param1)
            //
            // (And not this, after the '.' in the parameters part:
            // AnotherClass param1)
            methodName = encodedMethodQuery.Substring(fullMethodIdentifier.LastIndexOf('.') + 1);
            if (encodedMethodQuery.Contains("..ctor"))
            {
                fullTypeNameQuery = fullMethodIdentifier.Substring(0, fullMethodIdentifier.LastIndexOf("..ctor"));
                methodName = "." + methodName;
            }
            encodedParameters = null;
            if (methodName.Contains("("))
            {
                encodedParameters = methodName.Substring(methodName.LastIndexOf('('));
                methodName = methodName.Substring(0, methodName.LastIndexOf('('));
            }
        }

        static ConcurrentDictionary<int, ConcurrentStack<string>> semiCallStacks = new();

        private class HooksPair
        {
            public HookAction Pre { get; set; }
            public HookAction Post { get; set; }

            public HooksPair(HookAction pre, HookAction post)
            {
                Pre = pre;
                Post = post;
            }
        }
        private static Dictionary<MethodBase, HooksPair> CreateHookHandlers(List<MethodBase> methodsToHook)
        {
            DateTime start = DateTime.Now;
            Dictionary<MethodBase, HooksPair> generatedHooks = new();
            // Generate unique hook for every overload - To show the right signature when invoked
            foreach (MethodBase method in methodsToHook)
            {
                string humanizedParametersList = string.Join(", ", method.GetParameters().Select(pi => pi.ToString()));
                string className = method.DeclaringType.FullName;
                string methodName = method.Name;
                string[] paramNames;
                string[] paramTypes;
                if (method is IRttiMethodBase rttiMethod)
                {
                    // Skipping 'this'
                    paramTypes = rttiMethod.LazyParamInfos.Skip(1).Select(resolver => resolver.TypeResolver.TypeFullName).ToArray();
                    paramNames = rttiMethod.LazyParamInfos.Skip(1).Select(resolver => resolver.Name).ToArray();
                }
                else
                {
                    // This one will trigger remote type resolution for every parameter type...
                    Debug.WriteLine("[@@@] SLOW paramter deconstruction!");
                    paramNames = method.GetParameters().Select(param => param.Name).ToArray();
                    paramTypes = method.GetParameters().Select(param => param.ParameterType.ToString()).ToArray();
                }



                var script = HandlersRepo.Get(className, method.Name, paramTypes.Length);
                ScriptRunner<object> handlerScript = HandlersRepo.Compile(script);


                string uniqueId = $"{className}.{methodName}`{paramTypes.Length}";
                HookAction preHook = (context, instance, args) =>
                {
                    var semiCallstack = GetSemiCallstack(context.ThreadId);
                    semiCallstack.Push(uniqueId);
                    string firstPrefix = new string('│', Math.Max(semiCallStacks.Count - 2, 0));
                    if (semiCallStacks.Count == 1)
                        firstPrefix += "┌";
                    else
                        firstPrefix += "├┬";
                    firstPrefix += 


                    try
                    {
                        var scriptGlobals = new HandlerGlobals()
                        {
                            Context = new TraceContext(context.StackTrace, start, className, methodName, paramTypes,
                                paramNames),
                            Instance = instance,
                            Args = args,
                            Output = new StringBuilder()
                        };
                        handlerScript(scriptGlobals);

                        StringBuilder output = scriptGlobals.Output;
                        output.Replace("\n", "\n");
                        output.Insert(0, $" [TID={context.ThreadId}] ");
                        output.Insert(0, firstPrefix);
                        output.AppendLine();
                        Console.Write(scriptGlobals.Output);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(ex);
                    }
                };

                HookAction postHook = (context, instance, args) =>
                {
                    var semiCallstack = GetSemiCallstack(context.ThreadId);
                    string prefix = new string('│', semiCallStacks.Count);
                    string lastPrefix = prefix[..^1] + "└─ END";

                    if (semiCallstack.TryPop(out string lastUniqueId))
                    {
                        if (lastUniqueId != uniqueId)
                        {
                            throw new Exception(
                                $"ERROR: Mismatching popped method and currently exiting method. Current: {uniqueId} , Popped: {lastUniqueId}");
                        }

                        Console.WriteLine(lastPrefix);
                    }
                };

                generatedHooks[method] = new HooksPair(preHook, postHook);
            }
            return generatedHooks;
        }

        private static ConcurrentStack<string> GetSemiCallstack(int threadId)
        {
            if (!semiCallStacks.TryGetValue(threadId, out var stack))
            {
                stack = new ConcurrentStack<string>();
                semiCallStacks[threadId] = stack;
            }
            return stack;
        }


        private static List<MethodBase> FindMethods(IEnumerable<MethodBase> allMethods, string fullTypeNameQuery, string nameQuery, string? encodedParametersQuery)
        {
            // First we'll find all overloads based on the requested method name
            Regex typeRegex = SimpleFilterToRegex(fullTypeNameQuery);
            Regex methodNameRegex = SimpleFilterToRegex(nameQuery);
            MethodBase[]? matchingMethods = allMethods
                .Where(mi => methodNameRegex.IsMatch(mi.Name))
                .Where(mi => typeRegex.IsMatch(mi.DeclaringType.FullName))
                .ToArray();

            // No we will filter only the overloads matching the parameters query
            List<MethodBase>? output = new List<MethodBase>();
            if (string.IsNullOrEmpty(encodedParametersQuery))
            {
                // No paramters sepcified, return all overloads
                output.AddRange(matchingMethods);
            }
            else
            {
                // Parameter types constrains found, searching matching overloads
                // (Because we allow wildcards in the query, this could be more than a single overload)
                //
                // Using 'Split(' ').First()' to get the TYPE part in case the user has given us also the parameter name:
                // "(object myObj)" ==> "object"
                encodedParametersQuery = encodedParametersQuery.TrimStart('(').TrimEnd(')');
                string[] paramFilters = encodedParametersQuery.Split(',').Select(paramType => paramType.Trim().Split(' ').First()).ToArray();
                foreach (MethodBase methodInfo in matchingMethods)
                {
                    if (CheckParameters(methodInfo, paramFilters))
                    {
                        output.Add(methodInfo);
                    }
                }
            }

            return output;
        }

        private static bool CheckParameters(MethodBase methodInfo, string[] parameterFilters)
        {
            return CheckParametersImplicitThis(methodInfo, parameterFilters) ||
                     CheckParametersExplicitThis(methodInfo, parameterFilters);
        }

        /// <summary>
        /// Checks for signatures where the 'this' parameter is implicit (not specified)
        /// This is the case for .NET instance method signatures.
        /// </summary>
        private static bool CheckParametersImplicitThis(MethodBase methodInfo, string[] parameterFilters)
        {
            var parameters = methodInfo.GetParameters();
            if (parameters.Length != parameterFilters.Length)
                return false;

            // Compare paramter types and filters given by the user
            for (int i = 0; i < parameterFilters.Length; i++)
            {
                Regex r = SimpleFilterToRegex(parameterFilters[i]);
                string normalizedParameterType = TypeNameUtils.Normalize(parameters[i].ParameterType.FullName);
                if (!r.IsMatch(normalizedParameterType))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Checks for signatures where the 'this' parameter is explicit (It's the first parameter)
        /// This is the case for C++ instance method signatures.
        /// </summary>
        private static bool CheckParametersExplicitThis(MethodBase methodInfo, string[] parameterFilters)
        {
            var parameters = methodInfo.GetParameters();
            if (parameters.Length + 1 != parameterFilters.Length)
                return false;

            // Collect types of parameters and add the type of 'this'
            string[] parameterTypesFullNames = parameters
                .Select(p => p.ParameterType!.FullName)
                .Prepend(methodInfo.DeclaringType!.FullName + "*") // The type of 'this' should be a pointer
                .ToArray();

            // Compare parameter types and filters given by the user
            for (int i = 0; i < parameterFilters.Length; i++)
            {
                // Prepare pattern
                string rawPattern = parameterFilters[i];
                // HACK: If we don't have the module name, accept any module name (or none at all, for primitives like "uint64_t")
                if (!rawPattern.Contains('!'))
                    rawPattern = "(.*!)?" + rawPattern;
                rawPattern = $"^{rawPattern}$";
                Regex r = new Regex(rawPattern);

                // Prepare target type name to check
                string normalizedParameterType = TypeNameUtils.Normalize(parameterTypesFullNames[i]);
                if (!r.IsMatch(normalizedParameterType))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Turn a "simple filter string" into a regex
        /// </summary>
        /// <param name="simpleFilter">A string that only allow '*' as a wild card meaning "0 or more characters"</param>
        private static Regex SimpleFilterToRegex(string simpleFilter)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append('^'); // Begining of string
            char last = ' ';
            foreach (char c in simpleFilter)
            {
                if (c == '*')
                {
                    if (last == '\\') // Escaped '*'
                        sb.Append("*");
                    else // Wildcard '*'
                        sb.Append(".*");
                }
                else
                {
                    // Current is NOT a asterick, so if the last one was a backslash we need to escape it (by adding a second instance)
                    if (last == '\\')
                        sb.Append('\\');

                    string asEscaped = c.ToString();
                    // Not escaping backslash yet because it might be escaping a *
                    if (c != '\\')
                        asEscaped = Regex.Escape(c.ToString());
                    sb.Append(asEscaped);
                }

                last = c;
            }
            sb.Append('$'); // End of string
            return new Regex(sb.ToString());
        }
    }

}