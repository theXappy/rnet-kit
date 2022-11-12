using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using CliWrap;
using CliWrap.Buffered;
using Microsoft.Win32;
using RemoteNET;

namespace RemoteNetGui
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        RemoteApp _app = null;

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
                //.ToList()
                //.Select(line => line.Split('\t').ToArray())
                //.Where(arr => arr.Length > 2)
                //.Select(arr => arr[1])
                .Select(str => str.Trim())
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


        private string _procBoxCurrItem;
        public string ProcName => _procBoxCurrItem.Split('\t').ToArray()[1].Trim();

        public string ClassName => (typesListBox.SelectedItem as DumpedType).FullTypeName;

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

            _procBoxCurrItem = procsBox.SelectedItem as string;

            StopGlow();

            if (_app != null)
            {
                _app.Dispose();
                _app = null;
            }
            _app = await Task.Run(() => RemoteApp.Connect(ProcName));

            var x = CliWrap.Cli.Wrap("rnet-dump.exe")
                .WithArguments($"types -t {ProcName} -q *")
                .WithValidation(CommandResultValidation.None)
                .ExecuteBufferedAsync();
            var res = await x.Task;
            var xx = res.StandardOutput.Split('\n')
                .Skip(2)
                .Select(str => str.Trim());
            foreach (string line in xx)
            {
                string[] parts = line.Split("]");
                if (parts.Length < 2)
                    continue;
                string assembly = parts[0].Trim(' ', '[', ']');
                string type = parts[1].Trim();
                if (!_assembliesToTypes.TryGetValue(assembly, out List<string> types))
                {
                    types = new List<string>();
                    _assembliesToTypes[assembly] = types;
                }
                types.Add(type);
            }

            var assemblies = _assembliesToTypes.Keys.ToList();
            assemblies.Add("* All");
            assemblies.Sort();
            assembliesListBox.ItemsSource = assemblies;
        }

        private Dictionary<string, List<string>> _assembliesToTypes = new Dictionary<string, List<string>>();

        private async void assembliesListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            membersListBox.ItemsSource = null;

            List<string> types = await GetTypesList();

            List<DumpedType> dumpedTypes = types.Select(str => new DumpedType(str, null)).ToList();
            typesListBox.ItemsSource = dumpedTypes;

            // Reapply filter
            filterBox_TextChanged(sender, null);
        }

        private async Task<List<string>?> GetTypesList()
        {
            string assembly = assembliesListBox.SelectedItem as string;
            List<string> types = null;
            if (assembly == "* All")
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
            string type = (typesListBox.SelectedItem as DumpedType)?.FullTypeName;
            if (type == null)
                return;


            var x = CliWrap.Cli.Wrap("rnet-dump.exe")
                .WithArguments($"members -t {ProcName} -q {type}")
                .WithValidation(CommandResultValidation.None)
                .ExecuteBufferedAsync();
            var res = await x.Task;
            var xx = res.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .SkipWhile(line => !line.Contains("Members of "))
                .Skip(1)
                .Select(str => str.Trim());

            List<string> membersList = xx.ToList();
            membersList.Sort();

            membersListBox.ItemsSource = membersList;
        }

        private async void FindHeapInstancesButtonClicked(object sender, RoutedEventArgs e)
        {
            string type = typesListBox.SelectedItem as string;

            var x = CliWrap.Cli.Wrap("rnet-dump.exe")
                .WithArguments($"heap -t {ProcName} -q {type}")
                .WithValidation(CommandResultValidation.None)
                .ExecuteBufferedAsync();
            var res = await x.Task;
            var xx = res.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .SkipWhile(line => !line.Contains("Found "))
                .Skip(1)
                .Select(str => str.Trim())
                .Select(HeapObject.Parse);

            List<HeapObject> instancesList = xx.ToList();
            instancesList.Sort();

            heapInstancesListBox.ItemsSource = instancesList;
        }

        private void filterBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ListBox associatedBox = null;
            if (sender == typesFilterBox)
                associatedBox = typesListBox;
            if (sender == assembliesFilterBox)
                associatedBox = assembliesListBox;
            if (sender == membersFilterBox)
                associatedBox = membersListBox;

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
                view.Filter = (o) => (o as DumpedType)?.FullTypeName?.Contains(filter) == true;
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

        private async void ProcsRefreshButton_OnClick(object sender, RoutedEventArgs e) => await RefreshProcessesList();

        private void RunTracesButtonClicked(object sender, RoutedEventArgs e)
        {
            if (!_traceList.Any())
            {
                MessageBox.Show("List of functions to trace is empty.");
                return;
            }

            List<string> args = new List<string>() { "-t", ProcName };
            foreach (string funcToTrace in _traceList)
            {
                args.Add("-i");
                args.Add($"\"{funcToTrace}\"");
            }

            string argsLine = string.Join(' ', args);
            MessageBox.Show(argsLine);
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
                string path = ofd.SafeFileName;
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

        private void FreezeUnfreezeHeapObject(object sender, RoutedEventArgs e)
        {
            Button senderButton = sender as Button;
            HeapObject? dataContext = senderButton.DataContext as HeapObject;
            if (dataContext.Frozen)
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
                try
                {
                    RemoteObject ro = _app.GetRemoteObject(address);
                    dataContext.Address = ro.RemoteToken;
                    dataContext.RemoteObject = ro;
                }
                catch
                {
                    MessageBox.Show($"Failed to get object at 0x{address:X8}.\n" +
                                    $"Please refresh heap instances panel and retry.");
                }
            }
        }

        private async void CountButton_Click(object sender, RoutedEventArgs e)
        {
            countButton.IsEnabled = false;

            var originalBrush = countLabel.Foreground;
            Brush transparentColor = originalBrush.Clone();
            transparentColor.Opacity = 0;
            countLabel.Foreground = transparentColor;
            rect1.Visibility = Visibility.Visible;

            List<string> types = await GetTypesList();
            Dictionary<string, int> typesAndIInstancesCount = types.ToDictionary(t => t, t => 0);


            var x = CliWrap.Cli.Wrap("rnet-dump.exe")
                .WithArguments($"heap -t {ProcName} -q *")
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

            List<DumpedType> dumpedTypes = new List<DumpedType>();
            foreach (KeyValuePair<string, int> kvp in typesAndIInstancesCount)
            {
                int? numInstances = kvp.Value != 0 ? kvp.Value : null;
                DumpedType dt = new DumpedType(kvp.Key, numInstances);
                dumpedTypes.Add(dt);
            }

            typesListBox.ItemsSource = dumpedTypes;

            // Reapply filter
            filterBox_TextChanged(typesFilterBox, null);

            rect1.Visibility = Visibility.Collapsed;
            countLabel.Foreground = originalBrush;

            countButton.IsEnabled = true;
        }
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

    public class HeapObject : INotifyPropertyChanged
    {
        private ulong _address;
        private RemoteObject remoteObject;

        public ulong Address
        {
            get
            {
                if (RemoteObject != null)
                    return RemoteObject.RemoteToken;
                return _address;
            }
            set
            {
                if (RemoteObject != null)
                    throw new Exception("Can't set address for frozen heap object");
                _address = value;
                OnPropertyChanged();
            }
        }

        public string FullTypeName { get; set; }

        public RemoteObject RemoteObject
        {
            get => remoteObject;
            set
            {
                remoteObject = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(Frozen));
            }
        }

        public bool Frozen => RemoteObject != null;

        public static HeapObject Parse(string text)
        {
            string[] splitted = text.Split(' ');
            string addressStr = splitted[0];
            if (addressStr.StartsWith("0x"))
                addressStr = addressStr[2..];
            ulong address = Convert.ToUInt64(addressStr, 16);

            return new HeapObject() { Address = address, FullTypeName = splitted[1] };
        }

        public override string ToString()
        {
            return $"0x{Address:X8} {FullTypeName}";
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }
    }
}
