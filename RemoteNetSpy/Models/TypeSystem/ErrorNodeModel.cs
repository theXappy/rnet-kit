namespace RemoteNetSpy.Models;

public class ErrorNodeModel : ITypeSystemNode
{
    public string Assembly { get; }
    public string ErrorMessage { get; }
    public string DisplayName => ErrorMessage;

    public ErrorNodeModel(string assembly, string errorMessage)
    {
        Assembly = assembly;
        ErrorMessage = errorMessage;
    }
}
