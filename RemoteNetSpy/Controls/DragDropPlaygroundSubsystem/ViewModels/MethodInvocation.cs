using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;

namespace DragDropExpressionBuilder
{
    public class MethodInvocationParameter : INotifyPropertyChanged
    {
        private Type _type;
        private string _paramName;
        private Instance _assignedInstance;

        public Type Type
        {
            get => _type;
        }

        public string TypeName
        {
            get => _type.Name;
            //set { if (_typeName != value) { _typeName = value; OnPropertyChanged(nameof(TypeName)); } }
        }
        public string ParamName
        {
            get => _paramName;
            set { if (_paramName != value) { _paramName = value; OnPropertyChanged(nameof(ParamName)); } }
        }
        public Instance AssignedInstance
        {
            get => _assignedInstance;
            set 
            {
                if (_assignedInstance != value) 
                {
                    _assignedInstance = value;
                    OnPropertyChanged(nameof(AssignedInstance));
                    OnPropertyChanged(nameof(ParamName));
                } 
            }
        }
        public MethodInvocationParameter(Type type, string paramName)
        {
            _type = type;
            ParamName = paramName;
        }
        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public class MethodInvocation : INotifyPropertyChanged
    {
        public MethodInfoWrapper Method { get; }
        public ObservableCollection<MethodInvocationParameter> Parameters { get; } = new();
        public MethodInvocationParameter ThisInstance { get; }
        public ICommand PlayCommand { get; }

        private bool _isStatic;
        public bool IsStatic
        {
            get => _isStatic;
            private set
            {
                if (_isStatic != value)
                {
                    _isStatic = value;
                    OnPropertyChanged(nameof(IsStatic));
                }
            }
        }

        private MethodInvocationParameter _returnValue;
        public MethodInvocationParameter ReturnValue
        {
            get => _returnValue;
            set
            {
                if (_returnValue != value)
                {
                    _returnValue = value;
                    OnPropertyChanged(nameof(ReturnValue));
                }
            }
        }

        public MethodInvocation(MethodInfoWrapper method)
        {
            Method = method;
            foreach (var p in method.Method.GetParameters())
            {
                Parameters.Add(new MethodInvocationParameter(p.ParameterType, p.Name));
            }
            PlayCommand = new DelegateCommand(Invoke);
            ThisInstance = new MethodInvocationParameter(method.Method.DeclaringType, "this");
            _isStatic = method.Method.IsStatic;
        }

        public void Invoke()
        {
            // Validate all parameters have assigned instances
            var method = Method.Method;
            if (method == null)
            {
                System.Windows.MessageBox.Show("No method info available.");
                return;
            }
            var paramInfos = method.GetParameters();
            object[] args = new object[paramInfos.Length];
            for (int i = 0; i < paramInfos.Length; i++)
            {
                var param = Parameters[i];
                if (param.AssignedInstance == null)
                {
                    System.Windows.MessageBox.Show($"Parameter '{param.ParamName}' is not assigned an instance.");
                    return;
                }

                Type expected = paramInfos[i].ParameterType;
                Type actual = param.AssignedInstance.Type;
                if (actual != expected)
                {
                    System.Windows.MessageBox.Show($"Assigned instance type for parameter '{param.ParamName}' ({actual.Name}) does not match method parameter type ({expected.Name}).");
                    return;
                }
                args[i] = param.AssignedInstance.Obj;
            }
            
            object target = null;
            if (!method.IsStatic)
            {
                target = ThisInstance.AssignedInstance.Obj;
            }

            try
            {
                object retVal = method.Invoke(target, args);
                ReturnValue = new MethodInvocationParameter(retVal.GetType(), retVal.ToString())
                {
                    AssignedInstance = new Instance()
                    {
                        Type = retVal.GetType(),
                        Obj = retVal,
                        Tag = retVal.ToString()
                    }
                };
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Invocation failed: {ex.Message}");
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
