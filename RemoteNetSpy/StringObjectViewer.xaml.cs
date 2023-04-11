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
        private Type _type;

        public StringObjectViewer(Window parent, string str)
        {
            InitializeComponent();
            double multiplier = parent is ObjectViewer ? 1 : 0.9;
            this.Height = parent.Height * multiplier;
            this.Width = parent.Width * multiplier;
            _type = str.GetType();

            objTypeTextBox.Text = TypeNameUtils.Normalize(_type.FullName);
            contentTextBox.Text = str;
        }

        private void CloseButtonClicked(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
