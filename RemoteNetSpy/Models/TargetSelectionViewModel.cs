using RemoteNetSpy.Models;
using System;
using System.Windows.Input;

namespace RemoteNetSpy.ViewModels
{
    public class TargetSelectionViewModel
    {
        public event EventHandler<InjectableProcess> ItemDoubleClick;

        public ICommand ItemDoubleClickCommand { get; }

        public TargetSelectionViewModel()
        {
            ItemDoubleClickCommand = new RelayCommand<InjectableProcess>(OnItemDoubleClick);
        }

        private void OnItemDoubleClick(InjectableProcess process)
        {
            ItemDoubleClick?.Invoke(this, process);
        }
    }
}
