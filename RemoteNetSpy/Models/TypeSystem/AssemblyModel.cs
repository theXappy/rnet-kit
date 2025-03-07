using RemoteNET;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;

namespace RemoteNetSpy.Models
{
    [DebuggerDisplay("{Name} ({Runtime}) IsMonitoringAllocation={IsMonitoringAllocation}")]
    public class AssemblyModel : INotifyPropertyChanged
    {
        private bool _isMonitoringAllocation;
        private bool anyTypes;
        private ObservableCollection<DumpedTypeModel> _types;
        private ObservableCollection<DumpedTypeModel> _filteredTypes;

        public string Name { get; private set; }
        public RuntimeType Runtime { get; private set; }

        public bool IsMonitoringAllocation
        {
            get => _isMonitoringAllocation;
            set => SetField(ref _isMonitoringAllocation, value);
        }

        /// <summary>
        /// Whether any types spotted inside this assembly
        /// </summary>
        public bool AnyTypes
        {
            get => anyTypes;
            set => SetField(ref anyTypes, value);
        }
        public ObservableCollection<DumpedTypeModel> Types
        {
            get => _types;
            set
            {
                SetField(ref _types, value);
                ApplyFilter();
            }
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

        public ObservableCollection<DumpedTypeModel> AllTypes
        {
            get => _types;
            set => SetField(ref _types, value);
        }

        public ObservableCollection<DumpedTypeModel> FilteredTypes
        {
            get => _filteredTypes;
        }

        public AssemblyModel(string name, RuntimeType runtime, bool anyTypes)
        {
            Name = name;
            Runtime = runtime;
            IsMonitoringAllocation = false;
            AnyTypes = anyTypes;
            _types = new ObservableCollection<DumpedTypeModel>();
        }

        public AssemblyModel(string name, string runtime, bool anyTypes) : this(name, Enum.Parse<RuntimeType>(runtime), anyTypes)
        {
        }

        private void ApplyFilter()
        {
            IEnumerable<DumpedTypeModel> filteredTypes = _types;
            if (_filter != null)
                filteredTypes = _types.Where(_filter);
            SetField(ref _filteredTypes, new ObservableCollection<DumpedTypeModel>(filteredTypes), propertyName: nameof(FilteredTypes));
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
