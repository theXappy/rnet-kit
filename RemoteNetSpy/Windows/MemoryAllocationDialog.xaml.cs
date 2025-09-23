using RemoteNET;
using System;
using System.Windows;

namespace RemoteNetSpy
{
    public partial class MemoryAllocationDialog : Window
    {
        private readonly RemoteApp _remoteApp;
        public IntPtr AllocatedAddress { get; private set; }

        public MemoryAllocationDialog(RemoteApp remoteApp)
        {
            InitializeComponent();
            _remoteApp = remoteApp;
        }

        private void AllocateButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!int.TryParse(SizeTextBox.Text, out int size) || size <= 0)
                {
                    StatusTextBlock.Text = "Please enter a valid positive number for size.";
                    return;
                }

                if (size > 1024 * 1024 * 100) // 100MB limit
                {
                    StatusTextBlock.Text = "Size is too large. Maximum allowed is 100MB.";
                    return;
                }

                StatusTextBlock.Text = "Allocating memory in target process...";

                // Use RemoteMarshal to allocate memory
                if (ZeroMemoryCheckBox.IsChecked == true)
                {
                    AllocatedAddress = _remoteApp.Marshal.AllocHGlobalZero(size);
                }
                else
                {
                    AllocatedAddress = _remoteApp.Marshal.AllocHGlobal(size);
                }

                StatusTextBlock.Text = $"Successfully allocated {size} bytes at address 0x{AllocatedAddress.ToInt64():X16}";

                DialogResult = true;
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = $"Failed to allocate memory: {ex.Message}";
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}