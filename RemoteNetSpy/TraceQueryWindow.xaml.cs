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
    /// Interaction logic for TraceQueryWindow.xaml
    /// </summary>
    public partial class TraceQueryWindow : Window
    {
        public string[] Queries { get; set; }

        private List<TextBox> queryBoxes = new List<TextBox>();

        public TraceQueryWindow()
        {
            InitializeComponent();
            var iter = grid.Children;
            foreach (UIElement curr in iter)
            {
                if (curr is TextBox t)
                {
                    queryBoxes.Add(t);
                }
            }
        }

        private void okButtonClicked(object sender, RoutedEventArgs e)
        {
            if (queryBoxes.All(b => string.IsNullOrWhiteSpace(b.Text)))
            {
                MessageBox.Show("Query box can't be empty.");
                return;
            }
            Queries = queryBoxes.Where(b => !string.IsNullOrWhiteSpace(b.Text))
                .Select(b => b.Text)
                .ToArray();

            this.DialogResult = true;
            this.Close();
        }

        private void cancelButtonClicked(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }
    }
}
