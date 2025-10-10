using System.Collections.Generic;

namespace rnet_class_dump.Models
{
    public class ClassModel
    {
        public List<string> OverlappingTypeFullNames { get; set; } = new();
        public string ClassName { get; set; } = string.Empty;
        public string? NamespaceName { get; set; }
        public bool HasNamespace { get; set; }
        public string[] NamespaceParts { get; set; } = Array.Empty<string>();
        public bool[] ParentClassFlags { get; set; } = Array.Empty<bool>();
        public string[] IndentStrings { get; set; } = Array.Empty<string>();
        public string FullName { get; set; } = string.Empty;
        public string DllName { get; set; } = string.Empty;
        public List<MemberModel> Members { get; set; } = new();
        public string Indent { get; set; } = string.Empty;
    }

    public class MemberModel
    {
        public string? Comment { get; set; }
        public string[] Content { get; set; }
    }
}