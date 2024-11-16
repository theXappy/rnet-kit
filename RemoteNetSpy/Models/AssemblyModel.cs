using RemoteNET;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Collections.ObjectModel;

namespace RemoteNetSpy.Models
{
    [DebuggerDisplay("{Name} ({Runtime}) IsMonitoringAllocation={IsMonitoringAllocation}")]
    public class AssemblyModel : INotifyPropertyChanged
    {
        private bool _isMonitoringAllocation;
        private bool anyTypes;
        private ObservableCollection<DumpedTypeModel> _fullTypesList;
        private ObservableCollection<DumpedTypeModel> _filteredTypesList;

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

        public ObservableCollection<DumpedTypeModel> FullTypesList
        {
            get => _fullTypesList;
            set => SetField(ref _fullTypesList, value);
        }

        public ObservableCollection<DumpedTypeModel> FilteredTypesList
        {
            get => _filteredTypesList;
            set => SetField(ref _filteredTypesList, value);
        }

        public AssemblyModel(string name, RuntimeType runtime, bool anyTypes)
        {
            Name = name;
            Runtime = runtime;
            IsMonitoringAllocation = false;
            AnyTypes = anyTypes;
            FullTypesList = new ObservableCollection<DumpedTypeModel>();
            FilteredTypesList = new ObservableCollection<DumpedTypeModel>();
        }

        public AssemblyModel(string name, string runtime, bool anyTypes) : this(name, Enum.Parse<RuntimeType>(runtime), anyTypes)
        {
        }

        public void UpdateFilteredTypesList(Func<DumpedTypeModel, bool> filterCriteria)
        {
            FilteredTypesList.Clear();
            foreach (var type in FullTypesList)
            {
                if (filterCriteria(type))
                {
                    FilteredTypesList.Add(type);
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
