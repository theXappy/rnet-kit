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
                Console.Write($"Hooking Method: (Class: {methodToHook.DeclaringType.FullName}) ");
                Console.WriteLine(TypeNameUtils.Normalize(methodToHook));
            }


            // Create local handler functions for all future hooks
            Dictionary<MethodBase, HookAction> hookHandlers = CreateHookHandlers(methodsToHook);

            // Prepare clean-up code for when our program closes (gracefully)
            ManualResetEvent mre = new ManualResetEvent(false);
            bool unhooked = false;
            void Unhook(object? o, EventArgs e)
            {
                if (unhooked)
                    return;
                unhooked = true;

                Console.WriteLine("Unhooking...");
                foreach (KeyValuePair<MethodBase, HookAction> methodAndHook in hookHandlers)
                {
                    (app as ManagedRemoteApp).HookingManager.UnhookMethod(methodAndHook.Key, methodAndHook.Value);
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
            foreach (KeyValuePair<MethodBase, HookAction> methodAndHook in hookHandlers)
            {
                Console.WriteLine(methodAndHook.Key.Name);
                (app as ManagedRemoteApp).HookingManager.HookMethod(methodAndHook.Key, HarmonyPatchPosition.Prefix, methodAndHook.Value);
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

        private static Dictionary<MethodBase, HookAction> CreateHookHandlers(List<MethodBase> methodsToHook)
        {
            DateTime start = DateTime.Now;
            Dictionary<MethodBase, HookAction> generatedHooks = new();
            // Generate unique hook for every overload - To show the right signature when invoked
            foreach (MethodBase method in methodsToHook)
            {
                string humanizedParametersList = string.Join(", ", method.GetParameters().Select(pi => pi.ToString()));
                string className = method.DeclaringType.FullName;
                string methodName = method.Name;
                string[] paramNames = method.GetParameters().Select(param => param.Name).ToArray();
                string[] paramTypes = method.GetParameters().Select(param => param.ParameterType.ToString()).ToArray();

                var script = HandlersRepo.Get(className, method.Name, paramTypes.Length);
                ScriptRunner<object> handlerScript = HandlersRepo.Compile(script);
                HookAction hAction = (context, instance, args) =>
                {
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
                        Console.Write(scriptGlobals.Output);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(ex);
                    }
                };

                generatedHooks[method] = hAction;
            }
            return generatedHooks;
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
        /// Turn a "simple filter string" into a regex
        /// </summary>
        /// <param name="simpleFilter">A string that only allow '*' as a wild card meaning "0 or more characters"</param>
        private static Regex SimpleFilterToRegex(string simpleFilter)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append('^'); // Begining of string
            foreach (char c in simpleFilter)
            {
                if (c == '*')
                {
                    sb.Append(".*");
                }
                else
                {
                    string asEscaped = Regex.Escape(c.ToString());
                    sb.Append(asEscaped);
                }
            }
            sb.Append('$'); // End of string
            return new Regex(sb.ToString());
        }
    }

}