using RemoteNET;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace RemoteNetSpy
{
    /// <summary>
    /// Interaction logic for DiverSelectionDialog.xaml
    /// </summary>
    public partial class DiverSelectionDialog : Window
    {
        public RuntimeType SelectedRuntime { get; set; }

        public DiverSelectionDialog()
        {
            InitializeComponent();
        }

        private void ManagedButtonClicked(object sender, RoutedEventArgs e)
        {
            SelectedRuntime = RuntimeType.Managed;
            this.DialogResult = true;
            this.Close();
        }
        private void UnmanagedButtonClicked(object sender, RoutedEventArgs e)
        {
            SelectedRuntime = RuntimeType.Unmanaged;
            this.DialogResult = true;
            this.Close();
        }
    }
}
