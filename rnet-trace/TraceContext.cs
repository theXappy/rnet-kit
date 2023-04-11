using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using RnetKit.Common;

namespace RemotenetTrace
{
    public class TraceContext
    {
        public class MethodParameter
        {
            public string Type { get; set; }
            public string Name { get; set; }

            public MethodParameter(string type, string name)
            {
                Type = type;
                Name = name;
            }

            public override string ToString() => $"{Type} {Name}";
        }

        public string StackTrace { get; private set; }
        public DateTime StartTime { get; private set; }
        public string ClassName { get; private set; }
        public string MethodName { get; private set; }
        public MethodParameter[] Parameters { get; private set; }

        public TraceContext(string stackTrace, DateTime startTime, string className, string methodName, string[] paramTypes, string[] paramNames)
        {
            StackTrace = stackTrace;
            StartTime = startTime;
            ClassName = className;
            MethodName = methodName;
            Parameters = paramTypes.Zip(paramNames).Select(tuple => new MethodParameter(tuple.First, tuple.Second)).ToArray();
        }

        private string PrettyPrintTypeName(string typeName) => TypeNameUtils.Normalize(typeName);
        private string PrettyPrintParameter(MethodParameter parameter) => $"{PrettyPrintTypeName(parameter.Type)} {parameter.Name}";
        public string PrettyParametersList() => String.Join(", ", Parameters.Select(PrettyPrintParameter));

        public Dictionary<string, object> ToDictionary()
        {
            var props = typeof(TraceContext).GetProperties();
            Dictionary<string, object> results = new();
            foreach (var prop in props)
            {
                results[prop.Name] = prop.GetValue(this);
            }

            return results;
        }

    }
}
