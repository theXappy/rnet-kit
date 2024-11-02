using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace RemoteNetSpy.Models
{
    public class TypesModel : INotifyPropertyChanged
    {
        private ObservableCollection<DumpedTypeModel> _types;
        private DumpedTypeModel _selectedType;

        public ObservableCollection<DumpedTypeModel> Types
        {
            get => _types;
            set => SetField(ref _types, value);
        }

        public DumpedTypeModel SelectedType
        {
            get => _selectedType;
            set => SetField(ref _selectedType, value);
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
