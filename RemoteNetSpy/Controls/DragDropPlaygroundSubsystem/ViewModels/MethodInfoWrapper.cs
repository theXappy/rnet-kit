using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Reflection;

namespace DragDropExpressionBuilder
{
    public class MethodParameter : INotifyPropertyChanged
    {
        private string _typeName;
        private string _name;
        public string TypeName
        {
            get => _typeName;
            set { if (_typeName != value) { _typeName = value; OnPropertyChanged(nameof(TypeName)); } }
        }
        public string Name
        {
            get => _name;
            set { if (_name != value) { _name = value; OnPropertyChanged(nameof(Name)); } }
        }
        public MethodParameter(string typeName, string name)
        {
            TypeName = typeName;
            Name = name;
        }
        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public class MethodInfoWrapper : INotifyPropertyChanged
    {
        private MethodInfo _method;
        private string _displayName;
        public ObservableCollection<MethodParameter> Parameters { get; } = new();

        public MethodInfo Method
        {
            get => _method;
            set
            {
                if (_method != value)
                {
                    _method = value;
                    OnPropertyChanged(nameof(Method));
                    OnPropertyChanged(nameof(ReturnType));
                    OnPropertyChanged(nameof(Name));
                    OnPropertyChanged(nameof(ArgsList));
                    UpdateParameters();
                }
            }
        }

        public string DisplayName
        {
            get => _displayName;
            set
            {
                if (_displayName != value)
                {
                    _displayName = value;
                    OnPropertyChanged(nameof(DisplayName));
                }
            }
        }

        public string ReturnType => Method?.ReturnType?.Name ?? string.Empty;
        public string Name => Method?.Name ?? string.Empty;
        public string ArgsList => Method == null ? string.Empty : string.Join(", ", Method.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"));

        public MethodInfoWrapper(MethodInfo method)
        {
            Method = method;
            DisplayName = method.Name;
            UpdateParameters();
        }

        private void UpdateParameters()
        {
            Parameters.Clear();
            if (Method != null)
            {
                foreach (var p in Method.GetParameters())
                {
                    Parameters.Add(new MethodParameter(p.ParameterType.Name, p.Name));
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string propertyName) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
