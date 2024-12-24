using System.Text.Json;

namespace RnetKit.Common
{
    public class TraceFunction
    {
        public string DemangledName { get; set; }
        public string FullMangledName { get; set; }

        public TraceFunction(string demangledName, string fullMangledName)
        {
            DemangledName = demangledName;
            FullMangledName = fullMangledName;
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
