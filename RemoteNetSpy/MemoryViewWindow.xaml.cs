using RemoteNetSpy.Controls;
using System.Windows;

namespace RemoteNetSpy
{
    public partial class MemoryViewWindow : Window
    {
        public MemoryViewWindow(RemoteAppModel raModel, ulong? address = null)
        {
            InitializeComponent();

            var model = new MemoryViewPanelModel(raModel);
            memoryViewPanel.DataContext = model;
            if (address != null)
            {
                model.Address = address.Value;
                memoryViewPanel.LoadBytes();
            }
        }
    }
}
