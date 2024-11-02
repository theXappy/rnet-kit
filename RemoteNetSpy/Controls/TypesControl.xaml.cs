using System;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.ComponentModel;
using System.Windows.Data;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using RemoteNetSpy.Models;
using RemoteNetSpy.Converters;

namespace RemoteNetSpy.Controls
{
    public partial class TypesControl : UserControl
    {
        public bool UseRegex { get; set; } = false;
        private bool _matchCaseTypes = false;
        private bool _onlyTypesInHeap = false;
        private DumpedTypeToDescription _dumpedTypeToDescription = new DumpedTypeToDescription();
        private string _currentFilter;

        public event Action<string> GoToAssemblyInvoked;

        public TypesControl()
        {
            InitializeComponent();
            DataContext = new TypesModel();
            var model = DataContext as TypesModel;
            model.PropertyChanged += TypesModel_PropertyChanged;
        }

        public void SetFilter(string text)
        {
            typesFilterBox.Text = text;
            filterBox_TextChanged(this.typesFilterBox, null);
        }

        public void UpdateTypesList(List<DumpedTypeModel> dumpedTypes)
        {
            var model = DataContext as TypesModel;

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
            DependencyProperty.Register("SelectedItem", typeof(DumpedTypeModel), typeof(TypesControl), new PropertyMetadata(null));

        private void filterBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            bool matchCase = _matchCaseTypes;
            bool useRegex = UseRegex;
            bool onlyTypesInHeap = _onlyTypesInHeap;
            Regex r = null;

            ListBox associatedBox = typesListBox;

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
            ICollectionView view = CollectionViewSource.GetDefaultView(associatedBox.ItemsSource);
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
                    string input = _dumpedTypeToDescription.Convert(o, null, null, null) as string;
                    if (onlyTypesInHeap && !HeapInstancesRegex().IsMatch(input))
                        return false;
                    if (!useRegex)
                        return input?.Contains(filter, comp) == true;
                    return r.IsMatch(input);
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

        private void typesListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var model = DataContext as TypesModel;
            model.SelectedType = typesListBox.SelectedItem as DumpedTypeModel;
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

        private void ReapplyFilter()
        {
            filterBox_TextChanged(typesFilterBox, null);
        }

        private void TypesModel_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(TypesModel.Types))
            {
                ReapplyFilter();
            }
        }
    }
}
