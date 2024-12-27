using RemoteNetSpy.Models;
using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

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
        public MemoryViewPanel()
        {
            InitializeComponent();
        }

        private async void GoButtonClicked(object sender, RoutedEventArgs e) => LoadBytesAsync();

        public async Task LoadBytesAsync()
        {
            using (fetchSpinner.TemporarilyShow())
            {
                var mvpModel = DataContext as MemoryViewPanelModel;
                if (mvpModel == null)
                {
                    MessageBox.Show("DataContext is not a RemoteAppModel");
                    return;
                }

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
                    return;
                }

                MemoryStream ba = new MemoryStream(temp);
                myHexEditor.Stream = ba;
            }
        }
    }
}
