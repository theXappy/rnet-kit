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

namespace RemoteNetSpy.Windows
{
    /// <summary>
    /// Interaction logic for DllProxySuggestionPrompt.xaml
    /// </summary>
    public partial class DllProxySuggestionPrompt : Window
    {
        public string ProcessName { get; set; }
        public int ProcessId { get; set; }
        public bool ShouldTryProxy { get; private set; }

        public DllProxySuggestionPrompt(string processName, int processId)
        {
            InitializeComponent();
            ProcessName = processName;
            ProcessId = processId;
            DataContext = this;
        }

        private void TryProxyButton_Click(object sender, RoutedEventArgs e)
        {
            ShouldTryProxy = true;
            DialogResult = true;
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            ShouldTryProxy = false;
            DialogResult = false;
        }
    }
}
