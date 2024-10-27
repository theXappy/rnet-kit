using System.Windows;

namespace RemoteNetSpy
{
    public partial class ArgumentPromptWindow : Window
    {
        public object[] Arguments { get; private set; }

        public ArgumentPromptWindow(int parameterCount)
        {
            InitializeComponent();
            Arguments = new object[parameterCount];
            for (int i = 0; i < parameterCount; i++)
            {
                var textBox = new TextBox { Margin = new Thickness(0, 0, 0, 10) };
                ArgumentsPanel.Children.Add(textBox);
            }
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            for (int i = 0; i < Arguments.Length; i++)
            {
                if (long.TryParse((ArgumentsPanel.Children[i] as TextBox).Text, out long result))
                {
                    Arguments[i] = result;
                }
                else
                {
                    MessageBox.Show($"Invalid input for argument {i + 1}. Please enter a valid 64-bit number.");
                    return;
                }
            }
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
