using HostingWfInWPF;
using RemoteNET;
using RemoteNetSpy.Models.Helpers;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace RemoteNetSpy.Models;

public class RemoteAppModel : INotifyPropertyChanged
{
    public RemoteApp App { get; private set; }
    public int TargetPid { get; set; }


    private bool _hasIInspectables;

    public ClassesModel ClassesModel { get; private set; }
    public FunctionsTracer Tracer { get; }
    public Interactor Interactor { get; }


    public bool HasIInspectables
    {
        get { return _hasIInspectables; }
        set
        {
            SetField(ref _hasIInspectables, value);
        }
    }

    public RuntimeType TargetRuntime => App is UnmanagedRemoteApp ? RuntimeType.Unmanaged : RuntimeType.Managed;

    public RemoteAppModel()
    {
        ClassesModel = new ClassesModel(this);
        Tracer = new FunctionsTracer(this);
        Interactor = new Interactor(this);
    }

    public void Update(RemoteApp app, int pid)
    {
        try
        {
            App?.Dispose();
        }
        catch
        {
            // Ignored
        }

        App = app;
        TargetPid = pid;
        _ = Task.Run(QueryIInspectables);
    }

    private void QueryIInspectables()
    {
        try
        {
            CandidateType candidate = App.QueryTypes("WinRT.IInspectable").FirstOrDefault();
            HasIInspectables = candidate != null;
        }
        catch
        {
            HasIInspectables = false;
        }
    }

    public string UnmanagedFlagIfNeeded()
    {
        if (TargetRuntime == RuntimeType.Unmanaged)
            return "-u";
        return string.Empty;
    }

    #region INotifyPropertyChanged

    public event PropertyChangedEventHandler PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    #endregion
}
