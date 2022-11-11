using System.Linq.Expressions;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using Microsoft.CSharp.RuntimeBinder;
using Pastel;
using RemoteNET;
using RemoteNET.Internal;

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

        public static ICollection<ColoredExpression> Get(string className, string methodName, int numArgs)
        {
            string path = $"__rnet_handlers__/{methodName}";
            ICollection<ColoredExpression> expressions;
            if (File.Exists(path))
            {
                expressions = File.ReadAllLines(path).Select(ColoredExpression.Parse).ToList();
            }
            else
            {
                expressions = new List<ColoredExpression>();
                expressions.Add(new ColoredExpression("$\"{Convert.ToInt32((DateTime.Now - Context.StartTime).TotalMilliseconds)} ms  \""));
                expressions.Add(new ColoredExpression("$\"[Class: {Context.ClassName}] \".Pastel(Color.DarkOrange)"));
                expressions.Add(new ColoredExpression("$\"{Context.MethodName}\".Pastel(Color.LightGoldenrodYellow)"));
                expressions.Add(new ColoredExpression("$\"({Context.PrettyParametersList()})\\n\".Pastel(Color.DarkOrange)"));
                expressions.Add(new ColoredExpression("$\"\\tArguments:\\n\" "));
                for (int i = 0; i < numArgs; i++)
                {
                    expressions.Add(new ColoredExpression($"$\"\\t\\t[{i}] {{Context.Parameters[{i}].Name}} = {{Args[{i}].ToString()}}\\n\""));
                }
                expressions.Add(new ColoredExpression("\"\\n\""));

                if (!Directory.Exists("__rnet_handlers__"))
                    Directory.CreateDirectory("__rnet_handlers__");
                File.WriteAllLines(path, expressions.Select(exp => exp.ToString()));
            }

            foreach (ColoredExpression exp in expressions)
            {
                //exp.Expression = _converter.ConvertToFormat(exp.Expression);
            }

            return expressions;
        }

        public static ScriptRunner<object> Compile(ColoredExpression exp)
        {
            //var p1 = Expression.Parameter(typeof(TraceContext), "context");
            //var p2 = Expression.Parameter(typeof(DynamicRemoteObject), "instance");
            //var p3 = Expression.Parameter(typeof(DynamicRemoteObject[]), "args");
            //LambdaExpression e = DynamicExpressionParser.ParseLambda(new[] { p1, p2, p3 }, null, exp.Expression);
            //Delegate output = e.Compile();
            //if (!exp.Color.HasValue)
            //    return output;
            //return (Func<TraceContext, DynamicRemoteObject, DynamicRemoteObject[], string>)((c, i, a) => (output.DynamicInvoke(c, i, a) as String).Pastel(exp.Color.Value));
            List<MetadataReference> references = new List<MetadataReference>();
            references.Add(MetadataReference.CreateFromFile(typeof(CSharpArgumentInfo).Assembly.Location));
            references.Add(MetadataReference.CreateFromFile(typeof(Pastel.ConsoleExtensions).Assembly.Location));
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
                    
                }
            }

            string usings = "using System.Drawing;\r\n" +
                            "using System.Linq;\r\n" +
                            "using System;\r\n" +
                            "using Pastel;\n";

            string finalScript = usings + exp.Expression;

            var script = CSharpScript.Create(
                        code: finalScript,
                        options: ScriptOptions.Default.WithReferences(references.ToArray()),
                        globalsType: typeof(HandlerGlobals))
                    .CreateDelegate();
            //.RunAsync(new Globals { C = c });
            return script;

        }
    }

    public class HandlerGlobals
    {
        public TraceContext Context { get; set; }
        public dynamic Instance { get; set; }
        public dynamic[] Args { get; set; }
    }
}
