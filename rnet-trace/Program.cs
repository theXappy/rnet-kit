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
using Pastel;
using RemoteNET.Common;
using RnetKit.Common;
using System.Drawing;
using RemoteNET.Internal.Reflection;

namespace QuickStart
{
    class Program
    {
        public class TraceOptions
        {
            [Option('t', "target", Required = true, HelpText = "Target process name. Partial names area allowed but a single match is expected. " +
                "e.g. \"notep\" for notepad")]
            public string TargetProcess { get; set; }
            [Option('i', "include", Required = false, HelpText = "Included Method: Query for a full type identifier of class + method name to include in trace. " +
                "e.g. \"System.Text.StringBuilder.Append\", " +
                "     \"System.Text.StringBuilder.Append(String)\". " +
                "Method substring can contain '*'s as wildcardss")]
            public IEnumerable<string> IncludedMethods { get; set; }
            [Option('l', "include", Required = false, HelpText = "Included Methods File: A path to a file of function queries. " +
                                                                 "See -i for information about queries.")]
            public string IncludedFlistFilePath { get; set; }
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
            List<string> methodQueries = opts.IncludedMethods.ToList();
            if (File.Exists(opts.IncludedFlistFilePath))
            {
                string[] functionsFromFlist = File.ReadAllText(opts.IncludedFlistFilePath).Split("\n", 
                    StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                methodQueries = methodQueries.Concat(functionsFromFlist).ToList();
            }

            if (!methodQueries.Any())
            {
                Console.WriteLine($"Error: No methods included to be traced. Use either -i or -l. Make sure the path give to -l exists.");
                return;
            }


            foreach (var methodQuery in methodQueries)
            {
                string fullTypeNameQuery, methodNameQuery, encodedParametersQuery;
                ParseMethodIdentifier(methodQuery, out fullTypeNameQuery, out methodNameQuery, out encodedParametersQuery);

                // Get all matching types
                List<Type> remoteTypes = new List<Type>();
                var candidates = app.QueryTypes(fullTypeNameQuery).ToList();
                if (candidates.Count == 0)
                {
                    Console.WriteLine(
                        $"WARNING:No types matched query '{fullTypeNameQuery}'");
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
                foreach (Type remoteType in remoteTypes)
                {
                    // Search Methods
                    var allMethods = remoteType.GetMethods();
                    var matchingMethods = FindMethods(allMethods, remoteType.FullName, methodNameQuery, encodedParametersQuery);
                    methodsToHook.AddRange(matchingMethods);
                    // Search Ctors
                    var allCtors = remoteType.GetConstructors();
                    var matchingCtors = FindMethods(allCtors, remoteType.FullName, methodNameQuery, encodedParametersQuery);
                    methodsToHook.AddRange(matchingCtors);

                    if (!methodsToHook.Any())
                    {
                    Console.WriteLine(
                        $"WARNING: Type found, but no methods matched query '{methodQuery}'");
                    }
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

            // Remove bad methods which we couldn't parse right in either the ScubaDiver/Locally
            foreach (var method in methodsToHook.ToList())
            {
                if (method is MethodInfo mi && mi.ReturnType == null)
                {
                    Console.WriteLine($"WARNING: Method {method} had a null instead of a return type. It'll not be hooked.");
                    methodsToHook.Remove(method);
                }
            }

            // Display all overloads to the user
            foreach (MethodBase methodToHook in methodsToHook)
            {
                Console.Write($"Found Method: (Class: {methodToHook.DeclaringType.FullName}) ");
                Console.WriteLine(TypeNameUtils.Normalize(methodToHook));
            }


            // Create local handler functions for all future hooks
            Dictionary<MethodBase, HooksPair> hookHandlers = CreateHookHandlers(methodsToHook, app);

            // Prepare clean-up code for when our program closes (gracefully)
            ManualResetEvent mre = new ManualResetEvent(false);
            bool unhooked = false;
            void Unhook(object o, EventArgs e)
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
                try
                {
                    app.HookingManager.HookMethod(methodAndHook.Key, HarmonyPatchPosition.Prefix, methodAndHook.Value.Pre);
                    Console.WriteLine($"OK!");
                    Console.Write($"Hooking Method: (Class: {method.DeclaringType.FullName}) {method.Name}... (Post) ");
                    app.HookingManager.HookMethod(methodAndHook.Key, HarmonyPatchPosition.Postfix, methodAndHook.Value.Post);
                    Console.WriteLine($"OK!");
                }
                catch(Exception ex)
                {
                    Console.WriteLine($"Oops! Ex:\n{ex}");
                }
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
        private static int _lastPrintingThreadId = 0;
        private static object _printLock = new object();

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
        private static Dictionary<MethodBase, HooksPair> CreateHookHandlers(List<MethodBase> methodsToHook, RemoteApp app)
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
                HookAction preHook = (context, instance, args, retValue) =>
                {
                    var semiCallstack = GetSemiCallstack(context.ThreadId);
                    semiCallstack.Push(uniqueId);
                    string prefix = new string('│', semiCallstack.Count);
                    string firstPrefix = "┌";
                    if (semiCallstack.Count > 1)
                        firstPrefix = new string('│', semiCallstack.Count - 2) + "├┬";

                    // Color prefixes
                    var color = GetModuloColor(context.ThreadId);
                    firstPrefix = firstPrefix.Pastel(color);
                    prefix = prefix.Pastel(color);

                    try
                    {
                        var scriptGlobals = new HandlerGlobals()
                        {
                            App = app,
                            Context = new TraceContext(context.StackTrace, start, className, methodName, paramTypes,
                                paramNames),
                            Instance = instance,
                            Args = args,
                            Output = new StringBuilder()
                        };
                        handlerScript(scriptGlobals);

                        StringBuilder output = scriptGlobals.Output;
                        output.Replace("\n", "\n" + prefix);
                        output.Insert(0, firstPrefix);
                        output.AppendLine();
                        lock (_printLock)
                        {
                            if (_lastPrintingThreadId != context.ThreadId)
                            {
                                _lastPrintingThreadId = context.ThreadId;
                                Console.WriteLine($"[TID=0x{context.ThreadId:X}]".Pastel(color));
                            }
                            Console.Write(scriptGlobals.Output);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(ex);
                    }
                };

                HookAction postHook = (context, instance, args, retValue) =>
                {
                    var color = GetModuloColor(context.ThreadId);
                    var semiCallstack = GetSemiCallstack(context.ThreadId);
                    string lastPrefix = new string('│', semiCallstack.Count - 1) + "└─ END";

                    // Prepare RetValue print
                    string retValueLine = null;
                    if (retValue != null)
                    {
                        retValueLine = (new string('│', semiCallstack.Count)).Pastel(color) + "\tReturn Value: " + retValue;
                    }

                    // Color prefix
                    lastPrefix = lastPrefix.Pastel(color);

                    if (semiCallstack.TryPop(out string lastUniqueId))
                    {
                        if (lastUniqueId != uniqueId)
                        {
                            throw new Exception(
                                $"ERROR: Mismatching popped method and currently exiting method. Current: {uniqueId} , Popped: {lastUniqueId}");
                        }

                        lock (_printLock)
                        {
                            if (_lastPrintingThreadId != context.ThreadId)
                            {
                                _lastPrintingThreadId = context.ThreadId;
                                Console.WriteLine($"[TID=0x{context.ThreadId:X}]".Pastel(color));
                            }

                            if (retValueLine != null)
                                Console.WriteLine(retValueLine);

                            Console.WriteLine(lastPrefix);
                        }
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
        public static Color GetModuloColor(int i)
        {
            // Calculate the modulo of 'i' with 5 to get a value in the range [0, 4].
            int moduloResult = i % 5;

            // Use a switch statement to map the modulo result to a System.Drawing.Color.
            switch (moduloResult)
            {
                case 0:
                    return Color.Cyan;
                case 1:
                    return Color.LawnGreen;
                case 2:
                    return Color.IndianRed;
                case 3:
                    return Color.MediumPurple;
                case 4:
                    return Color.Orange;
                default:
                    throw new ArgumentOutOfRangeException("i", "Input 'i' should be a non-negative integer.");
            }
        }


        private static List<MethodBase> FindMethods(IEnumerable<MethodBase> allMethods, string fullTypeNameQuery, string nameQuery, string encodedParametersQuery)
        {
            // First we'll find all overloads based on the requested method name
            Regex typeRegex = SimpleFilterToRegex(fullTypeNameQuery);
            Regex methodNameRegex = SimpleFilterToRegex(nameQuery);

            MethodBase[] matchingMethods;
            if (allMethods.Any(mi => mi is RemoteRttiMethodInfo))
            {
                var allRttiMethods = allMethods.Cast<RemoteRttiMethodInfo>();
                matchingMethods = allRttiMethods
                    .Where(rttiMethod =>
                    {
                        return methodNameRegex.IsMatch(rttiMethod.MangledName) ||
                                methodNameRegex.IsMatch(rttiMethod.Name);
                    })
                    .Where(rttiMethod =>
                    {
                        return typeRegex.IsMatch(rttiMethod.LazyDeclaringType.TypeFullName);
                    })
                    .ToArray();
            }
            else 
            {
                matchingMethods = allMethods
                    .Where(mi => methodNameRegex.IsMatch(mi.Name))
                    .Where(mi =>
                    {
                        // This one will trigger remote type resolution for every parameter type...
                        Debug.WriteLine("[@@@] SLOW declaring type resolution!");
                        return typeRegex.IsMatch(mi.DeclaringType.FullName);
                    })
                    .ToArray();
            }

            // No we will filter only the overloads matching the parameters query
            encodedParametersQuery = encodedParametersQuery?.TrimStart('(').TrimEnd(')');
            List<MethodBase> output = new List<MethodBase>();
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
                List<string> splitted = ParseParametersQuery(encodedParametersQuery);
                string[] paramFilters = new string[splitted.Count];
                for (int i = 0; i < splitted.Count; i++)
                {
                    // We don't know if we were given "arg_type arg_name" or just "arg_name".
                    // So checking here to get only "arg_name"
                    paramFilters[i] = splitted[i];
                    if (Regex.IsMatch(splitted[i], "^.* [a-zA-Z0-9_]+$"))
                        paramFilters[i] = splitted[i].Substring(0, splitted[i].LastIndexOf(' '));
                }

                // Normalize the types
                for (int i = 0; i < paramFilters.Length; i++)
                {
                    paramFilters[i] = TypeNameUtils.Normalize(paramFilters[i]);
                }

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

        private static List<string> ParseParametersQuery(string input)
        {
            List<string> result = new List<string>();
            StringBuilder current = new StringBuilder();
            int bracketDepth = 0;

            foreach (char c in input)
            {
                if (c == ',' && bracketDepth == 0)
                {
                    result.Add(current.ToString().Trim());
                    current.Clear();
                }
                else
                {
                    current.Append(c);
                    if (c == '[')
                    {
                        bracketDepth++;
                    }
                    else if (c == ']')
                    {
                        bracketDepth--;
                    }
                }
            }

            if (current.Length > 0)
            {
                result.Add(current.ToString().Trim());
            }

            // result = result.Select(encodedParam => TypeNameUtils.Normalize(encodedParam)).ToList();

            return result;
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