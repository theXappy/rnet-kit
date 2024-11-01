using RemoteNetSpy.Controls;
using System.Windows;

namespace RemoteNetSpy
{
    public partial class MemoryViewWindow : Window
    {
        MemoryViewPanelModel _model => DataContext as MemoryViewPanelModel;

        public MemoryViewWindow(RemoteAppModel raModel, ulong? address = null)
        {
            InitializeComponent();

            DataContext = new MemoryViewPanelModel(raModel);
            memoryViewPanel.DataContext = _model;
            if (address != null)
            {
                _model.Address = address.Value;
            }
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            if (_model.Address != 0)
            {
                memoryViewPanel.LoadBytesAsync();
            }
        }
    }
}
