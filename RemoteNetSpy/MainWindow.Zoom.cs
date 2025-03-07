using CSharpRepl.Services.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace RemoteNetSpy
{
    public partial class MainWindow
    {
        private object _zoomLock = new object();
        private int _zoomLevel = 0;
        private HashSet<Type> _forbiddens = new HashSet<Type>();

        private void OnKeyDown_DoZoom(object sender, KeyEventArgs e)
        {
            lock (_zoomLock)
            {
                if ((Keyboard.Modifiers & ModifierKeys.Control) != ModifierKeys.Control)
                    return;
                bool scaleUp = (e.Key == Key.Add || e.Key == Key.OemPlus);
                bool scaleDown = (e.Key == Key.Subtract || e.Key == Key.OemMinus);
                if (!scaleUp && !scaleDown)
                    return;
                ChangeAllFontSizes(scaleUp);
                e.Handled = true;
            }

            void ChangeAllFontSizes(bool up)
            {
                if (up)
                {
                    _zoomLevel++;
                }
                else
                {
                    // Make sure we're not scaling down below the default size
                    if (_zoomLevel == 0)
                        return;
                    _zoomLevel--;
                }

                var allElements = WindowElementEnumerator.EnumerateAllElementsInWindow(this); // 'this' refers to your Window instance
                var _newValues = new Dictionary<FrameworkElement, double>();
                foreach (FrameworkElement element in allElements)
                {
                    Type t = element.GetType();
                    if (_forbiddens.Contains(t))
                        continue;
                    if (t.GetMembers().All(member => member.Name != "FontSize"))
                    {
                        _forbiddens.Add(t);
                        continue;
                    }

                    if (element.IsPropertyBound("FontSize"))
                        continue;

                    if (element.HasAncestorWithName("titlebar"))
                        continue;

                    _newValues[element] = element.Steal<double>("FontSize") + (up ? 2 : (-2));
                }

                foreach (var kvp in _newValues)
                {
                    FrameworkElement element = kvp.Key;
                    element.SetMember("FontSize", kvp.Value);
                }
            }
        }

    }
}
