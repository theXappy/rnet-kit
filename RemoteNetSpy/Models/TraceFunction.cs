using System;
using System.IO;
using System.Text.Json;

namespace RemoteNetSpy.Models
{
    public class TraceFunction
    {
        public string DemangledName { get; set; }
        public string MangledName { get; set; }

        public TraceFunction(string demangledName, string mangledName)
        {
            DemangledName = demangledName;
            MangledName = mangledName;
        }

        public string ToJson()
        {
            return JsonSerializer.Serialize(this);
        }

        public static TraceFunction FromJson(string json)
        {
            return JsonSerializer.Deserialize<TraceFunction>(json);
        }

        public void SaveToFile(string filePath)
        {
            File.WriteAllText(filePath, ToJson());
        }

        public static TraceFunction LoadFromFile(string filePath)
        {
            string json = File.ReadAllText(filePath);
            return FromJson(json);
        }
    }
}
