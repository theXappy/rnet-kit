using System;

namespace RemoteNetSpy;

public class DumpedType
{
    public string Assembly { get; private set; }
    public string FullTypeName { get; private set; }
    private int? _numInstances;
    public bool HaveInstances => _numInstances != null;
    public int? NumInstances
    {
        get
        {
            return _numInstances ?? 0;
        }
        set
        {
            _numInstances = value;
        }
    }

    public DumpedType(string assembly, string fullTypeName, int? numInstances)
    {
        Assembly = assembly;
        FullTypeName = fullTypeName;
        _numInstances = numInstances;
    }

    public override bool Equals(object? obj)
    {
        return obj is DumpedType type &&
               Assembly == type.Assembly &&
               FullTypeName == type.FullTypeName;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Assembly, FullTypeName);
    }
}