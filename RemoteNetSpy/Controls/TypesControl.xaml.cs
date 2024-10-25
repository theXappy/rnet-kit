using System;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.ComponentModel;
using System.Windows.Data;

namespace RemoteNetSpy.Controls
{
    public partial class TypesControl : UserControl
    {
        private bool _matchCaseTypes = false;
        private bool _regexTypes = false;
        private bool _onlyTypesInHeap = false;
        private DumpedTypeToDescription _dumpedTypeToDescription = new DumpedTypeToDescription();

        public event Action<string> GoToAssemblyInvoked;

        public TypesControl()
        {
            InitializeComponent();
            DataContext = new TypesModel();
        }

        private void filterBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            bool matchCase = _matchCaseTypes;
            bool useRegex = _regexTypes;
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
            _regexTypes = !_regexTypes;
            Button b = (sender as Button);
            Brush brush = b.FindResource("ControlSelectedBackground") as Brush;
            b.Background = _regexTypes ? brush : null;
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
    }
}
