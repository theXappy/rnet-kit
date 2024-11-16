using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using RemoteNetSpy.Models;
using RemoteNetSpy.Converters;

namespace RemoteNetSpy.Controls
{
    public partial class TypesTreeControl : UserControl
    {
        public bool UseRegex { get; set; } = false;
        private bool _matchCaseTypes = false;
        private bool _onlyTypesInHeap = false;
        private DumpedTypeToDescription _dumpedTypeToDescription = new DumpedTypeToDescription();
        private string _currentFilter;

        public event Action<string> GoToAssemblyInvoked;

        public TypesTreeControl()
        {
            InitializeComponent();
            DataContext = new TypesTreeModel();
            var model = DataContext as TypesTreeModel;
            model.PropertyChanged += TypesTreeModel_PropertyChanged;
        }

        public void SetFilter(string text)
        {
            typesFilterBox.Text = text;
            filterBox_TextChanged(this.typesFilterBox, null);
        }

        public void UpdateTypesList(List<DumpedTypeModel> dumpedTypes)
        {
            var model = DataContext as TypesTreeModel;

            // In the rare case where we switch between the "All" pseudo assembly
            // and a specific one, this will allow us to re-focus on the currently selected type.
            DumpedTypeModel currentType = model.SelectedType;
            model.Types = new ObservableCollection<DumpedTypeModel>(dumpedTypes);
            if (currentType != null)
            {
                var matchingItem = dumpedTypes.FirstOrDefault(t => t == currentType);
                if (matchingItem != null)
                {
                    model.SelectedType = matchingItem;
                }
            }

            ReapplyFilter();
        }

        public DumpedTypeModel SelectedItem
        {
            get { return (DumpedTypeModel)GetValue(SelectedItemProperty); }
            set { SetValue(SelectedItemProperty, value); }
        }

        public static readonly DependencyProperty SelectedItemProperty =
            DependencyProperty.Register("SelectedItem", typeof(DumpedTypeModel), typeof(TypesTreeControl), new PropertyMetadata(null));

        private void filterBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            bool matchCase = _matchCaseTypes;
            bool useRegex = UseRegex;
            bool onlyTypesInHeap = _onlyTypesInHeap;
            Regex r = null;

            TreeView associatedTreeView = typesTreeView;

            if (useRegex)
            {
                try
                {
                    string tempFilter = (sender as TextBox)?.Text;
                    r = new Regex(tempFilter);
                }
                catch
                {
                    typesFilterBoxBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(255, 153, 164));
                    return;
                }
            }

            // No errors in the types filter, reset border
            typesFilterBoxBorder.BorderBrush = null;

            string filter = (sender as TextBox)?.Text;
            _currentFilter = filter;
            ICollectionView view = CollectionViewSource.GetDefaultView(associatedTreeView.ItemsSource);
            if (view == null) return;
            if (string.IsNullOrWhiteSpace(filter) && !onlyTypesInHeap)
            {
                view.Filter = null;
            }
            else
            {
                // For when we're only filtering with the `_onlyTypesInHeap` flag
                if (filter == null)
                    filter = string.Empty;

                StringComparison comp =
                    matchCase ? StringComparison.CurrentCulture : StringComparison.CurrentCultureIgnoreCase;
                view.Filter = (o) =>
                {
                    if (o is AssemblyModel assembly)
                    {
                        if (assembly.Name.Contains(filter, comp))
                            return true;

                        foreach (var type in assembly.FilteredTypesList)
                        {
                            string input = _dumpedTypeToDescription.Convert(type, null, null, null) as string;
                            if (onlyTypesInHeap && !HeapInstancesRegex().IsMatch(input))
                                continue;
                            if (!useRegex && input?.Contains(filter, comp) == true)
                                return true;
                            if (r.IsMatch(input))
                                return true;
                        }

                        return false;
                    }

                    return false;
                };
            }
        }

        private void clearTypesFilterButton_OnClick(object sender, RoutedEventArgs e)
        {
            typesFilterBox.Clear();
        }

        private void TypesMatchCaseButton_OnClick(object sender, RoutedEventArgs e)
        {
            _matchCaseTypes = !_matchCaseTypes;
            Button b = (sender as Button);
            Brush brush = b.FindResource("ControlSelectedBackground") as Brush;
            b.Background = _matchCaseTypes ? brush : null;
            filterBox_TextChanged(typesFilterBox, null);
        }

        private void TypesRegexButton_OnClick(object sender, RoutedEventArgs e)
        {
            UseRegex = !UseRegex;
            Button b = (sender as Button);
            Brush brush = b.FindResource("ControlSelectedBackground") as Brush;
            b.Background = UseRegex ? brush : null;
            filterBox_TextChanged(typesFilterBox, null);
        }

        private void typesWithInstancesFilterButton_OnClick(object sender, RoutedEventArgs e)
        {
            _onlyTypesInHeap = !_onlyTypesInHeap;
            Button b = (sender as Button);
            Brush brush = b.FindResource("ControlSelectedBackground") as Brush;
            b.Background = _onlyTypesInHeap ? brush : null;
            filterBox_TextChanged(typesFilterBox, null);
        }

        private void typesTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            var model = DataContext as TypesTreeModel;
            model.SelectedType = typesTreeView.SelectedItem as DumpedTypeModel;
            SelectedItem = model.SelectedType;
        }

        [GeneratedRegex("\\(Count: [\\d]", RegexOptions.IgnoreCase, "en-US")]
        private static partial Regex HeapInstancesRegex();

        private void TypeMenuItem_OnClick(object sender, RoutedEventArgs e)
        {
            MenuItem mi = sender as MenuItem;
            string typeName = (mi.DataContext as DumpedTypeModel).FullTypeName;
            Clipboard.SetText(typeName);
        }

        private void GoToAssemblyMenuItem_OnClick(object sender, RoutedEventArgs e)
        {
            MenuItem mi = sender as MenuItem;
            string assembly = (mi.DataContext as DumpedTypeModel).Assembly;
            GoToAssemblyInvoked?.Invoke(assembly);
        }

        private void AssemblyMenuItem_OnClick(object sender, RoutedEventArgs e)
        {
            MenuItem mi = sender as MenuItem;
            string assemblyName = (mi.DataContext as AssemblyModel).Name;
            Clipboard.SetText(assemblyName);
        }

        private void ReapplyFilter()
        {
            filterBox_TextChanged(typesFilterBox, null);
        }

        private void TypesTreeModel_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(TypesTreeModel.Types))
            {
                ReapplyFilter();
            }
        }
    }

    public class TypesTreeModel : INotifyPropertyChanged
    {
        private ObservableCollection<DumpedTypeModel> _types;
        private DumpedTypeModel _selectedType;

        public ObservableCollection<DumpedTypeModel> Types
        {
            get => _types;
            set => SetField(ref _types, value);
        }

        public DumpedTypeModel SelectedType
        {
            get => _selectedType;
            set => SetField(ref _selectedType, value);
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected bool SetField<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }
    }
}
