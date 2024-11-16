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
                    if (sender == assembliesFilterBox)
                    {
                        return (o as AssemblyModel)?.Name?.Contains(filter, comp) == true;
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

        private void CountButton_Click(object sender, RoutedEventArgs e)
        {
            throw new NotImplementedException();
            //countButton.IsEnabled = false;

            //var originalBrush = countLabel.Foreground;
            //Brush transparentColor = originalBrush.Clone();
            //transparentColor.Opacity = 0;
            //countLabel.Foreground = transparentColor;
            //spinner1.Visibility = Visibility.Visible;


            //AssemblyModel assembly = null; // assembliesListBox.SelectedItem as AssemblyModel;
            //if (assembly == null)
            //{
            //    MessageBox.Show("You must select an assembly first.", "Error", MessageBoxButton.OK,
            //        MessageBoxImage.Error);
            //    return;
            //}

            //string assemblyFilter = assembly.Name;
            //if (assembly.Name == "* All")
            //    assemblyFilter = "*"; // Wildcard

            //if (_app is UnmanagedRemoteApp)
            //    assemblyFilter += "!*"; // Indicate we want any type within the module
            //else if (_app is ManagedRemoteApp)
            //    assemblyFilter += ".*"; // Indicate we want any type within the assembly

            //var x = CliWrap.Cli.Wrap("rnet-dump.exe")
            //    .WithArguments($"heap -t {ProcBoxTargetPid} -q {assemblyFilter} {UnmanagedFlagIfNeeded()}")
            //    .WithValidation(CommandResultValidation.None)
            //    .ExecuteBufferedAsync();
            //BufferedCommandResult res = await x.Task;
            //IEnumerable<string> rnetDumpStdOutLines = res.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            //    .SkipWhile(line => !line.Contains("Found "))
            //    .Skip(1)
            //    .Select(str => str.Trim())
            //    .Select(str => str.Split(' ')[1]);

            //List<DumpedTypeModel> types = await GetTypesListAsync();
            //// Like `Distinct` without an IEqualityComparer
            //var uniqueTypes = types.GroupBy(x => x.FullTypeName).Select(grp => grp.First());
            //Dictionary<string, DumpedTypeModel> typeNamesToTypes = uniqueTypes.ToDictionary(dumpedType => dumpedType.FullTypeName);
            //Dictionary<string, int> typesAndInstancesCount = uniqueTypes.ToDictionary(dumpedType => dumpedType.FullTypeName, _ => 0);
            //foreach (string heapObjectType in rnetDumpStdOutLines)
            //{
            //    if (typesAndInstancesCount.ContainsKey(heapObjectType))
            //        typesAndInstancesCount[heapObjectType]++;
            //}


            //List<DumpedTypeModel> dumpedTypes = new List<DumpedTypeModel>();
            //foreach (KeyValuePair<string, int> kvp in typesAndInstancesCount)
            //{
            //    int? numInstances = kvp.Value != 0 ? kvp.Value : null;
            //    DumpedTypeModel dt;
            //    if (_dumpedTypesCache.TryGetValue(kvp.Key, out dt))
            //    {
            //        dt.NumInstances = numInstances;
            //    }
            //    else
            //    {
            //        dt = typeNamesToTypes[kvp.Key];
            //        dt.NumInstances = numInstances;
            //        _dumpedTypesCache[kvp.Key] = dt;
            //    }
            //    dumpedTypes.Add(dt);
            //}

            //TypesControl.UpdateTypesList(dumpedTypes);

            //spinner1.Visibility = Visibility.Collapsed;
            //countLabel.Foreground = originalBrush;
            //countButton.IsEnabled = true;
        }
    }
}
