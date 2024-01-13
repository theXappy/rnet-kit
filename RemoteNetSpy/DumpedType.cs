namespace RemoteNetSpy;

public class DumpedType
{
    public string FullTypeName { get; private set; }
    private int? _numInstances;
    public bool HaveInstances => _numInstances != null;
    public int NumInstances => _numInstances ?? 0;

    public DumpedType(string fullTypeName, int? numInstances)
    {
        FullTypeName = fullTypeName;
        _numInstances = numInstances;
    }
}