using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using RemoteNET;

namespace RemoteNetSpy.Models;

public class HeapObject : INotifyPropertyChanged, IComparable
{
    private ulong _address;
    private RemoteObject remoteObject;
    private string _fullTypeName;
    private string _interactiveRoVarName;
    private string _interactiveDroVarName;

    public ulong Address
    {
        get
        {
            if (RemoteObject != null)
                return RemoteObject.RemoteToken;
            return _address;
        }
        set
        {
            if (RemoteObject != null)
                throw new Exception("Can't set address for frozen heap object");
            _address = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Description));
        }
    }

    public string FullTypeName
    {
        get => _fullTypeName;
        set
        {
            if (value == _fullTypeName) return;
            _fullTypeName = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Description));
        }
    }
    public string InteractiveRoVarName
    {
        get => _interactiveRoVarName;
        set
        {
            if (value == _interactiveRoVarName) return;
            _interactiveRoVarName = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(InteractiveRoVarName));
        }
    }
    public string InteractiveDroVarName
    {
        get => _interactiveDroVarName;
        set
        {
            if (value == _interactiveDroVarName) return;
            _interactiveDroVarName = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(InteractiveDroVarName));
        }
    }

    public RemoteObject RemoteObject
    {
        get => remoteObject;
        set
        {
            remoteObject = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Frozen));
            OnPropertyChanged(nameof(Description));
        }
    }

    public bool Frozen => RemoteObject != null;

    public string Description => $"0x{Address:X16} {FullTypeName}";

    public static HeapObject Parse(string text)
    {
        string[] splitted = text.Split(' ');
        string addressStr = splitted[0];
        if (addressStr.StartsWith("0x"))
            addressStr = addressStr[2..];
        ulong address = Convert.ToUInt64(addressStr, 16);

        return new HeapObject() { Address = address, FullTypeName = splitted[1] };
    }

    public override string ToString()
    {
        return $"HeapObject: 0x{Address:X16} {FullTypeName}";
    }

    public int CompareTo(object obj)
    {
        if (obj is HeapObject casted)
            return _address.CompareTo(casted._address);
        return -1;
    }

    public override bool Equals(object obj)
    {
        if (obj is HeapObject casted)
            return _address.Equals(casted._address);
        return false;
    }

    public override int GetHashCode() => _address.GetHashCode();

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