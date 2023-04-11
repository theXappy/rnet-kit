using System;
using RemoteNET;
using System.Collections.ObjectModel;
using System.Windows;
using RemoteNET.Internal;
using RnetKit.Common;

namespace RemoteNetSpy
{
    /// <summary>
    /// Interaction logic for StringObjectViewer.xaml
    /// </summary>
    public partial class StringObjectViewer : Window
    {
        private RemoteObject _ro;
        private Type _type;

        public StringObjectViewer(Window parent, RemoteObject ro)
        {
            InitializeComponent();
            double multiplier = parent is ObjectViewer ? 1 : 0.9;
            this.Height = parent.Height * multiplier;
            this.Width = parent.Width * multiplier;
            _ro = ro;
            _type = _ro.GetType();

            DynamicRemoteObject dro = _ro.Dynamify() as DynamicRemoteObject;

            objTypeTextBox.Text = TypeNameUtils.Normalize(_ro.GetType().FullName);
            objAddrTextBox.Text = $"0x{_ro.RemoteToken:x8}";
            contentTextBox.Text = dro.ToString();
        }

        private void CloseButtonClicked(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
