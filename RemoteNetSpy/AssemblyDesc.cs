using System;
using System.Diagnostics;
using RemoteNET;

namespace RemoteNetGui;

[DebuggerDisplay("{Name} ({Runtime})")]
public class AssemblyDesc
{
    public string Name { get; private set; }
    public RuntimeType Runtime { get; private set; }

    /// <summary>
    /// Whether any types spotted inside this assembly
    /// </summary>
    public bool AnyTypes { get; set; }

    public AssemblyDesc(string name, RuntimeType runtime, bool anyTypes)
    {
        Name = name;
        Runtime = runtime;
        AnyTypes = anyTypes;
    }

    public AssemblyDesc(string name, string runtime, bool anyTypes) : this(name, Enum.Parse<RuntimeType>(runtime), anyTypes)
    {
    }

    public override bool Equals(object? obj)
    {
        if (obj is not AssemblyDesc other) return false;
        // Intentionally not checking 'anyTypes'
        return Name.Equals(other?.Name) && Runtime.Equals(other?.Runtime);
    }

    public override int GetHashCode()
    {
        return Name.GetHashCode();
    }
}