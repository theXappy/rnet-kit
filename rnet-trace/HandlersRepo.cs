using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using Microsoft.CSharp.RuntimeBinder;
using Color = System.Drawing.Color;

namespace RemotenetTrace
{
    public static class HandlersRepo
    {
        public class ColoredExpression
        {
            public string Expression { get; set; }
            public Color? Color { get; set; }

            public ColoredExpression(string expression, Color? color = null)
            {
                Expression = expression;
                Color = color;
            }

            public static ColoredExpression Parse(string input)
            {
                string exp = input;
                System.Drawing.Color? color = null;
                //if (!input.TrimEnd().EndsWith('"'))
                //{
                //    int lastComma = input.LastIndexOf(',');
                //    color = System.Drawing.Color.FromName(input[(lastComma + 1)..]);
                //    exp = input.Substring(0, lastComma);
                //}
                return new ColoredExpression(exp, color);
            }

            public override string ToString() =>
                Expression
                //+ (Color.HasValue ? $",{Color.Value.Name}" : string.Empty)
                ;
        }

        private static CSharpStringsConverter _converter = new();

        public static string Get(string className, string methodName, bool isStatic, int numArgs)
        {
            string script = "";
            string uniqueId= $"{className}!{methodName}!{numArgs}";
            // Sanitize path
            string path = "__rnet_handlers__/" + string.Join("", uniqueId.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
            if (File.Exists(path))
            {
                script = File.ReadAllText(path);
            }
            else
            {
                // Add comment section indicating available globals
                script += "// Available globals:\n";
                script += "// App: RemoteApp instance\n";
                script += "// Context: TraceContext instance\n";
                script += "// Instance: dynamic instance of the hooked class\n";
                script += "// Args: dynamic array of method arguments\n";
                script += "// Output: StringBuilder for appending output\n";
                script += "// TraceContext fields and properties:\n";
                script += "// - StackTrace: string\n";
                script += "// - StartTime: DateTime\n";
                script += "// - ClassName: string\n";
                script += "// - MethodName: string\n";
                script += "// - Parameters: array of MethodParameter\n";
                script += "// MethodParameter fields:\n";
                script += "// - Type: string\n";
                script += "// - Name: string\n\n";

                script += "Output.Append($\"{Convert.ToInt32((DateTime.Now - Context.StartTime).TotalMilliseconds)} ms  \");\r\n";
                script += "Output.Append($\"[Class: {Context.ClassName}] \".Pastel(Color.FromArgb(78, 201, 176)));\r\n";
                script += "Output.Append($\"{Context.MethodName}\".Pastel(Color.FromArgb(220, 220, 170)));\r\n";
                script += "Output.AppendLine($\"({Context.PrettyParametersList()})\".Pastel(Color.FromArgb(220, 220, 170)));\r\n";
                // Print instance (this/self) - if the method is not static
                if (!isStatic)
                {
                    script += "Output.AppendLine($\"\\tInstance:\");\r\n";
                    script += $"Output.Append($\"\\t\\t [-] this = \");\r\n";
                    script += "try {\r\n";
                    script += $"\tOutput.AppendLine((Instance?.ToString() ?? \"null\"));\r\n";
                    script += "} catch (Exception ex) {\r\n";
                    script += "\tOutput.AppendLine($\"(!) Error reading instance: {ex}\");\r\n";
                    script += "}\r\n";
                }
                // Print arguments
                if (numArgs != 0)
                    script += "Output.AppendLine($\"\\tArguments:\");\r\n";
                for (int i = 0; i < numArgs; i++)
                {
                    script += $"Output.Append($\"\\t\\t [{i}] {{Context.Parameters[{i}].Name}} = \");\r\n";
                    script +=  "try {\r\n";
                    script += $"\tOutput.AppendLine((Args[{i}]?.ToString() ?? \"null\"));\r\n";
                    script +=  "} catch (Exception ex) {\r\n";
                    script +=  "\tOutput.AppendLine($\"(!) Error reading parameter: {ex}\");\r\n";
                    script +=  "}\r\n";
                }
                //script += "Output.AppendLine();\r\n";

                if (!Directory.Exists("__rnet_handlers__"))
                    Directory.CreateDirectory("__rnet_handlers__");
                File.WriteAllText(path, script);
            }

            return script;
        }

        public static ScriptRunner<object> Compile(string script)
        {
            List<MetadataReference> references = new List<MetadataReference>();
            references.Add(MetadataReference.CreateFromFile(typeof(Random).Assembly.Location));
            references.Add(MetadataReference.CreateFromFile(typeof(CSharpArgumentInfo).Assembly.Location));
            references.Add(MetadataReference.CreateFromFile(typeof(Pastel.ConsoleExtensions).Assembly.Location));
            references.Add(MetadataReference.CreateFromFile(typeof(System.Drawing.Color).Assembly.Location));
            var domain = AppDomain.CurrentDomain;
            foreach (Assembly assembly in domain.GetAssemblies())
            {
                try
                {
                    if (!assembly.IsDynamic && File.Exists(assembly.Location))
                    {
                        references.Add(MetadataReference.CreateFromFile(assembly.Location));
                    }
                }
                catch
                {
                    // Ignore
                }
            }

            string usings = "using System.Drawing;\r\n" +
                            "using System.Linq;\r\n" +
                            "using System;\n" +
                            "using Pastel;\n" +
                            "using Color = System.Drawing.Color;\n\n";

            string finalScript = usings + script;

            //Debug.WriteLine("Compiled handler:\n" + finalScript);

            var compiledScript = CSharpScript.Create(
                        code: finalScript,
                        options: ScriptOptions.Default.WithReferences(references.ToArray()),
                        globalsType: typeof(HandlerGlobals))
                    .CreateDelegate();
            return compiledScript;

        }
    }
}
