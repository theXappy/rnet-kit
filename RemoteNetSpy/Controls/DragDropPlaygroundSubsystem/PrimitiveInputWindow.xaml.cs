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

namespace RemoteNetSpy.Controls.DragDropPlaygroundSubsystem
{
    /// <summary>
    /// Interaction logic for PrimitiveInputWindow.xaml
    /// </summary>
    public partial class PrimitiveInputWindow : Window
    {
        public string InputValue => InputTextBox.Text;

        public PrimitiveInputWindow(string prompt = "Enter value:")
        {
            InitializeComponent();
            Title = prompt;
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
        }
    }
}
