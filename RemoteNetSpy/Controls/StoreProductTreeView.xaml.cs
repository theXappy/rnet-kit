using CliWrap.Buffered;
using CliWrap;
using HostingWfInWPF;
using Microsoft.Win32;
using RemoteNET;
using RemoteNetSpy.Models;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Input;
using System.Linq;

namespace RemoteNetSpy.Controls
{
    /// <summary>
    /// Interaction logic for StoreProductTreeView.xaml
    /// </summary>
    public partial class StoreProductTreeView : UserControl
    {
        ClassesModel Model => DataContext as ClassesModel;

        public StoreProductTreeView()
        {
            InitializeComponent();
        }

        private async void AssembliesRefreshButton_OnClick(object sender, RoutedEventArgs e) => throw new NotImplementedException();

        private void injectDllButton_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog ofd = new OpenFileDialog();
            ofd.Multiselect = false;
            if (ofd.ShowDialog() != true)
                return;

            string file = ofd.FileName;
        }

        private void filterBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            bool matchCase = true;
            bool useRegex = false;
            bool onlyTypesInHeap = false;
            Regex r = null;

            ListBox associatedBox = null;

            string filter = (sender as TextBox)?.Text;
            ICollectionView view = CollectionViewSource.GetDefaultView(assembliesTreeView.ItemsSource);
            if (view == null) return;
            if (string.IsNullOrWhiteSpace(filter) && !onlyTypesInHeap)
            {
                view.Filter = null;
                foreach (AssemblyModel assembly in Model.Assemblies)
                {
                    assembly.Filter = null;
                }
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
                    if (sender == assembliesFilterBox)
                    {
                        var assembly = o as AssemblyModel;
                        assembly.Filter = (typeModel) => typeModel?.FullTypeName?.Contains(filter) == true;

                        if (assembly?.Name?.Contains(filter, comp) == true)
                            return true;
                        if (assembly.Types.Any(t => t.FullTypeName.Contains(filter, comp)))
                            return true;
                        return false;
                    }

                    return (o as string)?.Contains(filter) == true;
                };
            }
        }
        private void clearTypesFilterButton_OnClick(object sender, RoutedEventArgs e)
        {
            if (sender == clearAssembliesFilterButton)
                assembliesFilterBox.Clear();
        }

        private void TreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            switch(e.NewValue)
            {
                case AssemblyModel assembly:
                    break;
                case DumpedTypeModel type:
                    Model.SelectedType = type;
                    break;
            }
        }

        private void TreeViewItem_RequestBringIntoView(object sender, RequestBringIntoViewEventArgs e) => e.Handled = true;

        private void TraceTypeOptimal_OnClick(object sender, RoutedEventArgs e)
        {
            throw new NotImplementedException();

        }

        private void TraceTypeFull_OnClick(object sender, RoutedEventArgs e)
        {
            throw new NotImplementedException();
        }

        private void ShowTraceTypeContextMenu(object sender, RoutedEventArgs e)
        {
            throw new NotImplementedException();
        }

        private async void CountButton_Click(object sender, RoutedEventArgs e)
        {
            countButton.IsEnabled = false;

            var originalBrush = countLabel.Foreground;
            Brush transparentColor = originalBrush.Clone();
            transparentColor.Opacity = 0;
            countLabel.Foreground = transparentColor;
            spinner1.Visibility = Visibility.Visible;

            await Model.CountInstancesAsync();

            //TypesControl.UpdateTypesList(dumpedTypes);

            spinner1.Visibility = Visibility.Collapsed;
            countLabel.Foreground = originalBrush;
            countButton.IsEnabled = true;
        }
    }
}
