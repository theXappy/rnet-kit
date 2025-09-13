using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Data;

namespace RemoteNetSpy.Models;

public class TypeViewState : INotifyPropertyChanged
{
    private bool _isHeapSearchInProgress;
    private IEnumerable _heapInstancesSource;
    private DumpedTypeModel _typeModel;

    public DumpedTypeModel TypeModel
    {
        get => _typeModel;
        set => SetField(ref _typeModel, value);
    }

    public bool IsHeapSearchInProgress
    {
        get => _isHeapSearchInProgress;
        set
        {
            if (SetField(ref _isHeapSearchInProgress, value))
            {
                OnPropertyChanged(nameof(ShouldShowHeapInstancesOverlay));
            }
        }
    }

    public IEnumerable HeapInstancesSource
    {
        get => _heapInstancesSource;
        set
        {
            if (SetField(ref _heapInstancesSource, value))
            {
                OnPropertyChanged(nameof(ShouldShowHeapInstancesOverlay));
            }
        }
    }

    public bool ShouldShowHeapInstancesOverlay
    {
        get
        {
            // Don't show overlay if search is in progress
            if (IsHeapSearchInProgress)
                return false;

            // Show overlay if ItemsSource is empty or null
            if (HeapInstancesSource == null)
                return true;

            if (HeapInstancesSource is ICollectionView collectionView)
                return !collectionView.Cast<object>().Any();

            return !HeapInstancesSource.Cast<object>().Any();
        }
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