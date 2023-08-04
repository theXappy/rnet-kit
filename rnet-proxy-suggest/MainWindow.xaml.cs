using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Hardcodet.Wpf.TaskbarNotification; // Import the library


namespace rnet_proxy_suggest
{
    public partial class MainWindow : Window
    {
        private readonly string queryProcsPath = @"rnet-ps.exe";
        private readonly int intervalInSeconds = 5;
        private DateTime lastCheckTime = DateTime.MinValue;
        private HashSet<int> knownProcessIds = new HashSet<int>();
        private object listBoxLock = new object();

        private TaskbarIcon notifyIcon;
        private System.Windows.Controls.ContextMenu contextMenu;

        public MainWindow()
        {
            InitializeComponent();
            StartBackgroundProcessMonitor();

            // Initialize the tray icon and context menu
            InitializeTrayIcon();
        }

        private void InitializeTrayIcon()
        {
            notifyIcon = new TaskbarIcon();
            notifyIcon.Icon = new System.Drawing.Icon("app.ico"); // Set your icon here

            // Create context menu for the tray icon
            contextMenu = new System.Windows.Controls.ContextMenu();

            // Add "Exit" menu item to exit the application
            System.Windows.Controls.MenuItem exitMenuItem = new System.Windows.Controls.MenuItem();
            exitMenuItem.Header = "Exit";
            exitMenuItem.Click += ExitMenuItem_Click;
            contextMenu.Items.Add(exitMenuItem);

            // Set the context menu for the tray icon
            notifyIcon.ContextMenu = contextMenu;
        }

        private void ExitMenuItem_Click(object sender, RoutedEventArgs e)
        {
            // Clean up and exit the application
            notifyIcon.Dispose();
            System.Windows.Application.Current.Shutdown();
        }

        private void StartBackgroundProcessMonitor()
        {
            Thread backgroundThread = new Thread(() =>
            {
                while (true)
                {
                    try
                    {
                        if ((DateTime.Now - lastCheckTime).TotalSeconds >= intervalInSeconds)
                        {
                            lastCheckTime = DateTime.Now;
                            HashSet<int> newProcessIds = GetProcessesWithDiver(queryProcsPath);
                            UpdateListBoxWithNewProcesses(newProcessIds);
                            knownProcessIds = newProcessIds;
                        }
                    }
                    catch (Exception ex)
                    {
                        // You can handle any errors here if needed.
                    }

                    Thread.Sleep(1000);
                }
            });

            backgroundThread.IsBackground = true;
            backgroundThread.Start();
        }


        static HashSet<int> GetProcessesWithDiver(string queryProcsPath)
        {
            Process queryProcs = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = queryProcsPath,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            queryProcs.Start();

            List<string> lines = new List<string>();
            while (true)
            {
                string line = queryProcs.StandardOutput.ReadLine();
                if (string.IsNullOrWhiteSpace(line))
                    break;
                lines.Add(line);
            }
            queryProcs.WaitForExit();

            HashSet<int> processIdsWithDiver = new HashSet<int>();
            foreach (string line in lines)
            {
                if (line.Contains("Diver Injected"))
                {
                    string[] parts = line.Split('\t');
                    if (parts.Length >= 2 && int.TryParse(parts[0], out int processId))
                    {
                        processIdsWithDiver.Add(processId);
                    }
                }
            }

            return processIdsWithDiver;
        }


        private void UpdateListBoxWithNewProcesses(HashSet<int> newProcessIds)
        {
            lock (listBoxLock)
            {
                IEnumerable<int> newProcesses = newProcessIds.Except(knownProcessIds);
                foreach (int processId in newProcesses)
                {
                    try
                    {
                        Process process = Process.GetProcessById(processId);
                        string processInfo = $"PID: {processId}, Name: {process.ProcessName}";
                        Dispatcher.Invoke(() =>
                        {
                            processListBox.Items.Add(processInfo);
                            ShowProcessButton(new ProcessInfo()
                            { ProcessId = processId, ProcessName = process.ProcessName });
                        });
                    }
                    catch (ArgumentException)
                    {
                        // The process might have terminated before we could retrieve its details.
                    }
                }
            }
        }


        private void ShowProcessButton(ProcessInfo processInfo)
        {

            // Create a button for the process and add it to the context menu
            MenuItem processMenuItem = new MenuItem()
            {
                Header = $"{processInfo.ProcessName} (PID: {processInfo.ProcessId})",
            };
            processMenuItem.Click += ProcessMenuItemOnClick;
            contextMenu.Items.Add(processMenuItem);

            if (contextMenu.Items.Count == 2)
            {
                // First app with diver (discluding 'exif' menu item)
                ProcessMenuItemOnClick(processMenuItem, null);
            }
        }

        private void ProcessMenuItemOnClick(object sender, RoutedEventArgs e)
        {
            string currentDirectory = Environment.CurrentDirectory;
            string iconFilePath = Path.Combine(currentDirectory, "ok.ico");

            BitmapImage iconImage = new BitmapImage();
            iconImage.BeginInit();
            iconImage.UriSource = new Uri(iconFilePath);
            iconImage.EndInit();

            foreach (var item in contextMenu.Items)
            {
                (item as MenuItem).Icon = null;
            }


            // Create a new Image and set its properties
            Image icon = new Image
            {
                Source = iconImage,
                Stretch = System.Windows.Media.Stretch.Uniform // or Stretch.UniformToFill
            };

            // Create a StackPanel to hold the Image
            StackPanel iconContainer = new StackPanel();
            iconContainer.Children.Add(icon);
            (sender as MenuItem).Icon = iconContainer;

        }
    }

    // Helper class to store process information
    public class ProcessInfo
    {
        public int ProcessId { get; set; }
        public string ProcessName { get; set; }
    }
}
