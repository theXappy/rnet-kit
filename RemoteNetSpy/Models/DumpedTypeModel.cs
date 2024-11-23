using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace RemoteNetSpy.Models;

[DebuggerDisplay("{FullTypeName} (NumInstances={NumInstances}, PreviousNumInstances={PreviousNumInstances})")]
public class DumpedTypeModel : INotifyPropertyChanged
{
    public string Assembly { get; private set; }
    public string FullTypeName { get; private set; }
    private int? _numInstances;
    public bool HaveInstances => _numInstances != null && _numInstances > 0;
    public int? NumInstances
    {
        get
        {
            return _numInstances ?? 0;
        }
        set
        {
            SetField(ref _numInstances, value);
            OnPropertyChanged(propertyName: nameof(HaveInstances));
        }
    }

    public int? PreviousNumInstances { get; set; }

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
