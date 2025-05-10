using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CliWrap;
using CliWrap.Buffered;
using RemoteNetSpy.Models;
using RemoteNetSpy.ViewModels;

namespace RemoteNetSpy.Windows
{
    public partial class TargetSelectionWindow : Window
    {
        enum TargetSelectionMode
        {
            Attach,
            Ambush
        }

        // mode property
        private TargetSelectionMode _mode = TargetSelectionMode.Attach;

        public InjectableProcess SelectedProcess { get; private set; }

        public TargetSelectionWindow()
        {
            InitializeComponent();
            var dataContext = new TargetSelectionViewModel();
            dataContext.ItemDoubleClick += HandleProcessDoubleClick;
            DataContext = dataContext;
            Loaded += TargetSelectionWindow_Loaded;
        }


        private async Task RefreshProcessesListAsync()
        {
            procsBox.ItemsSource = null;
            procsBox.IsEnabled = false;
            //procsBoxLoadingOverlay.Visibility = Visibility.Visible;

            try
            {
                CommandTask<BufferedCommandResult> commandTask = CliWrap.Cli.Wrap("rnet-ps.exe").ExecuteBufferedAsync();
                BufferedCommandResult runResults = await commandTask.Task;
                IEnumerable<string[]> splitLines = runResults.StandardOutput.Split('\n')
                    .Skip(1)
                    .ToList()
                    .Select(line => line.Split('\t', StringSplitOptions.TrimEntries).ToArray())
                    .Where(arr => arr.Length > 2);

                List<InjectableProcess> procs = new();
                foreach (string[] splitLine in splitLines)
                {
                    int pid = int.Parse(splitLine[0]);
                    string name = splitLine[1];
                    string runtimeVersion = splitLine[2]; // .NET version or "native" for C/C++/Unknown
                    string diverState = splitLine[3];
                    InjectableProcess proc = new(pid, name, runtimeVersion, diverState);

                    procs.Add(proc);
                }
                procsBox.ItemsSource = procs;
                procsBox.IsEnabled = true;
                //procsBoxLoadingOverlay.Visibility = Visibility.Collapsed;

                Keyboard.Focus(procsBox);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to refresh processes list.\nException: " + ex, this.Title, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        private void TargetSelectionWindow_Loaded(object sender, RoutedEventArgs e)
        {
            _ = RefreshProcessesListAsync();
        }

        private void ProcsRefreshButton_OnClick(object sender, RoutedEventArgs e)
        {
            _ = RefreshProcessesListAsync();
        }

        private void SaveSelectionAndExit()
        {
            // Get selected process
            if (procsBox.SelectedIndex == -1)
            {
                MessageBox.Show("Please select a process first.", this.Title, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            SelectedProcess = procsBox.SelectedItem as InjectableProcess;
            DialogResult = true;
            Close();
        }

        bool isAmbushing = false;

        private void selectButton_Click(object sender, RoutedEventArgs e)
        {
            if (_mode == TargetSelectionMode.Attach)
            {
                SaveSelectionAndExit();
            }
            else
            {
                // Ambush mode
                // Check if already ambushing
                if (isAmbushing)
                {
                    // Stop ambushing
                    isAmbushing = false;
                    modeButton.IsEnabled = true;
                    attachButton.Content = "Ambush";
                    ambushTargetName.IsEnabled = true;

                    // Revert "ambushStatus" text to "Not searching target."
                    ambushStatus.Text = "Not searching target.";
                    ambushStatus.Foreground = ambushStatusLabel.Foreground;
                }
                else
                {
                    // Start ambushing
                    string targetName = ambushTargetName.Text;
                    if (string.IsNullOrWhiteSpace(targetName))
                    {
                        MessageBox.Show("Please enter a process name to ambush.", this.Title, MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    isAmbushing = true;
                    // Disable mode change button
                    modeButton.IsEnabled = false;
                    attachButton.Content = "Stop";
                    // change status
                    ambushStatus.Text = "Searching for target...";
                    ambushStatus.Foreground = Brushes.Green;
                    _ = AmbushAsync(targetName);
                }
            }
        }

        private Task AmbushAsync(string targetName)
        {
            return Task.Run(() =>
            {
                while (isAmbushing)
                {
                    var processes = System.Diagnostics.Process.GetProcesses();
                    var targetProcesses = processes.Where(process => process.ProcessName.Contains(targetName)).ToList();
                    if (targetProcesses.Count == 1)
                    {
                        var process = targetProcesses.First();
                        // Process found
                        SelectedProcess = GetProcessInfoAsync(process).Result;
                        Dispatcher.Invoke(() =>
                        {
                            DialogResult = true;
                            Close();
                        });
                        return;
                    }
                    else if (targetProcesses.Count > 1)
                    {
                        // Show error in "ambushStatus"
                        Dispatcher.Invoke(() =>
                        {
                            ambushStatus.Text = $"Found {targetProcesses.Count} processes, expected 1";
                            ambushStatus.Foreground = Brushes.Red;
                        });
                    }

                    System.Threading.Thread.Sleep(1000); // Check every 1 second
                }
            });
        }

        private async Task<InjectableProcess> GetProcessInfoAsync(System.Diagnostics.Process process)
        {
            CommandTask<BufferedCommandResult> commandTask = CliWrap.Cli.Wrap("rnet-ps.exe").ExecuteBufferedAsync();
            BufferedCommandResult runResults = await commandTask.Task;
            IEnumerable<string[]> splitLines = runResults.StandardOutput.Split('\n')
                .Skip(1)
                .ToList()
                .Select(line => line.Split('\t', StringSplitOptions.TrimEntries).ToArray())
                .Where(arr => arr.Length > 2);

            foreach (string[] splitLine in splitLines)
            {
                int pid = int.Parse(splitLine[0]);
                if (pid != process.Id)
                {
                    continue;
                }
                string name = splitLine[1];
                string runtimeVersion = splitLine[2];
                string diverState = splitLine[3];
                return new InjectableProcess(process.Id, process.ProcessName, runtimeVersion, diverState);
            }
            throw new Exception($"Process with PID={process.Id} not found in rnet-ps output.");
        }

        private void HandleProcessDoubleClick(object sender, InjectableProcess e)
        {
            SelectedProcess = e;
            DialogResult = true;
            Close();
        }

        private void procsBox_KeyDown(object sender, KeyEventArgs e)
        {
            // Enter works only in Attach mode
            if (e.Key == Key.Enter && _mode == TargetSelectionMode.Attach)
            {
                SaveSelectionAndExit();
            }
        }

        private void cancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }


        private void modeButton_Click(object sender, RoutedEventArgs e)
        {
            _mode = _mode == TargetSelectionMode.Attach ? TargetSelectionMode.Ambush : TargetSelectionMode.Attach;
            modeButton.Content = _mode == TargetSelectionMode.Ambush ? "Attach Mode" : "Ambush Mode";
            // Show "procsRefreshButton" only in Attach mode
            procsRefreshButton.Visibility = _mode == TargetSelectionMode.Attach ? Visibility.Visible : Visibility.Collapsed;

            // Switch name on attach button
            attachButton.Content = _mode == TargetSelectionMode.Attach ? "Attach" : "Ambush";

            // Swap visibility of "ambushDockPanel" and "attachDocPanel" according to mode
            ambushDockPanel.Visibility = _mode == TargetSelectionMode.Ambush ? Visibility.Visible : Visibility.Collapsed;
            attachDockPanel.Visibility = _mode == TargetSelectionMode.Attach ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}
