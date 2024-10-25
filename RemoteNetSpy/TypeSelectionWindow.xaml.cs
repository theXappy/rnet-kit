using System.Windows;

namespace RemoteNetSpy
{
    public partial class TypeSelectionWindow : Window
    {
        public DumpedType SelectedType { get; private set; }

        public TypeSelectionWindow()
        {
            InitializeComponent();
            TypesControl.DataContext = new TypesModel();
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            SelectedType = (TypesControl.DataContext as TypesModel)?.SelectedType;
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
