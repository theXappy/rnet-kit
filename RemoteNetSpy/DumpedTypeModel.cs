using System;
using System.Diagnostics;

namespace RemoteNetSpy;

[DebuggerDisplay("{FullTypeName} (NumInstances={NumInstances}, PreviousNumInstances={PreviousNumInstances})")]
public class DumpedTypeModel
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
            PreviousNumInstances = _numInstances;
            _numInstances = value;
        }
    }

    public int? PreviousNumInstances { get; private set; }

    public DumpedTypeModel(string assembly, string fullTypeName, int? numInstances)
    {
        Assembly = assembly;
        FullTypeName = fullTypeName;
        _numInstances = numInstances;
        PreviousNumInstances = null;
    }

    public override bool Equals(object obj)
    {
        return obj is DumpedTypeModel type &&
               Assembly == type.Assembly &&
               FullTypeName == type.FullTypeName;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Assembly, FullTypeName);
    }
}
