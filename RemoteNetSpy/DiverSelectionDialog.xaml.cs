using RemoteNET;
using System.Windows;

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
            DialogResult = true;
            Close();
        }

        private void UnmanagedButtonClicked(object sender, RoutedEventArgs e)
        {
            SelectedRuntime = RuntimeType.Unmanaged;
            DialogResult = true;
            Close();
        }
    }
}
