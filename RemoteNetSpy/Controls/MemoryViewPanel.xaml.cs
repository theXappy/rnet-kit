using RemoteNetSpy.Models;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace RemoteNetSpy.Controls
{
    public class ULongToHexConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value is ulong address)
            {
                return $"0x{address:X16}"; // Format as hex with leading 0x and 16 digits
            }
            return DependencyProperty.UnsetValue;
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            string input = value as string;
            if (input == null) return DependencyProperty.UnsetValue;

            if (input.Contains(" "))
            {
                // Check for Little Endian format (e.g. "88 77 66 55 44 33 22 11")
                string[] byteStrings = input.Split(' ');
                if (byteStrings.All(section => section.Length == 2) && byteStrings.Length <= 8)
                {
                    try
                    {
                        byte[] bytes = byteStrings.Select(b => System.Convert.ToByte(b, 16)).ToArray();

                        // Pad to 8
                        byte[] temp = new byte[8];
                        Array.Copy(bytes, 0, temp, 0, bytes.Length);
                        bytes = temp;

                        return BitConverter.ToUInt64(bytes, 0); // Convert back from Little Endian to long
                    }
                    catch
                    {
                        return DependencyProperty.UnsetValue;
                    }
                }
                // Check for Windows Calculator format (e.g. "1D23 ABCD EF87")
                else if (byteStrings.Any(section => section.Length == 4))
                {
                    try
                    {
                        string normalized = input.Replace(" ", string.Empty);
                        if (ulong.TryParse(normalized, System.Globalization.NumberStyles.HexNumber, null, out ulong result))
                        {
                            return result;
                        }
                    }
                    catch
                    {
                        return DependencyProperty.UnsetValue;
                    }
                }

            }
            else if (input.StartsWith("0x")) // Check for 0x format
            {
                input = input.Substring(2); // Remove "0x" prefix
                if (ulong.TryParse(input, System.Globalization.NumberStyles.HexNumber, null, out ulong result))
                {
                    return result;
                }
            }

            return DependencyProperty.UnsetValue;
        }
    }
    public class IntConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value is int address)
            {
                return address.ToString();
            }
            return DependencyProperty.UnsetValue;
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            string input = value as string;
            if (input == null) return DependencyProperty.UnsetValue;

            if (int.TryParse(input, out int result))
            {
                return result;
            }

            return DependencyProperty.UnsetValue;
        }
    }


    public class MemoryViewPanelModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private ulong _address;
        public ulong Address
        {
            get { return _address; }
            set
            {
                _address = value;
                OnPropertyChanged(nameof(Address)); // Implement INotifyPropertyChanged in your ViewModel
            }
        }

        private int _size;

        public int Size
        {
            get { return _size; }
            set
            {
                _size = value;
                OnPropertyChanged(nameof(Size)); // Implement INotifyPropertyChanged in your ViewModel
            }
        }

        private Type _detectedType;
        public Type DetectedType
        {
            get { return _detectedType; }
            set
            {
                _detectedType = value;
                OnPropertyChanged(nameof(DetectedType));
            }
        }

        public RemoteAppModel RemoteAppModel { get; set; }

        public MemoryViewPanelModel(RemoteAppModel remoteAppModel)
        {
            RemoteAppModel = remoteAppModel;
            Size = 240;
        }

    }

    /// <summary>
    /// Interaction logic for MemoryViewPanel.xaml
    /// </summary>
    public partial class MemoryViewPanel : UserControl
    {
        private System.Windows.Threading.DispatcherTimer _refreshTimer;
        private bool _isLoadingBytes = false;
        private bool _autoRefreshEnabled = false;
        private static readonly TimeSpan DefaultRefreshInterval = TimeSpan.FromSeconds(1);
        private byte[] _lastLoadedBuffer = null;
        private int _highlightsCountdown;

        public bool AutoRefreshEnabled
        {
            get => _autoRefreshEnabled;
            set
            {
                _autoRefreshEnabled = value;
                if (value)
                    StartAutoRefresh();
                else
                    StopAutoRefresh();
            }
        }

        public MemoryViewPanel()
        {
            InitializeComponent();
            InitializeTimer();
        }

        private void InitializeTimer()
        {
            _refreshTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = DefaultRefreshInterval
            };
            _refreshTimer.Tick += async (s, e) => await TryRefreshBytes();
        }

        private void StartAutoRefresh()
        {
            if (!_refreshTimer.IsEnabled)
                _refreshTimer.Start();
        }

        private void StopAutoRefresh()
        {
            if (_refreshTimer.IsEnabled)
                _refreshTimer.Stop();
        }

        private async Task TryRefreshBytes()
        {
            // If we're already loading bytes, don't start another operation
            if (_isLoadingBytes)
                return;

            _isLoadingBytes = true;
            try
            {
                await LoadBytesAsync();
            }
            finally
            {
                _isLoadingBytes = false;
            }
        }

        ulong currentlyShownAddress = 0;
        int currentlShownSize = 0;

        int updater = 1;
        public async Task LoadBytesAsync()
        {
            Debug.WriteLine($"{DateTime.Now.ToLongTimeString()} Boop!");
            // Atomicly increase the updater and keep the old value as our "id"
            int id = System.Threading.Interlocked.Increment(ref updater);
            // Sleep to let "Address" update
            await Task.Delay(100);
            // If the updater has changed since we started, return
            if (updater != id)
                return;

            var mvpModel = DataContext as MemoryViewPanelModel;
            if (mvpModel == null)
            {
                MessageBox.Show("DataContext is not a RemoteAppModel");
                StopAutoRefresh(); // Stop refresh on error
                return;
            }

            if (mvpModel.Address == 0)
                return;

            //using (fetchSpinner.TemporarilyShow())
            {
                // Debug print that we just starting THE REAL DEAL:
                Debug.WriteLine($"{DateTime.Now.ToLongTimeString()} Started the real deal");

                byte[] temp = new byte[mvpModel.Size];
                try
                {
                    RemoteNET.RemoteMarshal marshal = mvpModel.RemoteAppModel.App.Marshal;
                    await Task.Run(() =>
                        marshal.Read((nint)mvpModel.Address, temp, 0, mvpModel.Size));
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message);
                    StopAutoRefresh(); // Stop refresh on error
                    return;
                }

                try
                {
                    // read successful. Start Task to figure out which type we're looing at
                    await Task.Run(() =>
                    {
                        long vftableAddress = BitConverter.ToInt64(temp, 0);
                        Type objType = mvpModel.RemoteAppModel.App.GetRemoteType(vftableAddress);
                        mvpModel.DetectedType = objType;
                    });
                }
                catch
                {
                    // Ignored
                }

                bool anyChanged = _lastLoadedBuffer == null || !_lastLoadedBuffer.SequenceEqual(temp);
                if (anyChanged)
                {
                    MemoryStream ba = new MemoryStream(temp);
                    myHexEditor.Stream = ba;
                    // Update "current" vars
                    currentlyShownAddress = mvpModel.Address;
                    currentlShownSize = mvpModel.Size;
                    StartAutoRefresh();
                }

                // Highlight diff bytes
                _highlightsCountdown--;
                if (_highlightsCountdown < 1)
                {
                    myHexEditor.UnHighLightAll();
                }
                if (anyChanged && _lastLoadedBuffer != null)
                {
                    for (int i = 0; i < temp.Length; i++)
                    {
                        if (temp[i] != _lastLoadedBuffer[i])
                            myHexEditor.AddHighLight(i, 1, updateVisual: false);
                    }
                    myHexEditor.AddHighLight(1, 0, updateVisual: true);
                    _highlightsCountdown = 10;
                }
                _lastLoadedBuffer = (byte[])temp.Clone();
                Debug.WriteLine($"{DateTime.Now.ToLongTimeString()} Ended the real deal");
            }
        }

        private void InspectObjectClicked(object sender, RoutedEventArgs e)
        {
            _ = Task.Run(InspectObject);
        }
        private void InspectObject()
        {
            using (fetchSpinner.TemporarilyShow())
            {
                MemoryViewPanelModel mvpModel = null;
                Dispatcher.Invoke(() => { mvpModel = DataContext as MemoryViewPanelModel; });
                if (mvpModel == null)
                {
                    MessageBox.Show("DataContext is not a RemoteAppModel");
                    return;
                }

                try
                {
                    Type objType = mvpModel.DetectedType;
                    if (objType == null)
                    {
                        MessageBox.Show("Failed to get type from vftable");
                        return;
                    }

                    var app = mvpModel.RemoteAppModel.App;
                    app.Communicator.StartOffensiveGC(objType.Assembly.GetName().FullName);

                    // Get remote object from address + type
                    RemoteNET.RemoteObject obj = app.GetRemoteObject(mvpModel.Address, objType.FullName);

                    Dispatcher.Invoke(() =>
                    {
                        Window ownerWindow = Window.GetWindow(this);
                        Window objectViewer = ObjectViewer.CreateViewerWindow(ownerWindow, mvpModel.RemoteAppModel, obj);
                        objectViewer.Show();
                    });
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message);
                    return;
                }
            }
        }

        private void UserControl_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            // Cast DataContext to MemoryViewPanelModel
            MemoryViewPanelModel mvpModel = DataContext as MemoryViewPanelModel;
            if (mvpModel == null)
            {
                MessageBox.Show("DataContext is not a MemoryViewPanelModel");
                return;
            }
            // register to changes on the "Address" property of the model
            mvpModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == "Address")
                {
                    _ = LoadBytesAsync();
                }
            };
        }

        private void TextBox_KeyEnterUpdate(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                TextBox tBox = (TextBox)sender;
                DependencyProperty prop = TextBox.TextProperty;

                BindingExpression binding = BindingOperations.GetBindingExpression(tBox, prop);
                if (binding != null) { binding.UpdateSource(); }
            }
        }

        private void Bytes8Button_Click(object sender, RoutedEventArgs e) => SetBytesPerLine(8);

        private void Bytes16Button_Click(object sender, RoutedEventArgs e) => SetBytesPerLine(16);

        private void SetBytesPerLine(int bytesPerLine)
        {
            myHexEditor.BytePerLine = bytesPerLine;
            if (bytesPerLine == 16)
            {
                bytes16Button.Background = Brushes.Gray;
                bytes8Button.Background = Brushes.Transparent;
            }
            else
            {
                bytes16Button.Background = Brushes.Transparent;
                bytes8Button.Background = Brushes.LightGray;
            }
        }
    }
}
