using System.Linq.Expressions;
using System.Reflection;
using System;
using System.Linq.Expressions;
using System.Linq.Dynamic;
using ScubaDiver.API.Hooking;

namespace remotenet_trace
{
    public static class HandlersRepo
    {
        public static ICollection<string> Get(string className, string methodName, int numArgs)
        {
            string path = $"__rnet_handlers__/{methodName}";
            if (File.Exists(path))
            {
                return File.ReadAllLines(path);
            }
            else
            {
                List<string> expressions = new List<string>();
                for (int i = 0; i < numArgs; i++)
                {
                    expressions.Add($"args[{i}].ToString()");
                }

                if(!Directory.Exists("__rnet_handlers__"))
                    Directory.CreateDirectory("__rnet_handlers__");
                File.WriteAllLines(path, expressions);
                return expressions;
            }
        }

        public static Delegate Compile(string code)
        {
            var p1 = Expression.Parameter(typeof(TraceContext), "context");
            var p2 = Expression.Parameter(typeof(object), "instance");
            var p3 = Expression.Parameter(typeof(object[]), "args");
            LambdaExpression e = System.Linq.Dynamic.Core.DynamicExpressionParser.ParseLambda(new[] { p1, p2, p3 }, null, code);
            return e.Compile();
        }
    }
}
