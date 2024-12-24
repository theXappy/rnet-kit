using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace RemoteNetSpy.Models;

public class InjectableProcess : INotifyPropertyChanged
{
    private int _pid;
    private string _name;
    private string _dotNetVersion;
    private string _diverState;
    private bool _isProcessDead;
    public int Pid
    {
        get => _pid;
        set
        {
            _pid = value;
            OnPropertyChanged();
        }
    }
    public string Name
    {
        get => _name;
        set
        {
            _name = value;
            OnPropertyChanged();
        }
    }
    public string DotNetVersion
    {
        get => _dotNetVersion;
        set
        {
            _dotNetVersion = value;
            OnPropertyChanged();
        }
    }
    public string DiverState
    {
        get => _diverState;
        set
        {
            _diverState = value;
            OnPropertyChanged();
        }
    }
    public bool IsProcessDead
    {
        get => _isProcessDead;
        set
        {
            _isProcessDead = value;
            OnPropertyChanged();
        }
    }
    public InjectableProcess(int pid, string name, string dotNetVersion, string diverState)
    {
        Pid = pid;
        Name = name;
        DotNetVersion = dotNetVersion;
        DiverState = diverState;
    }

    #region INotifyPropertyChanged
    public event PropertyChangedEventHandler PropertyChanged;
    protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
    #endregion
}