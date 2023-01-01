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
    }
}
