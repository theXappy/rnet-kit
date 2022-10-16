using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace remotenet_trace
{
    internal class TraceContext
    {
        public string StackTrace { get; private set; }
        public DateTime StartTime { get; private set; }
        public string ClassName { get; private set; }
        public string MethodName { get; private set; }
        public string[] ParamNames { get; private set; }
        public string[] ParamTypes { get; private set; }

        public TraceContext(string stackTrace, DateTime startTime, string className, string methodName, string[] paramNames, string[] paramTypes)
        {
            StackTrace = stackTrace;
            StartTime = startTime;
            ClassName = className;
            MethodName = methodName;
            ParamNames = paramNames;
            ParamTypes = paramTypes;
        }

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
