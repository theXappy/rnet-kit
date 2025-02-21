using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using CliWrap;
using CliWrap.Buffered;
using RemoteNetSpy.Models;

namespace RemoteNetSpy.Windows
{
    public partial class TargetSelectionWindow : Window
    {
        public InjectableProcess SelectedProcess { get; private set; }

        public TargetSelectionWindow()
        {
            InitializeComponent();
            Loaded += TargetSelectionWindow_Loaded;
        }

        private async void TargetSelectionWindow_Loaded(object sender, RoutedEventArgs e)
        {
            await RefreshProcessesList();
        }

        private async Task RefreshProcessesList()
        {
            procsBox.ItemsSource = null;
            procsBox.IsEnabled = false;
            procsBoxLoadingOverlay.Visibility = Visibility.Visible;

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
                procsBoxLoadingOverlay.Visibility = Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to refresh processes list.\nException: " + ex, this.Title, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ProcsBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (procsBox.SelectedIndex == -1)
                return;

            SelectedProcess = procsBox.SelectedItem as InjectableProcess;
            DialogResult = true;
            Close();
        }

        private async void ProcsRefreshButton_OnClick(object sender, RoutedEventArgs e)
        {
            await RefreshProcessesList();
        }
    }
}
