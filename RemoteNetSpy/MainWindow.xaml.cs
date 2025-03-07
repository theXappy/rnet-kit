using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using AvalonDock.Controls;
using CliWrap;
using CliWrap.Buffered;
using CSharpRepl.Services.Extensions;
using Microsoft.Win32;
using RemoteNET;
using RemoteNetSpy.Models;
using RemoteNetSpy.Windows;
using RnetKit.Common;

namespace RemoteNetSpy
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        RemoteApp _app => _remoteAppModel.App;
        public InjectableProcess _targetProcess;
        public int ProcBoxTargetPid => _targetProcess?.Pid ?? 0;

        private DumpedTypeModel _currSelectedType => _remoteAppModel.ClassesModel.SelectedType;
        public string ClassName => _currSelectedType.FullTypeName;

        private RemoteAppModel _remoteAppModel;

        private System.Timers.Timer _aliveCheckTimer;
        private object _aliveCheckLock;

        public MainWindow()
        {
            _remoteAppModel = new RemoteAppModel();
            DataContext = _remoteAppModel;
            InitializeComponent();
            tracesListBox.ItemsSource = _traceList;
            tracesListBox.Items.SortDescriptions.Add(
                new System.ComponentModel.SortDescription("",
                    System.ComponentModel.ListSortDirection.Ascending));

            typeSystemTreeView.DataContext = _remoteAppModel.ClassesModel;
            _remoteAppModel.ClassesModel.PropertyChanged += ClassesModel_PropertyChanged;

            _aliveCheckLock = new object();
            _aliveCheckTimer = new System.Timers.Timer(500);
            _aliveCheckTimer.Elapsed += OnAliveCheckTimerElapsed;
        }

        private async void MainWindow_OnInitialized(object sender, EventArgs e)
        {
            var targetSelectionWindow = new TargetSelectionWindow();
            if (targetSelectionWindow.ShowDialog() == true)
            {
                _targetProcess = targetSelectionWindow.SelectedProcess;
                selectedTargetTextBlock.Text = $"{_targetProcess.Name} (PID: {_targetProcess.Pid})";
                await ConnectToSelectedProcess();

                // Get focus out of the "ConEmu" sub window
                Keyboard.Focus(typeSystemTreeView);
            }
            else
            {
                Close();
            }
        }

        private async Task ConnectToSelectedProcess()
        {
            if (_targetProcess == null)
                return;

            bool canConnectToUnmanagedDiver = _targetProcess.DiverState.Contains("[Unmanaged Diver Injected]");
            bool canConnectToManagedDiver = _targetProcess.DiverState.Contains("[Diver Injected]");
            if (canConnectToManagedDiver && canConnectToManagedDiver)
            {
                DiverSelectionDialog dsd = new DiverSelectionDialog();
                if (dsd.ShowDialog() != true)
                {
                    // User cancelled.
                    return;
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
                    MessageBox.Show($"Unexpected results from Diver selection dialog. Aborting app switch.\nSelected Runtime: {dsd.SelectedRuntime}");
                    return;
                }
            }

            // Saving aside last RemoteApp
            var oldApp = _app;

            // Creating new RemoteApp
            Dispatcher.Invoke(() => { processConnectionSpinner.Visibility = Visibility.Visible; });
            RemoteApp newApp;
            try
            {
                newApp = await ConnectRemoteApp(canConnectToUnmanagedDiver, canConnectToManagedDiver);
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    MessageBox.Show($"Failed to connect to target {_targetProcess.Name}\nException:\n" + ex, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                });
                Dispatcher.Invoke(() => { processConnectionSpinner.Visibility = Visibility.Collapsed; });
                return;
            }
            _remoteAppModel.Update(newApp, ProcBoxTargetPid);
            Dispatcher.Invoke(() => { processConnectionSpinner.Visibility = Visibility.Collapsed; });

            Debug.WriteLine($"[{DateTime.Now.ToLongTimeString()}] >> Initializing Interactive Window");
            InteractiveWindow_Init();

            // Only now we try to dispose of the old RemoteApp.
            // We must do it after creating a new one for the case where the user re-attaches to the same
            // app. Closing our old one before the new one is connected willl cause the Diver to die.
            if (oldApp != null)
            {
                Debug.WriteLine($"[{DateTime.Now.ToLongTimeString()}] >> Disposing old app");
                try
                {
                    oldApp.Dispose();
                }
                catch
                {
                }
                Debug.WriteLine($"[{DateTime.Now.ToLongTimeString()}] >> Disposed old app");

                oldApp = null;
            }

            Task assembliesListRefresh = RefreshAssembliesViewAsync();

            _aliveCheckTimer.Stop();
            _aliveCheckTimer.Start();

            // Disable the Frida tracing button for managed apps
            RunFridaTracesButton.IsEnabled = _remoteAppModel.TargetRuntime == RuntimeType.Unmanaged;
        }

        private async void OnAliveCheckTimerElapsed(object sender, ElapsedEventArgs e)
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

        private Task<RemoteApp> ConnectRemoteApp(bool canConnectToUnmanagedDiver, bool canConnectToManagedDiver)
        {
            Process proc;
            try
            {
                proc = Process.GetProcessById(ProcBoxTargetPid);
            }
            catch (Exception ex)
            {
                return Task.FromException<RemoteApp>(ex);
            }

            Debug.WriteLine($"[{DateTime.Now.ToLongTimeString()}] Calling  Process.GetProcessById(PID={ProcBoxTargetPid}), returned: {proc}");

            return Task.Run(() =>
            {
                bool noDiver = !canConnectToUnmanagedDiver && !canConnectToManagedDiver;
                bool isNativeApp = !_targetProcess.DotNetVersion.StartsWith("net");
                if ((noDiver && isNativeApp) ||
                    (canConnectToUnmanagedDiver && !canConnectToManagedDiver))
                {
                    return RemoteAppFactory.Connect(proc, RuntimeType.Unmanaged);
                }

                return RemoteAppFactory.Connect(proc, RuntimeType.Managed);
            });
        }

        private async void AssembliesRefreshButton_OnClick(object sender, RoutedEventArgs e) => await RefreshAssembliesViewAsync();

        private async Task RefreshAssembliesViewAsync()
        {
            await Dispatcher.InvokeAsync(() => { assembliesSpinner.Visibility = Visibility.Visible; });

            await _remoteAppModel.ClassesModel.LoadAssembliesAsync();

            await Dispatcher.InvokeAsync(() =>
            {
                assembliesSpinner.Visibility = Visibility.Collapsed;
                //
                // TODO
                //
                //assembliesListBox.ItemsSource = assemblies;
                //filterBox_TextChanged(assembliesFilterBox, null);
            });
        }

        private string UnmanagedFlagIfNeeded()
        {
            if (_remoteAppModel.TargetRuntime == RuntimeType.Unmanaged)
                return "-u";
            return string.Empty;
        }

        private async Task<List<DumpedTypeModel>> GetTypesListAsync()
        {
            throw new Exception();
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

        private int CompareDumperMembers(DumpedMember member1, DumpedMember member2)
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
            return member1.RawName.CompareTo(member2.RawName);
        }


        private async void ExportHeapInstancesButtonClicked(object sender, RoutedEventArgs e)
        {
            if (_instancesList == null || _instancesList.Count == 0)
            {
                MessageBox.Show("Nothing to save in Heap Instances windows.", this.Title);
                return;
            }

            SaveFileDialog sfd = new SaveFileDialog();
            sfd.Filter = ".csv|.csv";
            if (sfd.ShowDialog() != true)
            {
                // User cancelled
                return;
            }

            StringBuilder csv = new StringBuilder();
            await Task.Run(() =>
            {
                csv.AppendLine("Address,Type,Frozen");
                foreach (HeapObject heapObject in _instancesList)
                {
                    csv.AppendLine($"{heapObject.Address},{heapObject.FullTypeName},{heapObject.Frozen}");
                }
            });

            Stream file = sfd.OpenFile();
            using (StreamWriter sw = new StreamWriter(file))
            {
                await sw.WriteAsync(csv);
            }
        }

        private async void FindHeapInstancesButtonClicked(object sender, RoutedEventArgs e)
            => await FindHeapInstances();

        private async Task FindHeapInstances()
        {
            if (_currSelectedType == null)
            {
                Dispatcher.Invoke(() =>
                {
                    MessageBox.Show("You must select a type from the \"Types\" list first.", $"{this.Title} Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                });
                return;
            }

            Dispatcher.Invoke(() =>
            {
                findHeapInstancesButtonSpinner.Width = findHeapInstancesButtonTextPanel.ActualWidth;
                findHeapInstancesButtonSpinner.Visibility = Visibility.Visible;
                findHeapInstancesButtonTextPanel.Visibility = Visibility.Collapsed;
            });

            string type = (_currSelectedType)?.FullTypeName;

            var x = CliWrap.Cli.Wrap("rnet-dump.exe")
                .WithArguments($"heap -t {ProcBoxTargetPid} -q \"{type}\" " + UnmanagedFlagIfNeeded())
                .WithValidation(CommandResultValidation.None)
                .ExecuteBufferedAsync();
            var res = await x.Task;
            var newInstances = res.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .SkipWhile(line => !line.Contains("Found "))
                .Skip(1)
                .Select(str => str.Trim())
                .Select(HeapObject.Parse);

            // Carry with us all previously frozen objects
            if (_instancesList != null)
            {
                List<HeapObject> combined = new List<HeapObject>(_instancesList.Where(oldObj => oldObj.Frozen));
                foreach (var instance in newInstances)
                {
                    if (!combined.Contains(instance))
                        combined.Add(instance);
                }

                newInstances = combined;
            }

            _instancesList = newInstances.ToList();
            _instancesList.Sort();

            RefreshSearchAndWatchedLists();
        }

        private void RefreshSearchAndWatchedLists()
        {
            Dispatcher.Invoke(() =>
            {
                ICollectionView unfrozens = CollectionViewSource.GetDefaultView(_instancesList);
                unfrozens.Filter = (item) => (item as HeapObject).Frozen == false;
                heapInstancesListBox.ItemsSource = unfrozens;

                var instancesListCopy = _instancesList.ToList();
                ICollectionView frozens = CollectionViewSource.GetDefaultView(instancesListCopy);
                frozens.Filter = (item) => (item as HeapObject).Frozen;
                watchedObjectsListBox.ItemsSource = frozens;

                findHeapInstancesButtonSpinner.Visibility = Visibility.Collapsed;
                findHeapInstancesButtonTextPanel.Visibility = Visibility.Visible;
            });
        }

        private List<HeapObject> _instancesList;

        [GeneratedRegex("\\(Count: [\\d]", RegexOptions.IgnoreCase, "en-US")]
        private static partial Regex HeapInstancesRegex();

        private void filterBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            bool matchCase = true;
            bool useRegex = false;
            bool onlyTypesInHeap = false;
            Regex r = null;

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

        private void clearTypesFilterButton_OnClick(object sender, RoutedEventArgs e)
        {
            if (sender == clearMembersFilterButton)
                membersFilterBox.Clear();
        }

        private void RunTracesButtonClicked(object sender, RoutedEventArgs e)
        {
            if (!_traceList.Any())
            {
                MessageBox.Show("List of functions to trace is empty.", "Error", MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }

            if (_targetProcess == null)
            {
                MessageBox.Show("You must attach to a process first", "Error", MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }

            string tempFlistPath = Path.ChangeExtension(Path.GetTempFileName(), "flist");
            SaveTraceFunctionsList(tempFlistPath);

            try
            {
                List<string> args = new List<string>() { "-t", ProcBoxTargetPid.ToString(), UnmanagedFlagIfNeeded() };
                args.Add("-l");
                args.Add($"\"{tempFlistPath}\"");

                string argsLine = string.Join(' ', args);
                ProcessStartInfo psi = new ProcessStartInfo("rnet-trace.exe", argsLine)
                {
                    UseShellExecute = true
                };

                Process.Start(psi);
            }
            catch
            {
                try { File.Delete(tempFlistPath); } catch { }
                ;
            }
        }

        private ObservableCollection<TraceFunction> _traceList = new ObservableCollection<TraceFunction>();

        private void MemberListItemMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed && e.ClickCount == 2)
            {
                var memberTextBlock = sender as TextBlock;
                TraceMember(memberTextBlock.DataContext as DumpedMember);
            }
        }

        private void TraceMember(DumpedMember sender)
        {
            string fullDemangledName;
            string member = sender?.RawName;
            if (member == null)
                return;

            if (sender.MemberType != "Method" && sender.MemberType != "Constructor")
                return;

            string targetClass = ClassName;

            // Removing "[Method]" prefix
            string justSignature = member[(member.IndexOf(']') + 1)..].TrimStart();
            if (justSignature.Contains('('))
            {
                // Managed

                // Splitting return type + name / parameters
                string parametrs = justSignature[(justSignature.IndexOf('('))..];
                string retTypeAndName = justSignature[..(justSignature.IndexOf('('))];

                // Splitting return type and name
                string methodName = retTypeAndName[(retTypeAndName.LastIndexOf(' ') + 1)..];
                string retType = retTypeAndName[..(retTypeAndName.LastIndexOf(' '))];

                // Escaping asteriks in parameters because of pointers ("SomeClass*" - the asterik does not mean a wild card)
                parametrs = parametrs.Replace(" *", "*"); // HACK: "SomeClass *" -> "SomeClass*"
                parametrs = parametrs.Replace("*", "\\*");

                string sigWithoutReturnType = methodName + parametrs;

                fullDemangledName = $"{targetClass}.{sigWithoutReturnType}";
            }
            else
            {
                // Unmanaged
                fullDemangledName = $"{targetClass}.{justSignature}";
            }

            if (!_traceList.Any(tf => tf.DemangledName == fullDemangledName))
            {
                string module = _currSelectedType.Assembly;
                if (!module.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                    module += ".dll";
                string fullMangledName = $"{module}!{justSignature}";
                _traceList.Add(new TraceFunction(fullDemangledName, fullMangledName));
            }

            tabControl.SelectedItem = tracingTabItem;
        }

        private void ClearTraceListButtonClicked(object sender, RoutedEventArgs e)
        {
            _traceList.Clear();
        }

        private void OpenTraceListClicked(object sender, RoutedEventArgs e)
        {
            var ofd = new OpenFileDialog();
            ofd.Filter = "Functions List File (*.flist)|*.flist";
            bool? success = ofd.ShowDialog();
            if (success == true)
            {
                string path = ofd.FileName;
                if (path == null)
                {
                    MessageBox.Show("Invalid file name.");
                    return;
                }

                _traceList.Clear();
                string[] functions = File.ReadAllText(path).Split("\n", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                foreach (var func in functions)
                {
                    _traceList.Add(TraceFunction.FromJson(func));
                }
            }
        }

        private void SaveTraceFunctionsListClicked(object sender, RoutedEventArgs e)
        {
            var sfd = new SaveFileDialog();
            sfd.Filter = "Functions List File (*.flist)|*.flist";
            sfd.OverwritePrompt = true;
            bool? success = sfd.ShowDialog();
            if (success == true)
            {
                string path = sfd.FileName;
                SaveTraceFunctionsList(path);
            }
        }

        private void SaveTraceFunctionsList(string path)
        {
            FileStream f;
            StreamWriter sw;
            if (path == null)
            {
                MessageBox.Show("Invalid file name.");
                return;
            }
            f = File.Open(path, FileMode.Create);
            sw = new StreamWriter(f);
            foreach (TraceFunction traceFunction in _traceList)
            {
                sw.WriteLine(traceFunction.ToJson());
            }

            sw.Flush();
            f.Close();
        }

        private async void FreezeUnfreezeHeapObjectButtonClicked(object sender, RoutedEventArgs e)
        {
            Button senderButton = sender as Button;
            var grid = senderButton.FindLogicalChildren<Grid>().Single();
            var dPanel = grid.Children.OfType<DockPanel>().Single();
            var loadingImage = grid.Children.OfType<Image>().Single();

            dPanel.Visibility = Visibility.Collapsed;
            loadingImage.Visibility = Visibility.Visible;

            HeapObject ho = senderButton.DataContext as HeapObject;
            bool success = false;
            try
            {
                success = await FreezeUnfreeze(ho);
            }
            finally
            {
                dPanel.Visibility = Visibility.Visible;
                loadingImage.Visibility = Visibility.Collapsed;
            }

            if (!success)
                return;

            if (ho.Frozen)
                InterativeWindow_AddVar(ho);
            else
                InterativeWindow_DeleteVar(ho);

            RefreshSearchAndWatchedLists();
        }

        private async Task<bool> FreezeUnfreeze(HeapObject dataContext)
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

        private async void CountButton_Click(object sender, RoutedEventArgs e)
        {

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

                List<string> args = new List<string>() { "-t", ProcBoxTargetPid.ToString(), UnmanagedFlagIfNeeded() };
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

        private void MemberMenuItem_OnClick(object sender, RoutedEventArgs e)
        {
            MenuItem mi = sender as MenuItem;
            string member = (mi.DataContext as DumpedMember).NormalizedName;
            Clipboard.SetText(member);
        }

        private void InspectButtonBaseOnClick(object sender, RoutedEventArgs e)
        {
            Button senderButton = sender as Button;
            HeapObject dataContext = senderButton.DataContext as HeapObject;
            if (!dataContext.Frozen || dataContext.RemoteObject == null)
            {
                MessageBox.Show("ERROR: Object must be frozen.");
                return;
            }

            (ObjectViewer.CreateViewerWindow(this, _remoteAppModel, dataContext.RemoteObject)).Show();
        }

        private void TraceLineDelete_OnClick(object sender, RoutedEventArgs e)
        {
            TraceFunction trace = (sender as FrameworkElement)?.DataContext as TraceFunction;
            if (trace != null)
            {
                _traceList.Remove(trace);
            }
        }

        int _remoteObjectIndex = 0;

        private void ExploreButtonBaseOnClick(object sender, RoutedEventArgs e)
        {
            Button senderButton = sender as Button;
            HeapObject dataContext = senderButton.DataContext as HeapObject;
            InterativeWindow_AddVar(dataContext);
        }

        private Task interactiveWindowInitTask = null;
        private async Task InteractiveWindow_Init()
        {
            // Already completed initialization
            if (interactivePanel.IsStarted)
                return;

            // Initialization is in progress
            if (interactiveWindowInitTask != null)
            {
                await interactiveWindowInitTask;
                return;
            }

            // Initialization is not started yet, starting now
            RuntimeType runtime = RuntimeType.Managed;
            if (_app is UnmanagedRemoteApp)
                runtime = RuntimeType.Unmanaged;

            string RuntimeTypeFullTypeName = typeof(RuntimeType).FullName;

            await interactivePanel.StartAsync("rnet-repl.exe");
            string connectionScript =
@$"var app = RemoteAppFactory.Connect(Process.GetProcessById({ProcBoxTargetPid}), {RuntimeTypeFullTypeName}.{runtime});";
            interactiveWindowInitTask = interactivePanel.WriteInputTextAsync($"{connectionScript}\r\n", clearLast: false);
            await interactiveWindowInitTask;
            return;
        }
        private void InterativeWindow_AddVar(HeapObject dataContext)
        {
            if (!dataContext.Frozen || dataContext.RemoteObject == null)
            {
                MessageBox.Show("ERROR: Object must be frozen.");
                return;
            }

            Task initTask = InteractiveWindow_Init();

            _remoteObjectIndex++;
            string roVarName = $"ro{_remoteObjectIndex}";
            string droVarName = $"dro{_remoteObjectIndex}";
            string objectScript =
$"var {roVarName} = app.GetRemoteObject(0x{dataContext.Address:X16}, \"{dataContext.FullTypeName}\");\r\n" +
$"dynamic {droVarName} = {roVarName}.Dynamify();\r\n";

            initTask.ContinueWith(_ =>
            {
                Dispatcher.Invoke(async () =>
                {
                    await interactivePanel.WriteInputTextAsync(objectScript);
                    dataContext.InteractiveRoVarName = roVarName;
                    dataContext.InteractiveDroVarName = droVarName;
                });
            });
        }

        private void InterativeWindow_DeleteVar(HeapObject dataContext)
        {
            string roVarName = dataContext.InteractiveRoVarName;
            string droVarName = dataContext.InteractiveDroVarName;
            string objectScript =
$"{roVarName} = null;\r\n" +
$"{droVarName} = null;\r\n";
            Dispatcher.Invoke(async () =>
            {
                await interactivePanel.WriteInputTextAsync(objectScript);
                dataContext.InteractiveRoVarName = null;
                dataContext.InteractiveDroVarName = null;
            });
        }

        private void InterativeWindow_CastVar(HeapObject dataContext, string fullTypeName)
        {
            if (!dataContext.Frozen || dataContext.RemoteObject == null)
            {
                MessageBox.Show("ERROR: Object must be frozen.");
                return;
            }

            Task initTask = InteractiveWindow_Init();

            string roVarName = dataContext.InteractiveRoVarName;
            string droVarName = dataContext.InteractiveDroVarName;
            string objectScript =
$"{roVarName} = {roVarName}.Cast(app.GetRemoteType(\"{fullTypeName}\"));\r\n" +
$"{droVarName} = {roVarName}.Dynamify();\r\n";

            initTask.ContinueWith(_ =>
            {
                Dispatcher.Invoke(async () =>
                {
                    await interactivePanel.WriteInputTextAsync(objectScript);
                });
            });
        }

        private object _scalingLock = new object();
        private int _scalingFactor = 0;
        private HashSet<Type> _forbiddens = new HashSet<Type>();

        private void MainWindow_OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            void ChangeAllFontSizes(bool up)
            {
                if (up)
                {
                    _scalingFactor++;
                }
                else
                {
                    // Make sure we're not scaling down below the default size
                    if (_scalingFactor == 0)
                        return;
                    _scalingFactor--;
                }

                var allElements = WindowElementEnumerator.EnumerateAllElementsInWindow(this); // 'this' refers to your Window instance
                var _newValues = new Dictionary<FrameworkElement, double>();
                foreach (FrameworkElement element in allElements)
                {
                    Type t = element.GetType();
                    if (_forbiddens.Contains(t))
                        continue;
                    if (t.GetMembers().All(member => member.Name != "FontSize"))
                    {
                        _forbiddens.Add(t);
                        continue;
                    }

                    if (element.IsPropertyBound("FontSize"))
                        continue;

                    if (element.HasAncestorWithName("titlebar"))
                        continue;

                    _newValues[element] = element.Steal<double>("FontSize") + (up ? 2 : (-2));
                }

                foreach (var kvp in _newValues)
                {
                    FrameworkElement element = kvp.Key;
                    element.SetMember("FontSize", kvp.Value);
                }
            }

            lock (_scalingLock)
            {
                if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
                {
                    bool scaleUp = (e.Key == Key.Add || e.Key == Key.OemPlus);
                    bool scaleDown = (e.Key == Key.Subtract || e.Key == Key.OemMinus);
                    if (scaleUp || scaleDown)
                    {
                        ChangeAllFontSizes(scaleUp);

                        e.Handled = true;
                    }
                }
            }
        }

        private void ModuleWatchMenuItem_OnClick(object sender, RoutedEventArgs e)
        {
            MenuItem mi = sender as MenuItem;
            AssemblyModel module = mi?.DataContext as AssemblyModel;
            WatchModuleAllocations(module);
        }

        private void watchAllocationToolbarButton_Clicked(object sender, RoutedEventArgs e)
        {
            throw new Exception();
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

        private void traceMethodButton_Click(object sender, RoutedEventArgs e)
        {
            TraceMember(membersListBox?.SelectedItem as DumpedMember);
        }

        private void TraceTypeFull_OnClick(object sender, RoutedEventArgs e)
        {
            if (_currSelectedType == null)
            {
                MessageBox.Show("You must select a type from the \"Types\" list first.", $"{this.Title} Error",
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
            if (_currSelectedType == null)
            {
                MessageBox.Show("You must select a type from the \"Types\" list first.", $"{this.Title} Error",
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

        private void ShowTraceTypeContextMenu(object sender, RoutedEventArgs e)
        {
            var extraButton = (sender as Button);
            var contextMenu = extraButton.ContextMenu;
            contextMenu.HorizontalOffset = extraButton.ActualWidth;
            contextMenu.VerticalOffset = extraButton.ActualHeight / 2;

            contextMenu.IsOpen = true;
            e.Handled = true;
        }

        private void memoryViewButton_Click(object sender, RoutedEventArgs e)
        {
            if (_app == null)
            {
                MessageBox.Show("You must attach to a process first", "Error", MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }

            MemoryViewWindow mvw = new MemoryViewWindow(DataContext as RemoteAppModel);
            mvw.Show();
        }

        private void CopyAddressMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var heapObj = (sender as MenuItem).DataContext as HeapObject;
            Clipboard.SetText($"0x{heapObj.Address:X16}");
        }

        private async void ClassesModel_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(ClassesModel.SelectedType))
                return;

            DumpedTypeModel selectedType = (sender as ClassesModel).SelectedType;
            if (selectedType == null)
                return;

            string type = _currSelectedType?.FullTypeName;
            if (type == null)
            {
                membersListBox.ItemsSource = null;
                return;
            }

            //
            // (1)
            // Dump members into the "Tracing" tab
            //
            Task loadMembersTask = LoadTypeMembers(type);

            // 
            // (2)
            // heap Search instances in "Interactive" tab
            //
            FindHeapInstancesButtonClicked(null, null);
        }

        private async Task LoadTypeMembers(string typeFullName)
        {
            Type type = _app.GetRemoteType(typeFullName);
            var members = type.GetMembers();
            List<DumpedMember> dumpedMembers = members.Select(mi => new DumpedMember(mi)).ToList();
            dumpedMembers.Sort(CompareDumperMembers);
            membersListBox.ItemsSource = dumpedMembers;
            filterBox_TextChanged(membersFilterBox, null);
        }

        private async void CastToAnotherTypeMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var heapObject = (sender as MenuItem).DataContext as HeapObject;
            if (heapObject == null)
                return;

            if (!heapObject.Frozen)
            {
                var res = MessageBox.Show("Object must be frozen before casting.\nFreeze now?", "Error", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (res != MessageBoxResult.Yes)
                    return;

                await FreezeUnfreeze(heapObject);
            }

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
                return task.Result.Select(newTypeDump =>
                {
                    if (mainControlFullTypeNameToTypes.TryGetValue(newTypeDump.FullTypeName, out DumpedTypeModel existingTypeDump))
                    {
                        // Return the same objects as in the main TypesControl to preserve number of instances
                        return existingTypeDump;
                    }
                    return newTypeDump;
                }).ToList();
            });
            typesModel.Types = new ObservableCollection<DumpedTypeModel>(deepCopiesTypesList);

            var typeSelectionWindow = new TypeSelectionWindow();
            typeSelectionWindow.DataContext = typesModel;

            // Set "hint" in types window: If the current type is a C++ type, suggest other types
            // with the same name in all assemblies.
            // e.g., mylib.dll!MyNameSpace::MyType
            // will suggest a regex that'll also cover:
            // * my_other_lib.dll!MyNameSpace::MyType
            // * mylib.dll!SecondNamespace::MyType
            // Regex breakdown:
            // ::MyType(?:\s\([^)]+\))?$
            //  ^  ^    ^^^^^^^^^^^^^^^^^
            //  |  |             |
            //  | Curr type name |
            //  |                |
            //  |    Match end of line OR "(Count: X...)" suffix  
            // Separator
            string currFullTypeName = heapObject.FullTypeName;
            if (currFullTypeName.Contains("::"))
            {
                string currTypeName = currFullTypeName.Split("::").Last();
                string regex = "::" + currTypeName + @"(?:\s\([^)]+\))?$";
                typeSelectionWindow.ApplyRegexFilter(regex);
            }

            if (typeSelectionWindow.ShowDialog() != true)
                return;

            DumpedTypeModel selectedType = typesModel.SelectedType;
            if (selectedType == null)
                return;

            try
            {
                Type newType = _app.GetRemoteType(selectedType.FullTypeName);
                var newRemoteObject = heapObject.RemoteObject.Cast(newType);
                heapObject.RemoteObject = newRemoteObject;
                heapObject.FullTypeName = selectedType.FullTypeName;
                InterativeWindow_CastVar(heapObject, selectedType.FullTypeName);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to cast object: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

#pragma warning disable IDE0051 // Remove unused private members
        private void TypesControl_GoToAssemblyInvoked(string assembly)
        {
            throw new Exception();
        }
#pragma warning restore IDE0051 // Remove unused private members

        private void RunFridaTracesButtonClicked(object sender, RoutedEventArgs e)
        {
            if (!_traceList.Any())
            {
                MessageBox.Show("List of functions to trace is empty.", "Error", MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }

            if (_targetProcess == null)
            {
                MessageBox.Show("You must attach to a process first", "Error", MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }

            try
            {
                List<string> args = new List<string>() { "-p", ProcBoxTargetPid.ToString() };

                foreach (var traceFunction in _traceList)
                {
                    args.Add("-i");
                    args.Add($"\"{traceFunction.FullMangledName}\"");
                }

                string argsLine = string.Join(' ', args);
                ProcessStartInfo psi = new ProcessStartInfo("frida-trace", argsLine)
                {
                    UseShellExecute = true
                };

                Process.Start(psi);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to start Frida trace: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

    }

    public static class VisualTreeHelperExtensions
    {
        public static IEnumerable<DependencyObject> EnumerateAllVisualChildren(this DependencyObject parent)
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                yield return child;

                foreach (var visualChild in EnumerateAllVisualChildren(child))
                {
                    yield return visualChild;
                }
            }
        }
    }

    public static class WindowElementEnumerator
    {
        public static IEnumerable<FrameworkElement> EnumerateAllElementsInWindow(Window window)
        {
            foreach (var child in window.EnumerateAllVisualChildren())
            {
                if (child is FrameworkElement frameworkElement)
                {
                    yield return frameworkElement;
                }
            }
        }
    }
    public static class FrameworkElementExtensions
    {
        public static bool HasAncestorWithName(this FrameworkElement element, string ancestorName)
        {
            FrameworkElement currentElement = element;

            while (currentElement != null)
            {
                if (currentElement.Name == ancestorName)
                {
                    return true;
                }

                currentElement = VisualTreeHelper.GetParent(currentElement) as FrameworkElement;
            }

            return false;
        }

        // Assuming 'element' is your WPF framework element, and 'propertyName' is the name of the property you want to check for binding.
        public static bool IsPropertyBound(this FrameworkElement element, string propertyName)
        {
            // Try to get the binding for the specified property.
            BindingBase binding = BindingOperations.GetBinding(element, DependencyPropertyDescriptor.FromName(propertyName, element.GetType(), element.GetType()).DependencyProperty);

            // If the binding is not null, it means the property is data-bound.
            return binding != null;
        }
    }
}
