using System.Windows;

namespace RemoteNetSpy
{
    public partial class TypeSelectionWindow : Window
    {
        string _regexToApply = null;
        public TypeSelectionWindow()
        {
            InitializeComponent();
        }


        public void ApplyRegexFilter(string pattern)
        {
            // If we try to set it BEFORE the window is shown, this doesn't work since the
            // DataContext of TypesControl is still null. So we're saving this askide
            _regexToApply = pattern;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            if (_regexToApply == null)
                return;

            TypesControl.UseRegex = true;
            TypesControl.SetFilter(_regexToApply);
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
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
