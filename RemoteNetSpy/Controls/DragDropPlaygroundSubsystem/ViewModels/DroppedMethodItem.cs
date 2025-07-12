using System;
using System.ComponentModel;

namespace DragDropExpressionBuilder
{
    public class DroppedMethodItem : INotifyPropertyChanged
    {
        private MethodInvocation _invocation;
        private double _x;
        private double _y;
        private double _z;

        public MethodInvocation Invocation
        {
            get => _invocation;
            set { if (_invocation != value) { _invocation = value; OnPropertyChanged(nameof(Invocation)); } }
        }
        public double X
        {
            get => _x;
            set { if (_x != value) { _x = value; OnPropertyChanged(nameof(X)); } }
        }
        public double Y
        {
            get => _y;
            set { if (_y != value) { _y = value; OnPropertyChanged(nameof(Y)); } }
        }

        public DroppedMethodItem(MethodInvocation invocation, double x, double y)
        {
            Invocation = invocation;
            X = x;
            Y = y;
        }
        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string propertyName) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
