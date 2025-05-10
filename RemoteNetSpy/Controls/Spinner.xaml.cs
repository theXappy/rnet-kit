using System;
using System.Windows.Controls;

namespace RemoteNetSpy
{
    /// <summary>
    /// Interaction logic for Spinner.xaml
    /// </summary>
    public partial class Spinner : UserControl
    {
        private bool _hideBackground = false;
        public bool HideBackground
        {
            set
            {
                _hideBackground = value;
                if (_hideBackground == true)
                {
                    panel.Background.Opacity = 0;
                }
            }
            get
            {
                return _hideBackground;
            }
        }
        public Spinner()
        {
            InitializeComponent();
        }

        public IDisposable TemporarilyShow()
        {
            return new SpinnerShower(this);
        }

        private class SpinnerShower : IDisposable
        {
            private Spinner _spinner;
            public SpinnerShower(Spinner spinner)
            {
                _spinner = spinner;
                _ = _spinner.Dispatcher.InvokeAsync(() => { _spinner.Visibility = System.Windows.Visibility.Visible; });
            }

            public void Dispose()
            {
                _ = _spinner.Dispatcher.InvokeAsync(() => { _spinner.Visibility = System.Windows.Visibility.Collapsed; });
            }
        }
    }
}
