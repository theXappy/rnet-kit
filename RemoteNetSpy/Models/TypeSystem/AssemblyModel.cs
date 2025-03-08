using RemoteNET;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Data;

namespace RemoteNetSpy.Models
{
    [DebuggerDisplay("{Name} ({Runtime}) IsMonitoringAllocation={IsMonitoringAllocation}")]
    public class AssemblyModel : INotifyPropertyChanged
    {
        private bool _isMonitoringAllocation;
        public object TypesLock = new object();
        public object FilteredTypesLock = new object();
        private readonly SortedObservableCollection<DumpedTypeModel> _types;
        private ObservableCollection<DumpedTypeModel> _filteredTypes;

        public string Name { get; private set; }
        public RuntimeType Runtime { get; private set; }

        public bool IsMonitoringAllocation
        {
            get => _isMonitoringAllocation;
            set => SetField(ref _isMonitoringAllocation, value);
        }

        public IReadOnlyList<DumpedTypeModel> Types
        {
            get => _types;
        }

        private Func<DumpedTypeModel, bool> _filter;
        public Func<DumpedTypeModel, bool> Filter
        {
            set
            {
                SetField(ref _filter, value);
                ApplyFilter();
            }
        }

        public ObservableCollection<DumpedTypeModel> FilteredTypes
        {
            get => _filter != null ? _filteredTypes : _types;
        }

        public AssemblyModel(string name, RuntimeType runtime, bool anyTypes)
        {
            Name = name;
            Runtime = runtime;
            IsMonitoringAllocation = false;
            _types = new SortedObservableCollection<DumpedTypeModel>((x, y) => x.FullTypeName.CompareTo(y.FullTypeName));
            _filteredTypes = new ObservableCollection<DumpedTypeModel>();
        }

        public AssemblyModel(string name, string runtime, bool anyTypes) : this(name, Enum.Parse<RuntimeType>(runtime), anyTypes)
        {
        }

        public void AddType(DumpedTypeModel type)
        {
            lock (TypesLock)
            {
                _types.Add(type);
            }
            ApplyFilter();
        }

        private void ApplyFilter()
        {
            if (_filter == null)
                return;

            lock (FilteredTypesLock)
            {
                _filteredTypes.Clear();
                lock (TypesLock)
                {
                    foreach (var item in _types.Where(_filter))
                    {
                        _filteredTypes.Add(item);
                    }
                }
            }
        }

        public override bool Equals(object obj)
        {
            if (obj is not AssemblyModel other) return false;
            // Intentionally not checking 'anyTypes'
            return Name.Equals(other?.Name) && Runtime.Equals(other?.Runtime);
        }

        public override int GetHashCode()
        {
            return Name.GetHashCode();
        }

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
    }
}
