using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CliWrap;
using CliWrap.Buffered;
using RemoteNetSpy.Models;
using RemoteNetSpy.ViewModels;

namespace RemoteNetSpy.Windows
{
    public partial class TargetSelectionWindow : Window
    {
        public InjectableProcess SelectedProcess { get; private set; }

        public TargetSelectionWindow()
        {
            InitializeComponent();
            var dataContext = new TargetSelectionViewModel();
            dataContext.ItemDoubleClick += HandleProcessDoubleClick;
            DataContext = dataContext;
            Loaded += TargetSelectionWindow_Loaded;
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
        private void TargetSelectionWindow_Loaded(object sender, RoutedEventArgs e) => RefreshProcessesList();
        private void ProcsRefreshButton_OnClick(object sender, RoutedEventArgs e) => RefreshProcessesList();

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

        private void attachButton_Click(object sender, RoutedEventArgs e) => SaveSelectionAndExit();

        private void HandleProcessDoubleClick(object sender, InjectableProcess e)
        {
            SelectedProcess = e;
            DialogResult = true;
            Close();
        }

        private void procsBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                SaveSelectionAndExit();
            }
        }

        private void cancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
