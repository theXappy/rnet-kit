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
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using AvalonDock.Controls;
using CliWrap;
using CliWrap.Buffered;
using CSharpRepl.Services.Extensions;
using Microsoft.CodeAnalysis.Scripting;
using Microsoft.Win32;
using RemoteNET;
using RemoteNetSpy.Converters;
using RemoteNetSpy.Models;

namespace RemoteNetSpy
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        RemoteApp _app = null;
        private DumpedTypeToDescription _dumpedTypeToDescription = new DumpedTypeToDescription();
        private Dictionary<string, DumpedTypeModel> _dumpedTypesCache = new Dictionary<string, DumpedTypeModel>();
        private TypesModel _typesModel;
        public InjectableProcess _procBoxCurrItem;
        public int TargetPid => _procBoxCurrItem?.Pid ?? 0;

        private DumpedTypeModel _currSelectedType => _typesModel.SelectedType;
        public string ClassName => _currSelectedType.FullTypeName;

        private AssemblyModel _allAssembliescModel;
        private Dictionary<AssemblyModel, List<string>>
            _assembliesToTypes = new Dictionary<AssemblyModel, List<string>>();

        private RemoteAppModel _remoteAppModel;

        public MainWindow()
        {
            _remoteAppModel = new RemoteAppModel();
            DataContext = _remoteAppModel;
            InitializeComponent();
            tracesListBox.ItemsSource = _traceList;
            tracesListBox.Items.SortDescriptions.Add(
                new System.ComponentModel.SortDescription("",
                    System.ComponentModel.ListSortDirection.Ascending));

            // Set the data context of the TypesControl to an instance of the TypesModel class
            _typesModel = TypesControl.DataContext as TypesModel;
            // Subscribe to the PropertyChanged event of the TypesModel
            _typesModel.PropertyChanged += TypesModel_PropertyChanged;
        }

        private async void MainWindow_OnInitialized(object sender, EventArgs e) => await RefreshProcessesList();

        private async Task RefreshProcessesList()
        {
            procsBox.ItemsSource = null;
            StopGlow();

            procsBox.IsEnabled = false;
            procsBoxLoadingOverlay.Visibility = Visibility.Visible;

            var x = CliWrap.Cli.Wrap("rnet-ps.exe").ExecuteBufferedAsync();
            var res = await x.Task;
            var xx = res.StandardOutput.Split('\n')
                .Skip(1)
                .ToList()
                .Select(line => line.Split('\t', StringSplitOptions.TrimEntries).ToArray())
                .Where(arr => arr.Length > 2)
                .Select(arr => new InjectableProcess(int.Parse(arr[0]), arr[1], arr[2], arr[3]))
                .ToList();
            procsBox.ItemsSource = xx;
            procsBox.IsEnabled = true;
            procsBoxLoadingOverlay.Visibility = Visibility.Collapsed;

            t = new Timer(UpdateGlowEffect, null, TimeSpan.Zero, TimeSpan.FromMilliseconds(100));
            _glowActive = true;
        }


        private Timer t;
        private bool _glowActive = false;
        private int step = 0;

        private void UpdateGlowEffect(object state)
        {
            if (!_glowActive)
                return;

            double opacity = 0;
            int MODIFIER = 20;
            double OPACITY_STEP = 1d / (MODIFIER / 2);
            step = (step + 1) % MODIFIER;
            if (step <= MODIFIER / 2)
            {
                opacity = OPACITY_STEP * step;
            }
            else
            {
                opacity = 1 - (OPACITY_STEP * (step - (MODIFIER / 2)));
            }

            Dispatcher.BeginInvoke(() =>
            {
                procsBoxBorder.Effect = new DropShadowEffect()
                { BlurRadius = 10, Color = Colors.Yellow, Opacity = opacity, ShadowDepth = 0 };
            });
        }



        private void StopGlow()
        {
            _glowActive = false;
            t?.Dispose();
            t = null;
            procsBoxBorder.Effect = null;
        }

        private async void procsBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            Debug.WriteLine($"[{DateTime.Now.ToLongTimeString()}] >> procsBox_SelectionChanged");
            if (procsBox.SelectedIndex == -1)
                return;

            Debug.WriteLine($"[{DateTime.Now.ToLongTimeString()}] Checking injectability");
            _procBoxCurrItem = procsBox.SelectedItem as InjectableProcess;
            bool canConnectToUnmanagedDiver = _procBoxCurrItem.DiverState.Contains("[Unmanaged Diver Injected]");
            bool canConnectToManagedDiver = _procBoxCurrItem.DiverState.Contains("[Diver Injected]");
            if (canConnectToManagedDiver && canConnectToManagedDiver)
            {
                Debug.WriteLine($"[{DateTime.Now.ToLongTimeString()}] Can inject to both. Prompting user to choose.");
                // Both divers present, let user choose which to connect to.
                DiverSelectionDialog dsd = new DiverSelectionDialog();
                dsd.Owner = this;
                if (dsd.ShowDialog() != true)
                {
                    // User cancelled.
                    procsBox.SelectedIndex = -1;
                    return;
                }

                Debug.WriteLine($"[{DateTime.Now.ToLongTimeString()}] User chose: {dsd.SelectedRuntime}");
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


            Debug.WriteLine($"[{DateTime.Now.ToLongTimeString()}] Stopping Glow");
            StopGlow();
            Debug.WriteLine($"[{DateTime.Now.ToLongTimeString()}] Stopped Glow");

            // Saving aside last RemoteApp
            var oldApp = _app;

            // Creating new RemoteApp
            Dispatcher.Invoke(() => { processConnectionSpinner.Visibility = Visibility.Visible; });
            _app = await ConnectRemoteApp(canConnectToUnmanagedDiver, canConnectToManagedDiver);
            Dispatcher.Invoke(() => { processConnectionSpinner.Visibility = Visibility.Collapsed; });

            Debug.WriteLine($"[{DateTime.Now.ToLongTimeString()}] >> Initializing Interactive Window");
            Task interactiveWindowInitTask = InteractiveWindow_Init();

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

            Debug.WriteLine($"[{DateTime.Now.ToLongTimeString()}] >> RefreshAssembliesAsync");
            Task assembliesListRefresh = RefreshAssembliesAsync();
        }

        private Task<RemoteApp> ConnectRemoteApp(bool canConnectToUnmanagedDiver, bool canConnectToManagedDiver)
        {
            Debug.WriteLine($"[{DateTime.Now.ToLongTimeString()}] Calling  Process.GetProcessById(PID={TargetPid})");
            Process proc = Process.GetProcessById(TargetPid);
            Debug.WriteLine($"[{DateTime.Now.ToLongTimeString()}] Calling  Process.GetProcessById(PID={TargetPid}), returned: {proc}");
            try
            {
                return Task.Run(() =>
                {
                    bool noDiver = !canConnectToUnmanagedDiver && !canConnectToManagedDiver;
                    bool isNativeApp = !_procBoxCurrItem.DotNetVersion.StartsWith("net");
                    if ((noDiver && isNativeApp) ||
                        (canConnectToUnmanagedDiver && !canConnectToManagedDiver))
                    {
                        return RemoteAppFactory.Connect(proc, RuntimeType.Unmanaged);
                    }

                    return RemoteAppFactory.Connect(proc, RuntimeType.Managed);
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to connect to target '{_procBoxCurrItem.Name}'.\n\n" + ex);
                return Task.FromException<RemoteApp>(ex);
            }
        }

        private async void AssembliesRefreshButton_OnClick(object sender, RoutedEventArgs e) => await RefreshAssembliesAsync();

        private Regex r = new Regex(@"\[(.*?)\]\[(.*?)\](.*)");

        private Task RefreshAssembliesAsync()
        {
            _assembliesToTypes.Clear();

            return Task.Run(() =>
            {

                Dispatcher.Invoke(() => { assembliesSpinner.Visibility = Visibility.Visible; });

                var x = CliWrap.Cli.Wrap("rnet-dump.exe")
                    .WithArguments($"types -t {TargetPid} -q * " + UnmanagedFlagIfNeeded())
                    .WithValidation(CommandResultValidation.None)
                    .ExecuteBufferedAsync();
                var res = x.Task.Result;
                var xx = res.StandardOutput.Split('\n')
                    .Skip(2)
                    .Select(str => str.Trim());
                foreach (string line in xx)
                {
                    var match = r.Match(line);
                    string[] parts = match.Groups.Values.Select(g => g.Value).Skip(1).ToArray();
                    if (parts.Length < 3)
                        continue;
                    string runtime = parts[0].Trim();
                    string assemblyName = parts[1].Trim();
                    var assembly = new AssemblyModel(assemblyName, runtime, anyTypes: true);
                    string type = parts[2].Trim();
                    if (!_assembliesToTypes.TryGetValue(assembly, out List<string> types))
                    {
                        types = new List<string>();
                        _assembliesToTypes[assembly] = types;
                    }
                    else if (!types.Any())
                    {
                        // This assembly was previously added as a types-less assembly. Now we found a type inside so we need to indicate that.
                        // Note the `assembly` we have in hand might not be the one used as the KEY in the dictionary.
                        AssemblyModel assemblyKey = _assembliesToTypes.Keys.Single(key => key.Name == assembly.Name);
                        assemblyKey.AnyTypes = true;
                    }

                    types.Add(type);
                }

                // Also look for types-less assemblies
                var commandTask = CliWrap.Cli.Wrap("rnet-dump.exe")
                    .WithArguments($"domains -t {TargetPid} " + UnmanagedFlagIfNeeded())
                    .WithValidation(CommandResultValidation.None)
                    .ExecuteBufferedAsync();
                var domainsRes = commandTask.Task.Result;
                var domainsStdout = domainsRes.StandardOutput.Split('\n')
                    .Skip(2)
                    .Select(str => str.Trim());
                foreach (string line in domainsStdout)
                {
                    if (line.StartsWith("[module] "))
                    {
                        string moduleName = line.Substring("[module] ".Length);
                        var assemblyModel = new AssemblyModel(moduleName, TargetRuntime, anyTypes: false);
                        if (!_assembliesToTypes.ContainsKey(assemblyModel))
                        {
                            _assembliesToTypes[assemblyModel] = new List<string>();
                        }
                    }
                }

                List<AssemblyModel> assemblies = _assembliesToTypes.Keys.ToList();
                _allAssembliescModel = new AssemblyModel("* All", RuntimeType.Unknown, anyTypes: true);
                assemblies.Add(_allAssembliescModel);
                assemblies.Sort((desc, assemblyModel) => desc.Name.CompareTo(assemblyModel.Name));

                Dispatcher.Invoke(() =>
                {
                    assembliesListBox.ItemsSource = assemblies;
                    assembliesSpinner.Visibility = Visibility.Collapsed;
                });
            })
                .ContinueWith(_ =>
                {
                    Dispatcher.Invoke(() => filterBox_TextChanged(assembliesFilterBox, null));
                });
        }

        private RuntimeType TargetRuntime => _app is UnmanagedRemoteApp ? RuntimeType.Unmanaged : RuntimeType.Managed;

        private string UnmanagedFlagIfNeeded()
        {
            if (TargetRuntime == RuntimeType.Unmanaged)
                return "-u";
            return string.Empty;
        }

        private async void assembliesListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            membersListBox.ItemsSource = null;

            List<DumpedTypeModel> dumpedTypes = await GetTypesListAsync();
            TypesControl.UpdateTypesList(dumpedTypes);
        }

        private async Task<List<DumpedTypeModel>> GetTypesListAsync()
        {
            AssemblyModel assembly = assembliesListBox.SelectedItem as AssemblyModel;
            return await GetTypesListAsync(assembly);
        }

        private async Task<List<DumpedTypeModel>> GetTypesListAsync(AssemblyModel assembly)
        {
            IEnumerable<DumpedTypeModel> types = null;
            if (assembly == null)
                return new List<DumpedTypeModel>();
            if (assembly.Name == "* All")
            {
                await Task.Run(() =>
                {
                    types = _assembliesToTypes.SelectMany(kvp => kvp.Value.Select(type => new DumpedTypeModel(kvp.Key.Name, type, null)));
                });
            }
            else
            {
                await Task.Run(() =>
                {
                    types = _assembliesToTypes[assembly].Select(str => new DumpedTypeModel(assembly.Name, str, null));
                });
            }

            var tempList = types.ToHashSet().ToList();
            tempList.Sort((dt1, dt2) => dt1.FullTypeName.CompareTo(dt2.FullTypeName));
            return tempList;
        }

        //private async void typesListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        //{
        //    _currSelectedType = (_currSelectedType);
        //    string type = _currSelectedType?.FullTypeName;
        //    if (type == null)
        //    {
        //        membersListBox.ItemsSource = null;
        //        return;
        //    }


        //    var x = CliWrap.Cli.Wrap("rnet-dump.exe")
        //        .WithArguments($"members -t {TargetPid} -q \"{type}\" -n true " + UnmanagedFlagIfNeeded())
        //        .WithValidation(CommandResultValidation.None)
        //        .ExecuteBufferedAsync();
        //    var res = await x.Task;
        //    var xx = res.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries)
        //        .SkipWhile(line => !line.Contains("Members of "))
        //        .Skip(1)
        //        .Select(str => str.Trim());

        //    List<string> rawLinesList = xx.ToList();
        //    List<DumpedMember> dumpedMembers = new List<DumpedMember>();
        //    for (int i = 0; i < rawLinesList.Count; i += 2)
        //    {
        //        DumpedMember dumpedMember = new DumpedMember()
        //        {
        //            RawName = rawLinesList[i],
        //            NormalizedName = rawLinesList[i + 1]
        //        };
        //        dumpedMembers.Add(dumpedMember);
        //    }

        //    dumpedMembers.Sort(CompareDumperMembers);

        //    membersListBox.ItemsSource = dumpedMembers;

        //    filterBox_TextChanged(membersFilterBox, null);
        //}

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
        {
            if (_currSelectedType == null)
            {
                MessageBox.Show("You must select a type from the \"Types\" list first.", $"{this.Title} Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            findHeapInstancesButtonSpinner.Width = findHeapInstancesButtonTextPanel.ActualWidth;
            findHeapInstancesButtonSpinner.Visibility = Visibility.Visible;
            findHeapInstancesButtonTextPanel.Visibility = Visibility.Collapsed;

            string type = (_currSelectedType)?.FullTypeName;

            var x = CliWrap.Cli.Wrap("rnet-dump.exe")
                .WithArguments($"heap -t {TargetPid} -q \"{type}\" " + UnmanagedFlagIfNeeded())
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
            ICollectionView unfrozens = CollectionViewSource.GetDefaultView(_instancesList);
            unfrozens.Filter = (item) => (item as HeapObject).Frozen == false;
            heapInstancesListBox.ItemsSource = unfrozens;

            var instancesListCopy = _instancesList.ToList();
            ICollectionView frozens = CollectionViewSource.GetDefaultView(instancesListCopy);
            frozens.Filter = (item) => (item as HeapObject).Frozen;
            watchedObjectsListBox.ItemsSource = frozens;

            findHeapInstancesButtonSpinner.Visibility = Visibility.Collapsed;
            findHeapInstancesButtonTextPanel.Visibility = Visibility.Visible;
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
            //if (sender == typesFilterBox)
            //{
            //    associatedBox = typesListBox;
            //    matchCase = _matchCaseTypes;
            //    useRegex = _regexTypes;
            //    onlyTypesInHeap = _onlyTypesInHeap;

            //    if (useRegex)
            //    {
            //        try
            //        {
            //            string tempFilter = (sender as TextBox)?.Text;
            //            r = new Regex(tempFilter);
            //        }
            //        catch
            //        {
            //            typesFilterBoxBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(255, 153, 164));
            //            return;
            //        }
            //    }

            //    // No errors in the types filter, reset border
            //    typesFilterBoxBorder.BorderBrush = null;
            //}

            if (sender == assembliesFilterBox)
            {
                associatedBox = assembliesListBox;
            }

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

                    //if (sender == typesFilterBox)
                    //{
                    //    string input = _dumpedTypeToDescription.Convert(o, null, null, null) as string;
                    //    if (onlyTypesInHeap && !HeapInstancesRegex().IsMatch(input))
                    //        return false;
                    //    if (!useRegex)
                    //        return input?.Contains(filter, comp) == true;
                    //    return r.IsMatch(input);
                    //}

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
            if (sender == clearMembersFilterButton)
                membersFilterBox.Clear();
        }

        private async void ProcsRefreshButton_OnClick(object sender, RoutedEventArgs e)
        {
            membersListBox.ItemsSource = null;
            _typesModel.SelectedType = null;
            _typesModel.Types = null;
            assembliesListBox.ItemsSource = null;
            heapInstancesListBox.ItemsSource = null;
            _traceList.Clear();

            await RefreshProcessesList();
        }

        private void RunTracesButtonClicked(object sender, RoutedEventArgs e)
        {
            if (!_traceList.Any())
            {
                MessageBox.Show("List of functions to trace is empty.", "Error", MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }

            if (_procBoxCurrItem == null)
            {
                MessageBox.Show("You must attach to a process first", "Error", MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }

            string tempFlistPath = Path.ChangeExtension(Path.GetTempFileName(), "flist");
            SaveTraceFunctionsList(tempFlistPath);

            try
            {
                List<string> args = new List<string>() { "-t", TargetPid.ToString(), UnmanagedFlagIfNeeded() };
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
                try { File.Delete(tempFlistPath); } catch { };
            }
        }

        private ObservableCollection<string> _traceList = new ObservableCollection<string>();

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
            string newItem;
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

                newItem = $"{targetClass}.{sigWithoutReturnType}";
            }
            else
            {
                // Unmanaged
                newItem = $"{targetClass}.{justSignature}";
            }

            if (!_traceList.Contains(newItem))
                _traceList.Add(newItem);

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
                    _traceList.Add(func);
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
            f = File.Open(path, FileMode.OpenOrCreate);
            sw = new StreamWriter(f);
            foreach (string traceFunction in _traceList)
            {
                sw.WriteLine(traceFunction);
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
            try
            {
                await FreezeUnfreeze(ho);
            }
            finally
            {
                dPanel.Visibility = Visibility.Visible;
                loadingImage.Visibility = Visibility.Collapsed;
            }

            if (ho.Frozen)
                InterativeWindow_AddVar(ho);
            else
                InterativeWindow_DeleteVar(ho);

            RefreshSearchAndWatchedLists();
        }

        private async Task FreezeUnfreeze(HeapObject dataContext)
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
                    AssemblyModel assembly = _assembliesToTypes.Keys.FirstOrDefault(a => a.Name == assemblyName);
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
            }
            catch (Exception ex)
            {
                // ignored
                string error = "Error while unfreezing.\r\n";
                if (!isFrozen)
                    error = "Error while freezing.\r\nPlease refresh the heap search and retry.\r\n";
                error += "Exception: " + ex;
                MessageBox.Show(error, "Error", MessageBoxButton.OK);
            }
        }

        private async void CountButton_Click(object sender, RoutedEventArgs e)
        {
            countButton.IsEnabled = false;

            var originalBrush = countLabel.Foreground;
            Brush transparentColor = originalBrush.Clone();
            transparentColor.Opacity = 0;
            countLabel.Foreground = transparentColor;
            spinner1.Visibility = Visibility.Visible;


            AssemblyModel assembly = assembliesListBox.SelectedItem as AssemblyModel;
            if (assembly == null)
            {
                MessageBox.Show("You must select an assembly first.", "Error", MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }

            string assemblyFilter = assembly.Name;
            if (assembly.Name == "* All")
                assemblyFilter = "*"; // Wildcard

            if (_app is UnmanagedRemoteApp)
                assemblyFilter += "!*"; // Indicate we want any type within the module
            else if (_app is ManagedRemoteApp)
                assemblyFilter += ".*"; // Indicate we want any type within the assembly

            var x = CliWrap.Cli.Wrap("rnet-dump.exe")
                .WithArguments($"heap -t {TargetPid} -q {assemblyFilter} " + UnmanagedFlagIfNeeded())
                .WithValidation(CommandResultValidation.None)
                .ExecuteBufferedAsync();
            BufferedCommandResult res = await x.Task;
            IEnumerable<string> rnetDumpStdOutLines = res.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .SkipWhile(line => !line.Contains("Found "))
                .Skip(1)
                .Select(str => str.Trim())
                .Select(str => str.Split(' ')[1]);

            List<DumpedTypeModel> types = await GetTypesListAsync();
            // Like `Distinct` without an IEqualityComparer
            var uniqueTypes = types.GroupBy(x => x.FullTypeName).Select(grp => grp.First());
            Dictionary<string, DumpedTypeModel> typeNamesToTypes = uniqueTypes.ToDictionary(dumpedType => dumpedType.FullTypeName);
            Dictionary<string, int> typesAndInstancesCount = uniqueTypes.ToDictionary(dumpedType => dumpedType.FullTypeName, _ => 0);
            foreach (string heapObjectType in rnetDumpStdOutLines)
            {
                if (typesAndInstancesCount.ContainsKey(heapObjectType))
                    typesAndInstancesCount[heapObjectType]++;
            }


            List<DumpedTypeModel> dumpedTypes = new List<DumpedTypeModel>();
            foreach (KeyValuePair<string, int> kvp in typesAndInstancesCount)
            {
                int? numInstances = kvp.Value != 0 ? kvp.Value : null;
                DumpedTypeModel dt;
                if (_dumpedTypesCache.TryGetValue(kvp.Key, out dt))
                {
                    dt.NumInstances = numInstances;
                }
                else
                {
                    dt = typeNamesToTypes[kvp.Key];
                    dt.NumInstances = numInstances;
                    _dumpedTypesCache[kvp.Key] = dt;
                }
                dumpedTypes.Add(dt);
            }

            TypesControl.UpdateTypesList(dumpedTypes);

            spinner1.Visibility = Visibility.Collapsed;
            countLabel.Foreground = originalBrush;
            countButton.IsEnabled = true;
        }

        private void ManualTraceClicked(object sender, RoutedEventArgs e)
        {
            if (_procBoxCurrItem == null)
            {
                MessageBox.Show("You must attach to a process first", "Error", MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }

            TraceQueryWindow qWin = new TraceQueryWindow();
            bool? res = qWin.ShowDialog();
            if (res == true)
            {

                List<string> args = new List<string>() { "-t", TargetPid.ToString(), UnmanagedFlagIfNeeded() };
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
            string trace = (sender as FrameworkElement)?.DataContext as string;
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

        private async Task InteractiveWindow_Init()
        {
            if (interactivePanel.IsStarted)
                return;

            RuntimeType runtime = RuntimeType.Managed;
            if (_app is UnmanagedRemoteApp)
                runtime = RuntimeType.Unmanaged;

            string RuntimeTypeFullTypeName = typeof(RuntimeType).FullName;

            await interactivePanel.StartAsync("rnet-repl.exe");
            string connectionScript =
@$"var app = RemoteAppFactory.Connect(Process.GetProcessById({TargetPid}), {RuntimeTypeFullTypeName}.{runtime});";
            await interactivePanel.WriteInputTextAsync($"{connectionScript}\r\n");
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

                    // Do something with each element, for example, print their names
                    element.IndianaJones("FontSize", (double oldVal) => up ? (oldVal + 2) : (oldVal - 2));
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
            AssemblyModel module = assembliesListBox.SelectedItem as AssemblyModel;
            WatchModuleAllocations(module);
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

        private void injectDllButton_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog ofd = new OpenFileDialog();
            ofd.Multiselect = false;
            if (ofd.ShowDialog() != true)
                return;

            string file = ofd.FileName;
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

        private void TraceTypeExtraButton_OnMouseEnter(object sender, MouseEventArgs e)
        {
            traceTypeExtraButtonHoverHack.Visibility = (sender as Button).IsMouseOver ? Visibility.Visible : Visibility.Hidden;
            Debug.WriteLine($"traceTypeExtraButtonHoverHack.Visibility changed to {traceTypeExtraButtonHoverHack.Visibility}");
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

        private async void TypesModel_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(TypesModel.SelectedType))
                return;

            DumpedTypeModel selectedType = (sender as TypesModel).SelectedType;
            if (selectedType == null)
                return;

            string type = _currSelectedType?.FullTypeName;
            if (type == null)
            {
                membersListBox.ItemsSource = null;
                return;
            }

            var x = CliWrap.Cli.Wrap("rnet-dump.exe")
                .WithArguments($"members -t {TargetPid} -q \"{type}\" -n true " + UnmanagedFlagIfNeeded())
                .WithValidation(CommandResultValidation.None)
                .ExecuteBufferedAsync();
            var res = await x.Task;
            var xx = res.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .SkipWhile(line => !line.Contains("Members of "))
                .Skip(1)
                .Select(str => str.Trim());

            List<string> rawLinesList = xx.ToList();
            List<DumpedMember> dumpedMembers = new List<DumpedMember>();
            for (int i = 0; i < rawLinesList.Count; i += 2)
            {
                DumpedMember dumpedMember = new DumpedMember()
                {
                    RawName = rawLinesList[i],
                    NormalizedName = rawLinesList[i + 1]
                };
                dumpedMembers.Add(dumpedMember);
            }

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
            ObservableCollection<DumpedTypeModel> mainTypesControlTypes = (this.TypesControl.DataContext as TypesModel).Types;
            Dictionary<string, DumpedTypeModel> mainControlFullTypeNameToTypes = mainTypesControlTypes.ToDictionary(x => x.FullTypeName);
            var typesModel = new TypesModel();
            List<DumpedTypeModel> deepCopiesTypesList = await GetTypesListAsync(_allAssembliescModel).ContinueWith((task) =>
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
            AssemblyModel matchingAssembly = (assembliesListBox.ItemsSource as List<AssemblyModel>).FirstOrDefault(x => x.Name == assembly);
            int index = assembliesListBox.Items.IndexOf(matchingAssembly);

            //Trick to scroll to our selected item from the BOTTOM
            double singleListItemHeight = assembliesListBox.FindVisualChildren<ListBoxItem>().First().ActualHeight;
            double numItemsShown = assembliesListBox.ActualHeight / singleListItemHeight;
            var furtherDownItem = assembliesListBox.Items[Math.Min(index + (int)numItemsShown - 2, assembliesListBox.Items.Count - 1)];
            assembliesListBox.SelectedItem = matchingAssembly;
            assembliesListBox.ScrollIntoView(furtherDownItem);
        }
#pragma warning restore IDE0051 // Remove unused private members
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
