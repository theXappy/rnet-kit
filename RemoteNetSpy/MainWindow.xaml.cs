using AvalonDock.Controls;
using CliWrap;
using CliWrap.Buffered;
using DragDropExpressionBuilder;
using Microsoft.Win32;
using RemoteNET;
using RemoteNET.Access;
using RemoteNetSpy.Controls;
using RemoteNetSpy.Models;
using RemoteNetSpy.Windows;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using HeapObjectViewModel = RemoteNetSpy.Models.HeapObjectViewModel;

namespace RemoteNetSpy
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private RemoteAppModel _remoteAppModel;
        RemoteApp _app => _remoteAppModel.App;
        public InjectableProcess _targetProcess;
        public int TargetPid => _targetProcess.Pid;
        private DumpedTypeModel _currSelectedType => _remoteAppModel.ClassesModel.SelectedType;
        public string ClassName => _currSelectedType.FullTypeName;

        private System.Timers.Timer _aliveCheckTimer;
        private object _aliveCheckLock;

        public MainWindow()
        {
            _remoteAppModel = new RemoteAppModel();
            DataContext = _remoteAppModel;
            InitializeComponent();
            tracesListBox.ItemsSource = _remoteAppModel.Tracer.TraceList;
            tracesListBox.Items.SortDescriptions.Add(
                new System.ComponentModel.SortDescription("",
                    System.ComponentModel.ListSortDirection.Ascending));

            typeSystemTreeView.DataContext = _remoteAppModel.ClassesModel;
            typeSystemTreeView.TypeDoubleClicked += ClassesModel_PropertyChanged;

            _aliveCheckLock = new object();
            _aliveCheckTimer = new System.Timers.Timer(500);
            _aliveCheckTimer.Elapsed += DoHeartbeat;
        }

        private void MainWindow_OnInitialized(object sender, EventArgs e)
        {
            var targetSelectionWindow = new TargetSelectionWindow();
            if (targetSelectionWindow.ShowDialog() == true)
            {
                _targetProcess = targetSelectionWindow.SelectedProcess;
                selectedTargetTextBlock.Text = $"{_targetProcess.Name} (PID: {_targetProcess.Pid})";
                _ = ConnectToSelectedProcessAsync().ContinueWith(_ =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        // Get focus out of the "ConEmu" sub window
                        Keyboard.Focus(typeSystemTreeView);
                    });
                }, TaskScheduler.Default);
            }
            else
            {
                Close();
            }

            (dragDropPlayground.DataContext as PlaygroundViewModel)?.LoadDemoData();

            // TODO: Dummy for string 
            TabItem x = MyTabControl.Items[MyTabControl.Items.Count - 1] as TabItem;
        }

        private async Task ConnectToSelectedProcessAsync()
        {
            typeSystemTreeView.DisableSearch();

            if (_targetProcess == null)
                return;

            RuntimeType selectedRuntime = ChooseTargetRuntime();
            if (selectedRuntime == RuntimeType.Unknown)
                return;

            // Creating new RemoteApp
            Debug.WriteLine($"[{DateTime.Now.ToLongTimeString()}] >> Creating new RemoteApp");
            using (processConnectionSpinner.TemporarilyShow())
            {
                RemoteApp newApp;
                try
                {
                    var task = Task.Run(() =>
                    {
                        Process proc = Process.GetProcessById(TargetPid);
                        return RemoteAppFactory.Connect(proc, selectedRuntime);
                    });
                    newApp = await task;
                }
                catch (Exception ex)
                {
                    ShowError($"Failed to connect to target {_targetProcess.Name}\nException:\n" + ex);
                    return;
                }
                Debug.WriteLine($"[{DateTime.Now.ToLongTimeString()}] >> >> View Model Update");
                _remoteAppModel.Update(newApp, TargetPid);
                Debug.WriteLine($"[{DateTime.Now.ToLongTimeString()}] >> >> View Model Update done");
            }
            Debug.WriteLine($"[{DateTime.Now.ToLongTimeString()}] >> Creating new RemoteApp done");


            Debug.WriteLine($"[{DateTime.Now.ToLongTimeString()}] >> Initializing Interactive Window (Async)");
            _ = _remoteAppModel.Interactor.InitAsync(this);
            Debug.WriteLine($"[{DateTime.Now.ToLongTimeString()}] >> Initializing Interactive Window (Async), task started");


            Debug.WriteLine($"[{DateTime.Now.ToLongTimeString()}] >> LoadAssembliesAsync");
            await _remoteAppModel.ClassesModel.LoadAssembliesAsync(Dispatcher);
            typeSystemTreeView.EnableSearch();
            Debug.WriteLine($"[{DateTime.Now.ToLongTimeString()}] >> LoadAssembliesAsync done");

            _aliveCheckTimer.Stop();
            _aliveCheckTimer.Start();

            // Disable the Frida tracing button for managed apps
            RunFridaTracesButton.IsEnabled = _remoteAppModel.TargetRuntime == RuntimeType.Unmanaged;
        }

        private RuntimeType ChooseTargetRuntime()
        {
            RuntimeType selectedRuntime;
            bool canConnectToUnmanagedDiver = _targetProcess.DiverState.Contains("[Unmanaged Diver Injected]");
            bool canConnectToManagedDiver = _targetProcess.DiverState.Contains("[Diver Injected]");
            if (canConnectToUnmanagedDiver && canConnectToManagedDiver)
            {
                DiverSelectionDialog dsd = new DiverSelectionDialog();
                if (dsd.ShowDialog() != true)
                {
                    // User cancelled.
                    return RuntimeType.Unknown;
                }

                if (dsd.SelectedRuntime == RuntimeType.Unmanaged)
                {
                    canConnectToUnmanagedDiver = true;
                    canConnectToManagedDiver = false;
                }
                else if (dsd.SelectedRuntime == RuntimeType.Managed)
                {
                    canConnectToUnmanagedDiver = false;
                    canConnectToManagedDiver = true;
                }
                else
                {
                    ShowError($"Unexpected results from Diver selection dialog. Aborting app switch.\nSelected Runtime: {dsd.SelectedRuntime}");
                    return RuntimeType.Unknown;
                }
            }

            bool noDiver = !canConnectToUnmanagedDiver && !canConnectToManagedDiver;
            bool isNativeApp = !_targetProcess.DotNetVersion.StartsWith("net");

            selectedRuntime = RuntimeType.Managed;
            if (noDiver && isNativeApp)
                selectedRuntime = RuntimeType.Unmanaged;
            else if (canConnectToUnmanagedDiver && !canConnectToManagedDiver)
                selectedRuntime = RuntimeType.Unmanaged;
            return selectedRuntime;
        }

        private void DoHeartbeat(object sender, ElapsedEventArgs e)
        {
            if (_app == null)
            {
                _aliveCheckTimer.Stop();
                return;
            }

            if (!Monitor.TryEnter(_aliveCheckLock))
                return;

            try
            {
                bool alive = _app.Communicator.CheckAliveness();
                if (!alive)
                {
                    Dispatcher.Invoke(() =>
                    {
                        _targetProcess.IsProcessDead = true;
                    });
                    _aliveCheckTimer.Stop();
                }
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    MessageBox.Show($"Error checking target app alive state: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                });
                _aliveCheckTimer.Stop();
            }
            finally
            {
                Monitor.Exit(_aliveCheckLock);
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


        private List<HeapObjectViewModel> _watchedInstancesList;
        private async Task RefreshSearchAndWatchedListsAsync()
        {
            await Dispatcher.InvokeAsync(() =>
            {
                // TODO: this but in the current TypeView
                // ICollectionView unfrozens = CollectionViewSource.GetDefaultView(_instancesList);
                // unfrozens.Filter = (item) => (item as HeapObject).Frozen == false;
                // heapInstancesListBox.ItemsSource = unfrozens;

                var instancesListCopy = _watchedInstancesList.ToList();
                ICollectionView frozens = CollectionViewSource.GetDefaultView(instancesListCopy);
                frozens.Filter = (item) => (item as HeapObjectViewModel).Frozen;
                watchedObjectsListBox.ItemsSource = frozens;
            });
        }

        private void filterBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            bool matchCase = true;
            bool onlyTypesInHeap = false;

            ListBox associatedBox = null;
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
                    return (o as string)?.Contains(filter) == true;
                };
            }
        }

        private void TraceLineDelete_OnClick(object sender, RoutedEventArgs e) => _remoteAppModel.Tracer.DeleteFunc(sender);

        // TODO: This whole method should be a command in the RemoteAppModel
        private void FreezeUnfreezeHeapObjectButtonClicked(object sender, RoutedEventArgs e)
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
            Task<bool> freezeUnfreezeTask = FreezeUnfreezeAsync(ho);

            // Undor temp UI changes
            _ = freezeUnfreezeTask.ContinueWith(t =>
            {
                Dispatcher.Invoke(() =>
                {
                    // Revert the loading image
                    dPanel.Visibility = Visibility.Visible;
                    loadingImage.Visibility = Visibility.Collapsed;
                });

                if (!t.Result)
                    return;

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

                _ = RefreshSearchAndWatchedListsAsync();
            }, TaskScheduler.Default);
        }

        private async Task<bool> FreezeUnfreezeAsync(HeapObjectViewModel dataContext)
        {
            bool isFrozen = dataContext.Frozen;
            try
            {
                if (isFrozen)
                {
                    // Unfreezing
                    ulong lastKnownAddres = dataContext.Address;
                    dataContext.RemoteObject = null;
                    dataContext.Address = lastKnownAddres;
                }
                else
                {
                    // Freeze
                    ulong address = dataContext.Address;

                    // Check if the assembly related to the HeapObject is already being monitored
                    string assemblyName = dataContext.FullTypeName.Split('!')[0];
                    AssemblyModel assembly = _remoteAppModel.ClassesModel.Assemblies.FirstOrDefault(a => a.Name == assemblyName);
                    if (assembly != null && !assembly.IsMonitoringAllocation)
                    {
                        // Activate OffensiveGC if it is not already active
                        WatchModuleAllocations(assembly);
                    }

                    Task dumperTask = Task.Run(() =>
                    {
                        RemoteObject ro = (RemoteObject)_app.GetRemoteObject(address, dataContext.FullTypeName);
                        dataContext.Address = ro.RemoteToken;
                        dataContext.RemoteObject = ro;
                    });
                    await dumperTask;
                }
                return true;
            }
            catch (Exception ex)
            {
                string error = "Error while unfreezing.\r\n";
                if (!isFrozen)
                    error = "Error while freezing.\r\nPlease refresh the heap search and retry.\r\n";
                error += "Exception: " + ex;
                MessageBox.Show(error, "Error", MessageBoxButton.OK);
                return false;
            }
        }

        private void ManualTraceClicked(object sender, RoutedEventArgs e)
        {
            if (_targetProcess == null)
            {
                MessageBox.Show("You must attach to a process first", "Error", MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }

            TraceQueryWindow qWin = new TraceQueryWindow();
            bool? res = qWin.ShowDialog();
            if (res == true)
            {

                List<string> args = new List<string>() { "-t", TargetPid.ToString(), _remoteAppModel.UnmanagedFlagIfNeeded() };
                string[] funcsToTrace = qWin.Queries;
                foreach (string funcToTrace in funcsToTrace)
                {
                    args.Add("-i");

                    string reducedSignaturee = funcToTrace;
                    if (funcToTrace.Contains('('))
                        reducedSignaturee = funcToTrace.Substring(0, funcToTrace.IndexOf('('));
                    args.Add($"\"{reducedSignaturee}\"");
                }

                string argsLine = string.Join(' ', args);
                ProcessStartInfo psi = new ProcessStartInfo("rnet-trace.exe", argsLine)
                {
                    UseShellExecute = true
                };
                Process.Start(psi);
            }
        }
        private void AssemblyMenuItem_OnClick(object sender, RoutedEventArgs e)
        {
            MenuItem mi = sender as MenuItem;
            string typeName = (mi.DataContext as AssemblyModel).Name;
            Clipboard.SetText(typeName);
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

            (ObjectViewer.CreateViewerWindow(this, _remoteAppModel, dataContext.RemoteObject)).Show();
        }

        private void WatchModuleAllocations(AssemblyModel module)
        {
            if (!(_app is UnmanagedRemoteApp unmanagedApp))
            {
                MessageBox.Show("This feature is only available for Unmanaged targets.", $"{Title} Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (module == null)
            {
                MessageBox.Show("No module selected.", $"{Title} Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            if (module.IsMonitoringAllocation)
            {
                // This module is already monitored...
                return;
            }
            string assembly = module.Name;
            unmanagedApp.Communicator.StartOffensiveGC(assembly);
            module.IsMonitoringAllocation = true;
        }

        private void LaunchBrowserClick(object sender, RoutedEventArgs e)
        {
            if (_app == null)
            {
                MessageBox.Show("You must attach to a process first", "Error", MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = $"http://127.0.0.1:{_app.Communicator.DiverPort}/help",
                UseShellExecute = true
            });
        }

        private void memoryViewButton_Click(object sender, RoutedEventArgs e)
        {
            _remoteAppModel.ShowMemoryView(this);
        }

        private void CopyAddressMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var heapObj = (sender as MenuItem).DataContext as HeapObjectViewModel;
            Clipboard.SetText($"0x{heapObj.Address:X16}");
        }

        private void ClassesModel_PropertyChanged(DumpedTypeModel selectedType)
        {
            if (selectedType == null)
                return;

            DumpedTypeModel type = _remoteAppModel.ClassesModel.SelectedType;
            CreateNewTypeTab(type);
        }

        public void CreateNewTypeTab(DumpedTypeModel model)
        {
            string fullName = _currSelectedType?.FullTypeName;
            if (fullName == null)
            {
                return;
            }

            TypeView typeView = new TypeView();
            typeView.Init(model, _remoteAppModel);
            typeView.MethodSentToPlayground += dragDropPlayground.AddMethod;
            typeView.ObjectSentToPlayground += dragDropPlayground.AddObject;
            typeView.ObjectFreezeRequested += TypeView_ObjectFreezeRequested;

            DockPanel header = new DockPanel() 
            {
                Margin = new Thickness(10, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            
            // Add close button first and dock it to the right
            Button closeButton = new Button()
            {
                Content = "X",
                Width = 32,
                Height = 34,
                Margin = new Thickness(0),
                Padding = new Thickness(0),
                FontSize = 14,
                Background = System.Windows.Media.Brushes.Transparent,
                BorderBrush = System.Windows.Media.Brushes.Transparent,
                Foreground = System.Windows.Media.Brushes.Gray,
                VerticalAlignment = VerticalAlignment.Center
            };
            DockPanel.SetDock(closeButton, Dock.Right);
            header.Children.Add(closeButton);
            
            // Add icon and text (these will fill the remaining space on the left)
            Image icon = new Image() 
            {
                Height = 16,
                Margin = new Thickness(0, 6, 0, 8),
                Source = new BitmapImage(new Uri("icons/Class.png", UriKind.Relative))
            };
            header.Children.Add(icon);
            
            TextBlock textBlock = new TextBlock() 
            {
                Text = model.FullTypeName,
                Margin = new Thickness(4,6,8,8),
                VerticalAlignment = VerticalAlignment.Center
            };
            header.Children.Add(textBlock);
            
            TabItem tab = new TabItem()
            {
                Header = header
            };
            tab.Content = typeView;
            
            // Set up close button click handler
            closeButton.Click += (sender, e) =>
            {
                MyTabControl.Items.Remove(tab);
                e.Handled = true; // Prevent tab selection when clicking close button
            };
            
            // Add hover effects to close button
            closeButton.MouseEnter += (sender, e) =>
            {
                closeButton.Foreground = System.Windows.Media.Brushes.White;
                closeButton.Background = System.Windows.Media.Brushes.Red;
            };
            
            closeButton.MouseLeave += (sender, e) =>
            {
                closeButton.Foreground = System.Windows.Media.Brushes.Gray;
                closeButton.Background = System.Windows.Media.Brushes.Transparent;
            };

            MyTabControl.Items.Add(tab);
            MyTabControl.SelectedItem = tab; // Switch to the new tab
        }

        public void CreateNewInstanceTab(HeapObjectViewModel heapObject)
        {
            var instanceView = new ObjectViewerControl();
            instanceView.Init(this, _remoteAppModel, heapObject);

            // Create a header with binding to FullTypeName and object.png icon
            var headerPanel = new DockPanel() 
            {
                Margin = new Thickness(10, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            
            // Add close button first and dock it to the right
            Button closeButton = new Button()
            {
                Content = "X",
                Width = 32,
                Height = 34,
                Margin = new Thickness(0),
                Padding = new Thickness(0),
                FontSize = 14,
                Background = System.Windows.Media.Brushes.Transparent,
                BorderBrush = System.Windows.Media.Brushes.Transparent,
                Foreground = System.Windows.Media.Brushes.Gray,
                VerticalAlignment = VerticalAlignment.Center
            };
            DockPanel.SetDock(closeButton, Dock.Right);
            headerPanel.Children.Add(closeButton);

            // Add icon
            Image icon = new Image
            {
                Height = 16,
                Margin = new Thickness(0, 6, 0, 8),
                Source = new BitmapImage(new Uri("icons/object.png", UriKind.Relative))
            };
            headerPanel.Children.Add(icon);

            // Add type name text block
            var typeNameTextBlock = new TextBlock { Margin = new Thickness(4, 6, 0, 8), VerticalAlignment = VerticalAlignment.Center };
            var typeBinding = new Binding("FullTypeName") { Source = heapObject };
            typeNameTextBlock.SetBinding(TextBlock.TextProperty, typeBinding);
            headerPanel.Children.Add(typeNameTextBlock);

            // Add address text block
            var addressTextBlock = new TextBlock { Margin = new Thickness(8, 6, 8, 8), VerticalAlignment = VerticalAlignment.Center };
            addressTextBlock.Text = $"(0x{heapObject.Address:X16})";
            headerPanel.Children.Add(addressTextBlock);

            TabItem tab = new TabItem()
            {
                Header = headerPanel,
                Content = instanceView
            };
            
            // Set up close button click handler
            closeButton.Click += (sender, e) =>
            {
                MyTabControl.Items.Remove(tab);
                e.Handled = true; // Prevent tab selection when clicking close button
            };
            
            // Add hover effects to close button
            closeButton.MouseEnter += (sender, e) =>
            {
                closeButton.Foreground = System.Windows.Media.Brushes.White;
                closeButton.Background = System.Windows.Media.Brushes.Red;
            };
            
            closeButton.MouseLeave += (sender, e) =>
            {
                closeButton.Foreground = System.Windows.Media.Brushes.Gray;
                closeButton.Background = System.Windows.Media.Brushes.Transparent;
            };

            MyTabControl.Items.Add(tab);
            MyTabControl.SelectedItem = tab; // Switch to the new tab
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
                Type newType = _app.GetRemoteType(selectedType.FullTypeName);
                heapObject.Cast(newType);
                _remoteAppModel.Interactor.CastVar(heapObject, selectedType.FullTypeName);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to cast object: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ShowError(string msg)
        {
            Dispatcher.Invoke(() => MessageBox.Show(msg, "Error", MessageBoxButton.OK, MessageBoxImage.Error));
        }

        private void FrozenObject_AddToPlayground(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem menuItem)
                return;
            if (menuItem.DataContext is not HeapObjectViewModel heapObj)
                return;
            FrozenObject_AddToPlayground(heapObj);
        }

        private void FrozenObject_AddToPlayground(HeapObjectViewModel heapObj)
        {
            dragDropPlayground.AddHeapObject(heapObj);
        }

        private void watchedObjectsListBox_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                var listBox = sender as ListBox;
                var item = listBox?.SelectedItem;
                if (item is not HeapObjectViewModel ho)
                    return;
                
                RemoteObject ro = ho.RemoteObject;

                Type t = ro.GetRemoteType();
                ushort shortTag = (ushort)ho.Address;
                string tag = $"{t.Name}_0x{shortTag:X4}";

                var instance = new Instance
                {
                    Type = t,
                    Tag = tag,
                    Obj = ro
                };
                DragDrop.DoDragDrop(listBox, instance, DragDropEffects.Copy);
            }
        }

        private async void TypeView_ObjectFreezeRequested(HeapObjectViewModel ho)
        {
            await FreezeUnfreezeAsync(ho); // Already have this method in MainWindow

            if (ho.Frozen)
            {
                CreateNewInstanceTab(ho);
                dragDropPlayground.AddHeapObject(ho);
            }
            else
            {
                // TODO: Remove tab
                // TODO: Remvoe from playground
            }
        }
    }
}
