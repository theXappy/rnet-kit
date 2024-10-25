using RemoteNET;
using System.ComponentModel;
using System.Linq;

namespace RemoteNetSpy;

public class RemoteAppModel : INotifyPropertyChanged
{
    public RemoteApp App { get; set; }

    private bool _hasIInspectables;
    public bool HasIInspectables
    {
        get { return _hasIInspectables; }
        set
        {
            _hasIInspectables = value;
            OnPropertyChanged(nameof(HasIInspectables));
        }
    }
    public void Update(RemoteApp app)
    {
        App = app;
        try
        {
            CandidateType candidate = app.QueryTypes("WinRT.IInspectable").FirstOrDefault();
            HasIInspectables = candidate != null;
        }
        catch
        {
            HasIInspectables = false;
        }
    }

    public void PerformCast(HeapObject heapObject, string newType)
    {
        try
        {
            RemoteObject newRemoteObject = heapObject.RemoteObject.Cast(newType);
            heapObject.RemoteObject = newRemoteObject;
            heapObject.FullTypeName = newType;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to cast to {newType}.", ex);
        }
    }

    public void UpdateRemoteObjectList(HeapObject oldHeapObject, RemoteObject newRemoteObject)
    {
        oldHeapObject.RemoteObject = newRemoteObject;
        oldHeapObject.FullTypeName = newRemoteObject.TypeName;
    }

    #region INotifyPropertyChanged

    public event PropertyChangedEventHandler PropertyChanged;

    protected void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    #endregion
}
