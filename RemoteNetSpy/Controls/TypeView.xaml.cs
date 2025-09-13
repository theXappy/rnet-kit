using AvalonDock.Controls;
using CliWrap;
using DragDropExpressionBuilder;
using HostingWfInWPF;
using Microsoft.Win32;
using RemoteNET;
using RemoteNetSpy.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace RemoteNetSpy.Controls
{
    /// <summary>
    /// Interaction logic for TypeView.xaml
    /// </summary>
    public partial class TypeView : UserControl
    {
        private DumpedTypeModel _model;
        private RemoteAppModel _remoteAppModel;
        private List<HeapObjectViewModel> _instancesList;

        public TypeView()
        {
            InitializeComponent();
        }

        public void Init(DumpedTypeModel model, RemoteAppModel appModel)
        {
            _remoteAppModel = appModel;
            _model = model;
            this.DataContext = model;

            //
            // (1)
            // Dump members into the "Tracing" tab
            //
            Task loadMembersTask = LoadTypeMembersAsync(model.FullTypeName);

            // 
            // (2)
            // heap Search instances in "Interactive" tab
            //
            FindHeapInstancesButtonClicked(null, null);
        }



        private async Task LoadTypeMembersAsync(string typeFullName)
        {
            List<DumpedMember> dumpedMembers = await Task.Run(
                () =>
                {
                    Type type = _remoteAppModel.App.GetRemoteType(typeFullName);
                    System.Reflection.MemberInfo[] members = type.GetMembers();
                    List<DumpedMember> dumpedMembers = members.Select(mi => new DumpedMember(mi)).ToList();
                    dumpedMembers.Sort(CompareDumperMembers);
                    return dumpedMembers;
                });
            membersListBox.ItemsSource = dumpedMembers;
            filterBox_TextChanged(membersFilterBox, null);
            return;

            int CompareDumperMembers(DumpedMember member1, DumpedMember member2)
            {
                var res = member1.MemberType.CompareTo(member2.MemberType);
                if (res != 0)
                {
                    // Member types mismatched.
                    // Order is mostly alphabetic except Method Tables, which go first.
                    if (member1.MemberType == "MethodTable")
                        return -1;
                    if (member2.MemberType == "MethodTable")
                        return 1;
                    return res;
                }

                // Same member type, sub-sort alphabetically (the member names).
                if (member1.RawName != null && member2.RawName != null)
                    return member1.RawName.CompareTo(member2.RawName);
                return member1.NormalizedName.CompareTo(member2.NormalizedName);
            }
        }

        private void FindHeapInstancesButtonClicked(object sender, RoutedEventArgs e) => FindHeapInstancesAsync();
        private async Task FindHeapInstancesAsync()
        {
            if (_model == null)
            {
                Dispatcher.Invoke(() =>
                {
                    MessageBox.Show("You must select a type from the \"Types\" list first.", $"Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                });
                return;
            }

            Dispatcher.Invoke(() =>
            {
                findHeapInstancesButtonSpinner.Width = findHeapInstancesButtonTextPanel.ActualWidth;
                findHeapInstancesButtonTextPanel.Visibility = Visibility.Collapsed;
            });

            using (findHeapInstancesButtonSpinner.TemporarilyShow())
            {

                string type = (_model)?.FullTypeName;


                var newInstances = await _remoteAppModel.SearchHeap(type);

                // Carry with us all previously frozen objects
                // TODO: This code is silly LOL.
                if (_instancesList != null)
                {
                    List<HeapObjectViewModel> combined = [.. _instancesList.Where(oldObj => oldObj.Frozen)];
                    foreach (var instance in newInstances)
                    {
                        if (!combined.Contains(instance))
                            combined.Add(instance);
                    }

                    newInstances = combined;
                }

                _instancesList = newInstances.ToList();
                _instancesList.Sort();

                await RefreshSearchListsAsync();
            }

            Dispatcher.Invoke(() =>
            {
                findHeapInstancesButtonSpinner.Width = findHeapInstancesButtonTextPanel.ActualWidth;
                findHeapInstancesButtonTextPanel.Visibility = Visibility.Visible;
            });
        }

        private async Task RefreshSearchListsAsync()
        {
            await Dispatcher.InvokeAsync(() =>
            {
                ICollectionView unfrozens = CollectionViewSource.GetDefaultView(_instancesList);
                unfrozens.Filter = (item) => (item as HeapObjectViewModel).Frozen == false;
                heapInstancesListBox.ItemsSource = unfrozens;
            });
        }

        private void clearTypesFilterButton_OnClick(object sender, RoutedEventArgs e)
        {
            if (sender == clearMembersFilterButton)
                membersFilterBox.Clear();
        }

        private void filterBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            bool matchCase = true;
            bool onlyTypesInHeap = false;

            ListBox associatedBox = null;
            if (sender == membersFilterBox)
            {
                associatedBox = membersListBox;
            }

            if (associatedBox == null)
                return;

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
                    if (sender == membersFilterBox)
                    {
                        return (o as DumpedMember)?.NormalizedName?.Contains(filter, comp) == true;
                    }
                    return (o as string)?.Contains(filter) == true;
                };
            }
        }


        private void MemberListItemMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed && e.ClickCount == 2)
            {
                var memberTextBlock = sender as TextBlock;
                TraceMember(memberTextBlock.DataContext as DumpedMember);
            }
        }
        private void TraceMember(DumpedMember sender) => _remoteAppModel.Tracer.AddFunc(sender);

        private void traceMethodButton_Click(object sender, RoutedEventArgs e)
        {
            TraceMember(membersListBox?.SelectedItem as DumpedMember);
        }

        private void TraceTypeFull_OnClick(object sender, RoutedEventArgs e)
        {
            if (_model == null)
            {
                MessageBox.Show("You must select a type from the \"Types\" list first.", $"Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // The type is selected, so all of its members should be dumped on the members list
            foreach (object member in membersListBox.Items)
            {
                TraceMember(member as DumpedMember);
            }
        }
        private void TraceTypeOptimal_OnClick(object sender, RoutedEventArgs e)
        {
            if (_model == null)
            {
                MessageBox.Show("You must select a type from the \"Types\" list first.", $"Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            string[] forbidden =
            {
                "System.Boolean Equals(",
                "bool Equals(",
                "void Finalize(",
                "System.Void Finalize(",
                "object MemberwiseClone(",
                "System.Object MemberwiseClone()",
                "System.Type GetType()",
                "Type GetType()",
                " GetHashCode(",
                " ToString("
            };
            // The type is selected, so all of its members should be dumped on the members list
            foreach (object member in membersListBox.Items)
            {
                DumpedMember dumpedMember = member as DumpedMember;
                if (dumpedMember.MemberType == "Method")
                {
                    bool isForbidden = false;
                    foreach (string forbiddenMember in forbidden)
                    {
                        if (dumpedMember.RawName.Contains(forbiddenMember))
                        {
                            isForbidden = true;
                            break;
                        }
                    }
                    if (isForbidden)
                        continue;
                }

                TraceMember(dumpedMember);
            }
        }


        private void ExportHeapInstancesButtonClicked(object sender, RoutedEventArgs e)
        {
            if (_instancesList == null || _instancesList.Count == 0)
            {
                MessageBox.Show("Nothing to save in Heap Instances windows.", "Error");
                return;
            }

            SaveFileDialog sfd = new SaveFileDialog();
            sfd.Filter = ".csv|.csv";
            if (sfd.ShowDialog() != true)
            {
                // User cancelled
                return;
            }

            _ = Task.Run(() =>
            {
                StringBuilder csv = new StringBuilder();
                csv.AppendLine("Address,Type,Frozen");
                foreach (HeapObjectViewModel heapObject in _instancesList)
                {
                    csv.AppendLine($"{heapObject.Address},{heapObject.FullTypeName},{heapObject.Frozen}");
                }

                Stream file = sfd.OpenFile();
                using (StreamWriter sw = new StreamWriter(file))
                {
                    sw.Write(csv);
                }
            });
        }



        private void membersListBox_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            // We want to allow the user to drag and drop Members into the Drag And Drop Playground.
            // BUT this mechanism interferes with the ListBox scroll bar dragging
            // The code below make sure we're only starting a drag operation if the mouse is over the selected listbox item

            if (e.LeftButton != MouseButtonState.Pressed)
                return;

            var listBox = sender as ListBox;
            if (listBox == null)
                return;

            // Get the element under the mouse
            var point = e.GetPosition(listBox);
            var element = listBox.InputHitTest(point) as DependencyObject;

            // Traverse up the visual tree to find the ListBoxItem
            while (element != null && element is not ListBoxItem)
            {
                element = System.Windows.Media.VisualTreeHelper.GetParent(element);
            }
            if (element is not ListBoxItem listBoxItem)
                return;

            // Only start drag if the item under the mouse is the selected item
            if (listBoxItem.DataContext != listBox.SelectedItem)
                return;

            if (listBox.SelectedItem is not DumpedMember dumpedMember)
                return;
            if (dumpedMember.MemberInfo is not System.Reflection.MethodInfo methodInfo)
                return;

            var miw = new MethodInfoWrapper(methodInfo);
            DragDrop.DoDragDrop(listBox, miw, DragDropEffects.Copy);
        }

        private void ShowTraceTypeContextMenu(object sender, RoutedEventArgs e)
        {
            var extraButton = (sender as Button);
            var contextMenu = extraButton.ContextMenu;
            contextMenu.HorizontalOffset = extraButton.ActualWidth;
            contextMenu.VerticalOffset = extraButton.ActualHeight / 2;

            contextMenu.IsOpen = true;
            e.Handled = true;
        }

        private void MemberMenuItem_OnClick(object sender, RoutedEventArgs e)
        {
            MenuItem mi = sender as MenuItem;
            string member = (mi.DataContext as DumpedMember).NormalizedName;
            Clipboard.SetText(member);
        }

        private void MemberMenuItem_AddToPlayground(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem menuItem)
                return;
            if (menuItem.DataContext is not DumpedMember dumpedMember)
                return;
            if (dumpedMember.MemberInfo is not MethodInfo methodInfo)
            {
                MessageBox.Show("Can only send Methods to playground.");
                return;
            }

            // TODO: Remove
            //dragDropPlayground.AddMethod(methodInfo);
            MethodSentToPlayground(methodInfo);
        }

        private void FrozenObject_AddToPlayground(HeapObjectViewModel heapObj)
        {
            RemoteObject ro = heapObj.RemoteObject;

            Type t = ro.GetRemoteType();
            ushort shortTag = (ushort)heapObj.Address;
            string tag = $"{t.Name}_0x{shortTag:X4}";
            ObjectSentToPlayground(ro, tag, t);
        }

        public event Action<MethodInfo> MethodSentToPlayground;
        public event Action<RemoteObject, string, Type> ObjectSentToPlayground;

        private void FreezeHeapObjectButtonClicked(object sender, RoutedEventArgs e)
        {
            Button senderButton = sender as Button;
            var grid = senderButton.FindLogicalChildren<Grid>().Single();
            var dPanel = grid.Children.OfType<DockPanel>().Single();
            var loadingImage = grid.Children.OfType<Image>().Single();

            // Temp UI changes
            dPanel.Visibility = Visibility.Collapsed;
            loadingImage.Visibility = Visibility.Visible;

            HeapObjectViewModel ho = senderButton.DataContext as HeapObjectViewModel;

            // Heavy operation
            ObjectFreezeRequested(ho);

            // Undor temp UI changes
            Dispatcher.Invoke(() =>
            {
                // Revert the loading image
                dPanel.Visibility = Visibility.Visible;
                loadingImage.Visibility = Visibility.Collapsed;
            });

            if (ho.Frozen)
            {
                _remoteAppModel.Interactor.AddVar(ho);

                Dispatcher.Invoke(() =>
                {
                    FrozenObject_AddToPlayground(ho);
                });
            }
            else
            {
                _remoteAppModel.Interactor.DeleteVar(ho);

            }

            RefreshSearchAndWatchedListsAsync();
        }

        public event Action<HeapObjectViewModel> ObjectFreezeRequested;


        private async Task RefreshSearchAndWatchedListsAsync()
        {
            await Dispatcher.InvokeAsync(() =>
            {
                ICollectionView unfrozens = CollectionViewSource.GetDefaultView(_instancesList);
                unfrozens.Filter = (item) => (item as HeapObjectViewModel).Frozen == false;
                heapInstancesListBox.ItemsSource = unfrozens;
            });
        }

        private void CopyAddressMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var heapObj = (sender as MenuItem).DataContext as HeapObjectViewModel;
            Clipboard.SetText($"0x{heapObj.Address:X16}");
        }

        private void ShowMemoryView_Click(object sender, RoutedEventArgs e)
        {
            var heapObj = (sender as MenuItem)?.DataContext as HeapObjectViewModel;
            _remoteAppModel.ShowMemoryView(Window.GetWindow(this), heapObj?.Address);
        }

        private void InspectButtonBaseOnClick(object sender, RoutedEventArgs e)
        {
            Button senderButton = sender as Button;
            HeapObjectViewModel dataContext = senderButton.DataContext as HeapObjectViewModel;
            if (!dataContext.Frozen || dataContext.RemoteObject == null)
            {
                MessageBox.Show("ERROR: Object must be frozen.");
                return;
            }

            (ObjectViewer.CreateViewerWindow(null, _remoteAppModel, dataContext.RemoteObject)).Show();
        }

        private void PromptForVariableCast(object sender, RoutedEventArgs e)
        {
            var heapObject = (sender as MenuItem).DataContext as HeapObjectViewModel;
            if (heapObject == null)
                return;
            PromptForVariableCastInnerAsync(heapObject);
        }

        private async Task PromptForVariableCastInnerAsync(HeapObjectViewModel heapObject)
        {
            //
            // Prepare new "Type Selection Window" to select the target type
            //

            // Helper dict of dumped types from the LAST heap "objects count" so we can propogate
            // the num of instances into the types list in the sub-window
            ObservableCollection<DumpedTypeModel> mainTypesControlTypes = new ObservableCollection<DumpedTypeModel>(_remoteAppModel.ClassesModel.FilteredAssemblies.SelectMany(a => a.Types));
            Dictionary<string, DumpedTypeModel> mainControlFullTypeNameToTypes = mainTypesControlTypes.ToDictionary(x => x.FullTypeName);
            var typesModel = new TypesModel();
            List<DumpedTypeModel> deepCopiesTypesList = await GetTypesListAsync(true).ContinueWith((task) =>
            {
                return task.Result.Select((DumpedTypeModel newTypeDump) =>
                {
                    if (mainControlFullTypeNameToTypes.TryGetValue(newTypeDump.FullTypeName, out DumpedTypeModel existingTypeDump))
                    {
                        // Return the same objects as in the main TypesControl to preserve number of instances
                        return existingTypeDump;
                    }
                    return newTypeDump;
                }).ToList();
            }, TaskScheduler.Default);
            typesModel.Types = new ObservableCollection<DumpedTypeModel>(deepCopiesTypesList);

            bool? res = false;

            Dispatcher.Invoke(() =>
            {
                var typeSelectionWindow = new TypeSelectionWindow();
                typeSelectionWindow.DataContext = typesModel;

                // Set "hint" in types window: If the current type is a C++ type, suggest other types
                // with the same name in all assemblies.
                // e.g., mylib.dll!MyNameSpace::MyType
                // will suggest a regex that'll also cover:
                // * my_other_lib.dll!MyNameSpace::MyType
                // * mylib.dll!SecondNamespace::MyType
                // Regex breakdown:
                // ::MyType$
                //  ^  ^   ^--------- Match end of line
                //  |  |             
                //  | Curr type name
                //  |
                //  |
                // Separator
                string currFullTypeName = heapObject.FullTypeName;
                if (currFullTypeName.Contains("::"))
                {
                    string currTypeName = currFullTypeName.Split("::").Last();
                    string regex = "::" + currTypeName + @"$";
                    typeSelectionWindow.ApplyRegexFilter(regex);
                }

                res = typeSelectionWindow.ShowDialog();
            });

            if (res != true)
                return;

            DumpedTypeModel selectedType = typesModel.SelectedType;
            if (selectedType == null)
                return;

            try
            {
                Type newType = _remoteAppModel.App.GetRemoteType(selectedType.FullTypeName);
                heapObject.Cast(newType);
                _remoteAppModel.Interactor.CastVar(heapObject, selectedType.FullTypeName);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to cast object: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task<List<DumpedTypeModel>> GetTypesListAsync(bool all)
        {
            IEnumerable<DumpedTypeModel> types = null;
            if (all)
            {
                await Task.Run(() =>
                {
                    types = _remoteAppModel.ClassesModel.Assemblies.SelectMany(a => a.Types);
                });
            }
            else
            {
                throw new ArgumentException();
                //await Task.Run(() =>
                //{
                //    types = _remoteAppModel.Assemblies[assembly].Types;
                //});
            }

            var tempList = types.ToHashSet().ToList();
            tempList.Sort((dt1, dt2) => dt1.FullTypeName.CompareTo(dt2.FullTypeName));
            return tempList;
        }

        private async void heapInstancesListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (heapInstancesListBox.SelectedItem is not HeapObjectViewModel selectedHeapObject)
                return;

            if (!selectedHeapObject.Frozen)
            {
                // Attempt to freeze the object first
                ObjectFreezeRequested?.Invoke(selectedHeapObject);
            }
        }
    }
}
