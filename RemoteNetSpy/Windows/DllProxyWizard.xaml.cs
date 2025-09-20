using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using RemoteNET.Access;
using RemoteNET.Internal.Extensions;
using SharpDllProxy;

namespace RemoteNetSpy.Windows
{
    /// <summary>
    /// Interaction logic for DllProxyWizard.xaml
    /// </summary>
    public partial class DllProxyWizard : Window, INotifyPropertyChanged
    {
        private Process _targetProcess;
        private string _processName;
        private int _processId;
        private string _processArchitecture;
        private string _payloadDllPath;
        private ObservableCollection<VictimModuleFinder.VictimModuleInfo> _victimDlls;
        private VictimModuleFinder.VictimModuleInfo _selectedVictimDll;

        public string ProcessName
        {
            get => _processName;
            set { _processName = value; OnPropertyChanged(nameof(ProcessName)); }
        }

        public int ProcessId
        {
            get => _processId;
            set { _processId = value; OnPropertyChanged(nameof(ProcessId)); }
        }

        public string ProcessArchitecture
        {
            get => _processArchitecture;
            set { _processArchitecture = value; OnPropertyChanged(nameof(ProcessArchitecture)); }
        }

        public string PayloadDllPath
        {
            get => _payloadDllPath;
            set { _payloadDllPath = value; OnPropertyChanged(nameof(PayloadDllPath)); }
        }

        public ObservableCollection<VictimModuleFinder.VictimModuleInfo> VictimDlls
        {
            get => _victimDlls;
            set { _victimDlls = value; OnPropertyChanged(nameof(VictimDlls)); }
        }

        public VictimModuleFinder.VictimModuleInfo SelectedVictimDll
        {
            get => _selectedVictimDll;
            set
            {
                _selectedVictimDll = value;
                OnPropertyChanged(nameof(SelectedVictimDll));
                CreateProxyButton.IsEnabled = value != null;
            }
        }

        /// <summary>
        /// Gets the file path of the selected DLL to proxy, or null if none selected
        /// </summary>
        public string SelectedDllPath => SelectedVictimDll?.OriginalFilePath;

        public DllProxyWizard(Process targetProcess)
        {
            InitializeComponent();
            DataContext = this;

            _targetProcess = targetProcess;
            ProcessName = targetProcess.ProcessName;
            ProcessId = targetProcess.Id;
            ProcessArchitecture = Environment.Is64BitProcess ? "x64" : "x86";

            // Set up payload DLL path from injection toolkit
            try
            {
                var kit = InjectionToolKit.GetKit(targetProcess, targetProcess.GetSupportedTargetFramework());
                PayloadDllPath = kit.UnmanagedAdapterPath;
            }
            catch (Exception ex)
            {
                PayloadDllPath = $"Error getting toolkit: {ex.Message}";
            }

            VictimDlls = new ObservableCollection<VictimModuleFinder.VictimModuleInfo>();
        }

        private async void ScanDllsButton_Click(object sender, RoutedEventArgs e)
        {
            ScanDllsButton.IsEnabled = false;
            ScanStatusText.Text = "Scanning process DLLs...";

            try
            {
                var dlls = await Task.Run(() => VictimModuleFinder.Search(_targetProcess));
                
                VictimDlls.Clear();
                foreach (var dll in dlls)
                {
                    VictimDlls.Add(dll);
                }

                ScanStatusText.Text = $"Found {dlls.Count} DLLs. Select one to hijack.";
            }
            catch (Exception ex)
            {
                ScanStatusText.Text = $"Error scanning DLLs: {ex.Message}";
                MessageBox.Show($"Failed to scan process DLLs:\n{ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                ScanDllsButton.IsEnabled = true;
            }
        }

        private async void CreateProxyButton_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedVictimDll == null)
            {
                MessageBox.Show("Please select a DLL to hijack.", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Show progress section
            ProgressGroupBox.Visibility = Visibility.Visible;
            CreateProxyButton.IsEnabled = false;
            CloseButton.Content = "Cancel";

            ProgressText.Text = "Creating proxy DLL...";
            ProgressBar.IsIndeterminate = true;
            LogTextBox.Clear();

            try
            {
                await CreateProxyAsync();
                ProgressText.Text = "Proxy DLL created successfully!";
                ProgressBar.IsIndeterminate = false;
                ProgressBar.Value = 100;
                
                var result = MessageBox.Show(
                    "Proxy DLL created successfully!\n\n" +
                    "The target process will be restarted with the proxy DLL.\n" +
                    "Continue with the injection?",
                    "Success",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    DialogResult = true;
                }
            }
            catch (Exception ex)
            {
                ProgressText.Text = "Error creating proxy DLL";
                ProgressBar.IsIndeterminate = false;
                AppendLog($"ERROR: {ex.Message}");
                MessageBox.Show($"Failed to create proxy DLL:\n{ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                CreateProxyButton.IsEnabled = true;
                CloseButton.Content = "Close";
            }
        }

        private async Task CreateProxyAsync()
        {
            await Task.Run(() =>
            {
                var proxyCreator = new ProxyCreator(LogMessage);
                
                Dispatcher.Invoke(() => AppendLog($"Target DLL: {SelectedVictimDll.OriginalFilePath}"));
                Dispatcher.Invoke(() => AppendLog($"Payload DLL: {PayloadDllPath}"));
                Dispatcher.Invoke(() => AppendLog($"Export Count: {SelectedVictimDll.ExportCount}"));
                Dispatcher.Invoke(() => AppendLog(""));

                var results = proxyCreator.CreateProxy(
                    SelectedVictimDll.OriginalFilePath,
                    PayloadDllPath,
                    "PromptEntryPoint");

                if (results != null)
                {
                    Dispatcher.Invoke(() => AppendLog($"Output DLL: {results.OutputDll}"));
                    Dispatcher.Invoke(() => AppendLog($"Proxied DLL: {results.ProxiedDll}"));
                }
            });
        }

        private void LogMessage(string message)
        {
            if (!string.IsNullOrEmpty(message))
            {
                Dispatcher.Invoke(() => AppendLog(message));
            }
        }

        private void AppendLog(string message)
        {
            LogTextBox.AppendText(message + Environment.NewLine);
            LogTextBox.ScrollToEnd();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
