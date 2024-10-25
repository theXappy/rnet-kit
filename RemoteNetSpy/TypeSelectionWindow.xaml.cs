using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace RemoteNetSpy
{
    public partial class TypeSelectionWindow : Window
    {
        public string SelectedType { get; private set; }

        public TypeSelectionWindow()
        {
            InitializeComponent();
            DataContext = new TypeSelectionViewModel();
            PopulateTypesListBox();
        }

        private void PopulateTypesListBox()
        {
            TypeSelectionViewModel viewModel = (TypeSelectionViewModel)DataContext;
            List<string> allTypes = viewModel.GetAllTypes();
            typesListBox.ItemsSource = allTypes;
        }

        private void TypesListBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            SelectedType = (string)typesListBox.SelectedItem;
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }
    }

    public class TypeSelectionViewModel
    {
        private RemoteAppModel _appModel;

        public TypeSelectionViewModel()
        {
            _appModel = new RemoteAppModel();
        }

        public List<string> GetAllTypes()
        {
            return _appModel.App.QueryTypes("*").Select(t => t.FullTypeName).ToList();
        }
    }
}
