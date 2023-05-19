using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using AvalonDock.Controls;
using CliWrap;
using CliWrap.Buffered;
using CommandLine;
using Microsoft.Win32;
using RemoteNET;
using RemoteNetSpy;

namespace RemoteNetGui
{
    public class InjectableProcess
    {
        public int Pid { get; set; }
        public string Name { get; set; }
        public string DotNetVersion { get; set; }
        public string DiverState { get; set; }

        public InjectableProcess(int pid, string name, string dotNetVersion, string diverState)
        {
            Pid = pid;
            Name = name;
            DotNetVersion = dotNetVersion;
            DiverState = diverState;
        }
    }

    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        RemoteApp _app = null;
        private DumpedTypeToDescription _dumpedTypeToDescription = new DumpedTypeToDescription();

        public MainWindow()
        {
            InitializeComponent();
            tracesListBox.ItemsSource = _traceList;
            tracesListBox.Items.SortDescriptions.Add(
                new System.ComponentModel.SortDescription("",
                    System.ComponentModel.ListSortDirection.Ascending));
        }

        private async void MainWindow_OnInitialized(object? sender, EventArgs e) => await RefreshProcessesList();
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


        private Timer? t;
        private bool _glowActive = false;
        private int step = 0;

        private void UpdateGlowEffect(object? state)
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


        public InjectableProcess? _procBoxCurrItem;
        public int TargetPid => _procBoxCurrItem?.Pid ?? 0;

        private DumpedType _currSelectedType;
        public string ClassName => _currSelectedType.FullTypeName;

        private void StopGlow()
        {
            _glowActive = false;
            t?.Dispose();
            t = null;
            procsBoxBorder.Effect = null;
        }

        private async void procsBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (procsBox.SelectedIndex == -1)
                return;

            _procBoxCurrItem = procsBox.SelectedItem as InjectableProcess;

            StopGlow();

            // Saving aside last RemoteApp
            var oldApp = _app;

            // Creating new RemoteApp
            _app = await Task.Run(() => RemoteAppFactory.Connect(Process.GetProcessById(TargetPid), RuntimeType.Unmanaged));

            // Only now we try to dispose of the old RemoteApp.
            // We must do it after creating a new one for the case where the user re-attaches to the same
            // app. Closing our old one before the new one is connected willl cause the Diver to die.
            if (oldApp != null)
            {
                try
                {
                    oldApp.Dispose();
                }
                catch
                {
                }
                oldApp = null;
            }

            RefreshAssembliesAsync();
        }


        private async void AssembliesRefreshButton_OnClick(object sender, RoutedEventArgs e) => RefreshAssembliesAsync();

        private Regex r = new Regex(@"\[(.*?)\]\[(.*?)\](.*)");
        private Task RefreshAssembliesAsync()
        {
            return Task.Run(() =>
            {

                Dispatcher.Invoke(() =>
                {
                    assembliesSpinner.Visibility = Visibility.Visible;
                });

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
                    var assembly = new AssemblyDesc(assemblyName, runtime);
                    string type = parts[2].Trim();
                    if (!_assembliesToTypes.TryGetValue(assembly, out List<string> types))
                    {
                        types = new List<string>();
                        _assembliesToTypes[assembly] = types;
                    }

                    types.Add(type);
                }

                var assemblies = _assembliesToTypes.Keys.ToList();
                assemblies.Add(new AssemblyDesc("* All", RuntimeType.Unknown));
                assemblies.Sort((desc, assemblyDesc) => desc.Name.CompareTo(assemblyDesc.Name));

                Dispatcher.Invoke(() =>
                {
                    assembliesListBox.ItemsSource = assemblies;
                    assembliesSpinner.Visibility = Visibility.Collapsed;
                });
            });
        }

        private string UnmanagedFlagIfNeeded()
        {
            if (_app is UnmanagedRemoteApp)
                return "-u";
            return string.Empty;
        }

        private class AssemblyDesc
        {
            public string Name { get; private set; }
            public RuntimeType Runtime { get; private set; }

            public AssemblyDesc(string name, RuntimeType runtime)
            {
                Name = name;
                Runtime = runtime;
            }

            public AssemblyDesc(string name, string runtime) : this(name, Enum.Parse<RuntimeType>(runtime))
            {
            }

            public override bool Equals(object? obj)
            {
                if(obj is not AssemblyDesc other) return false;
                return Name.Equals(other?.Name) && Runtime.Equals(other?.Runtime);
            }

            public override int GetHashCode()
            {
                return Name.GetHashCode();
            }
        }

        private Dictionary<AssemblyDesc, List<string>> _assembliesToTypes = new Dictionary<AssemblyDesc, List<string>>();

        private async void assembliesListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            membersListBox.ItemsSource = null;

            List<string> types = await GetTypesList();

            List<DumpedType> dumpedTypes = types.Select(str => new DumpedType(str, null)).ToList();
            typesListBox.ItemsSource = dumpedTypes;

            // Reapply filter for types
            filterBox_TextChanged(typesFilterBox, null);
        }

        private async Task<List<string>?> GetTypesList()
        {
            AssemblyDesc assembly = assembliesListBox.SelectedItem as AssemblyDesc;
            List<string> types = null;
            if (assembly == null)
                return new List<string>();
            if (assembly.Name == "* All")
            {
                await Task.Run(() =>
                {
                    types = _assembliesToTypes.Values.SelectMany(list => list).ToList();
                    types.Sort();
                });
            }
            else
            {
                await Task.Run(() =>
                {
                    types = _assembliesToTypes[assembly];
                    types.Sort();
                });
            }

            return types.ToHashSet().ToList();
        }

        private async void typesListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _currSelectedType = (typesListBox.SelectedItem as DumpedType);
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

            dumpedMembers.Sort((member1, member2) => member1.RawName.CompareTo(member2.RawName));

            membersListBox.ItemsSource = dumpedMembers;
        }

        private async void FindHeapInstancesButtonClicked(object sender, RoutedEventArgs e)
        {
            if (typesListBox.SelectedItem == null)
            {
                MessageBox.Show("You must select a type from the \"Types\" listbox first.", $"{this.Title} Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            findHeapInstancesButtonSpinner.Width = findHeapInstancesButtonTextPanel.ActualWidth;
            findHeapInstancesButtonSpinner.Visibility = Visibility.Visible;
            findHeapInstancesButtonTextPanel.Visibility = Visibility.Collapsed;

            string type = (typesListBox.SelectedItem as DumpedType)?.FullTypeName;

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

            heapInstancesListBox.ItemsSource = _instancesList;

            findHeapInstancesButtonSpinner.Visibility = Visibility.Collapsed;
            findHeapInstancesButtonTextPanel.Visibility = Visibility.Visible;
        }

        private List<HeapObject> _instancesList;

        private void filterBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            bool matchCase = true;
            ListBox associatedBox = null;
            if (sender == typesFilterBox)
            {
                associatedBox = typesListBox;
                matchCase = _matchCaseTypes;
            }
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
            if (string.IsNullOrWhiteSpace(filter))
            {
                view.Filter = null;
            }
            else
            {
                StringComparison comp =
                    matchCase ? StringComparison.CurrentCulture : StringComparison.CurrentCultureIgnoreCase;
                view.Filter = (o) =>
                {
                    if (sender == membersFilterBox)
                        return (o as DumpedMember)?.NormalizedName?.Contains(filter, comp) == true;
                    if (sender == typesFilterBox)
                        return (_dumpedTypeToDescription.Convert(o, null, null, null) as string)?.Contains(filter, comp) == true;
                    return (o as string)?.Contains(filter) == true;
                };
            }
        }

        private void clearTypesFilterButton_OnClick(object sender, RoutedEventArgs e)
        {
            if (sender == clearTypesFilterButton)
                typesFilterBox.Clear();
            if (sender == clearAssembliesFilterButton)
                assembliesFilterBox.Clear();
            if (sender == clearMembersFilterButton)
                membersFilterBox.Clear();
        }

        private async void ProcsRefreshButton_OnClick(object sender, RoutedEventArgs e)
        {
            membersListBox.ItemsSource = null;
            typesListBox.ItemsSource = null;
            assembliesListBox.ItemsSource = null;
            heapInstancesListBox.ItemsSource = null;
            _traceList.Clear();

            await RefreshProcessesList();
        }

        private void RunTracesButtonClicked(object sender, RoutedEventArgs e)
        {
            if (!_traceList.Any())
            {
                MessageBox.Show("List of functions to trace is empty.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (_procBoxCurrItem == null)
            {
                MessageBox.Show("You must attach to a process first", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            List<string> args = new List<string>() { "-t", TargetPid.ToString(), UnmanagedFlagIfNeeded() };
            foreach (string funcToTrace in _traceList)
            {
                args.Add("-i");
                string reducedSignaturee = funcToTrace;//.Substring(0, funcToTrace.IndexOf('('));
                args.Add($"\"{reducedSignaturee}\"");
            }

            string argsLine = string.Join(' ', args);
            ProcessStartInfo psi = new ProcessStartInfo("rnet-trace.exe", argsLine)
            {
                UseShellExecute = true
            };
            Process.Start(psi);
        }

        private ObservableCollection<string> _traceList = new ObservableCollection<string>();

        private void MemberListItemMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed && e.ClickCount == 2)
            {
                string member = (sender as TextBlock)?.Text;
                if (!member.StartsWith("[Method]") && !member.StartsWith("[Constructor]"))
                {
                    return;
                }

                // Removing "[Method]" prefix
                string justSignature = member[(member.IndexOf(']') + 1)..].TrimStart();

                // Splitting return type + name / parameters
                string parametrs = justSignature[(justSignature.IndexOf('('))..];
                string retTypeAndName = justSignature[..(justSignature.IndexOf('('))];

                // Splitting return type and name
                string methodName = retTypeAndName[(retTypeAndName.LastIndexOf(' ') + 1)..];
                string retType = retTypeAndName[..(retTypeAndName.LastIndexOf(' '))];

                string sigWithoutReturnType = methodName + parametrs;
                string targetClass = ClassName;

                string newItem = $"{targetClass}.{sigWithoutReturnType}";
                if (!_traceList.Contains(newItem))
                    _traceList.Add(newItem);

                tabControl.SelectedItem = tracingTabItem;
            }
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
                using FileStream f = File.Open(path, FileMode.OpenOrCreate);
                using StreamReader sr = new StreamReader(f);
                while (!sr.EndOfStream)
                {
                    string traceFunc = sr.ReadLine().TrimEnd();
                    _traceList.Add(traceFunc);
                }
            }
        }

        private void SaveTraceListClicked(object sender, RoutedEventArgs e)
        {
            var sfd = new SaveFileDialog();
            sfd.Filter = "Functions List File (*.flist)|*.flist";
            sfd.OverwritePrompt = true;
            bool? success = sfd.ShowDialog();
            if (success == true)
            {
                string path = sfd.FileName;
                if (path == null)
                {
                    MessageBox.Show("Invalid file name.");
                    return;
                }

                using FileStream f = File.Open(path, FileMode.OpenOrCreate);
                using StreamWriter sw = new StreamWriter(f);
                foreach (string traceFunction in _traceList)
                {
                    sw.WriteLine(traceFunction);
                }
                sw.Flush();
            }
        }

        private async void FreezeUnfreezeHeapObject(object sender, RoutedEventArgs e)
        {
            Button senderButton = sender as Button;
            var grid = senderButton.FindLogicalChildren<Grid>().Single();
            var dPanel = grid.Children.OfType<DockPanel>().Single();
            var loadingImage = grid.Children.OfType<Image>().Single();

            dPanel.Visibility = Visibility.Collapsed;
            loadingImage.Visibility = Visibility.Visible;

            HeapObject? dataContext = senderButton.DataContext as HeapObject;

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
                    Task dumperTask = Task.Run(() =>
                    {
                        RemoteObject ro = _app.GetRemoteObject(address, dataContext.FullTypeName);
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
            finally
            {
                dPanel.Visibility = Visibility.Visible;
                loadingImage.Visibility = Visibility.Collapsed;
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

            List<string> types = await GetTypesList();
            Dictionary<string, int> typesAndIInstancesCount = types.ToDictionary(t => t, t => 0);


            var x = CliWrap.Cli.Wrap("rnet-dump.exe")
                .WithArguments($"heap -t {TargetPid} -q * " + UnmanagedFlagIfNeeded())
                .WithValidation(CommandResultValidation.None)
                .ExecuteBufferedAsync();
            var res = await x.Task;
            var xx = res.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .SkipWhile(line => !line.Contains("Found "))
                .Skip(1)
                .Select(str => str.Trim())
                .Select(str => str.Split(' ')[1]);

            foreach (string heapObjectType in xx)
            {
                if (typesAndIInstancesCount.ContainsKey(heapObjectType))
                    typesAndIInstancesCount[heapObjectType]++;
            }

            string lastSelected = (typesListBox?.SelectedItem as DumpedType)?.FullTypeName;
            DumpedType typeToReselect = null;

            List<DumpedType> dumpedTypes = new List<DumpedType>();
            foreach (KeyValuePair<string, int> kvp in typesAndIInstancesCount)
            {

                int? numInstances = kvp.Value != 0 ? kvp.Value : null;
                DumpedType dt = new DumpedType(kvp.Key, numInstances);
                dumpedTypes.Add(dt);

                // Check if this was the last selected type  in the listbox
                if (kvp.Key == lastSelected)
                {
                    typeToReselect = dt;
                }
            }

            typesListBox.ItemsSource = dumpedTypes;
            if (typeToReselect != null)
            {
                typesListBox.SelectedItem = typeToReselect;
            }

            // Reapply filter
            filterBox_TextChanged(typesFilterBox, null);

            spinner1.Visibility = Visibility.Collapsed;
            countLabel.Foreground = originalBrush;

            countButton.IsEnabled = true;
        }

        private void ManualTraceClicked(object sender, RoutedEventArgs e)
        {
            if (_procBoxCurrItem == null)
            {
                MessageBox.Show("You must attach to a process first", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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

        private void TypeMenuItem_OnClick(object sender, RoutedEventArgs e)
        {
            MenuItem mi = sender as MenuItem;
            string typeName = (mi.DataContext as DumpedType).FullTypeName;
            Clipboard.SetText(typeName);
        }

        private void MemberMenuItem_OnClick(object sender, RoutedEventArgs e)
        {
            MenuItem mi = sender as MenuItem;
            string member = (mi.DataContext as DumpedMember).NormalizedName;
            Clipboard.SetText(member);
        }

        private bool _matchCaseTypes = false;
        private void TypesMatchCaseButton_OnClick(object sender, RoutedEventArgs e)
        {
            _matchCaseTypes = !_matchCaseTypes;
            Button b = (sender as Button);
            Brush brush = b.FindResource("ControlSelectedBackground") as Brush;
            b.Background = _matchCaseTypes ? brush : null;
            filterBox_TextChanged(typesFilterBox, null);
        }

        private void InspectButtonBaseOnClick(object sender, RoutedEventArgs e)
        {
            Button senderButton = sender as Button;
            HeapObject? dataContext = senderButton.DataContext as HeapObject;
            if (!dataContext.Frozen || dataContext.RemoteObject == null)
            {
                MessageBox.Show("ERROR: Object must be frozed.");
                return;
            }

            (ObjectViewer.CreateViewerWindow(this, dataContext.RemoteObject)).ShowDialog();
        }

        private void TraceLineDelete_OnClick(object sender, RoutedEventArgs e)
        {
            string trace = (sender as FrameworkElement)?.DataContext as string;
            if (trace != null)
            {
                _traceList.Remove(trace);
            }
        }

        private void ExploreButtonBaseOnClick(object sender, RoutedEventArgs e)
        {
            Button senderButton = sender as Button;
            HeapObject? dataContext = senderButton.DataContext as HeapObject;
            if (!dataContext.Frozen || dataContext.RemoteObject == null)
            {
                MessageBox.Show("ERROR: Object must be frozed.");
                return;
            }

            RuntimeType runtime = RuntimeType.Managed;
            if (_app is UnmanagedRemoteApp)
                runtime = RuntimeType.Unmanaged;
            string script =
@$"var app = RemoteAppFactory.Connect(Process.GetProcessById({TargetPid}), {nameof(RuntimeType)}.{runtime});
var ro = app.GetRemoteObject(0x{dataContext.Address:X16}, ""{dataContext.FullTypeName}"");
dynamic dro = ro.Dynamify();
";

            string path = Path.GetTempFileName();
            File.WriteAllText(path, script);

            Process.Start("rnet-repl.exe", new string[] { "--statementsFile", path });
        }
    }

    public class DumpedMember
    {
        public string RawName { get; set; }
        // This one has generic args normalized from [[System.Byte, ... ]] to <System.Byte>
        public string NormalizedName { get; set; }
    }

    public class DumpedType
    {
        public string FullTypeName { get; private set; }
        private int? _numInstances;
        public bool HaveInstances => _numInstances != null;
        public int NumInstances => _numInstances ?? 0;

        public DumpedType(string fullTypeName, int? numInstances)
        {
            FullTypeName = fullTypeName;
            _numInstances = numInstances;
        }
    }
}
