using RemoteNET;
using System.ComponentModel;

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
            var iinspectableType = app.GetRemoteType("WinRT.IInspectable");
            HasIInspectables = iinspectableType != null;
        }
        catch
        {
            HasIInspectables = false;
        }
    }

    #region INotifyPropertyChanged

    public event PropertyChangedEventHandler PropertyChanged;

    protected void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    #endregion
}