using System.Text.Json;

namespace RnetKit.Common
{
    public class TraceFunction : IComparable
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

        // IComparable Impl
        public int CompareTo(object obj)
        {
            if (obj == null) return 1;
            TraceFunction otherFunction = obj as TraceFunction;
            if (otherFunction != null)
                return this.DemangledName.CompareTo(otherFunction.DemangledName);
            else
                throw new ArgumentException("Object is not a TraceFunction");
        }
    }
}
