using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace RemoteNetSpy
{
    public static class VisualTreeHelperExtensions
    {
        public static IEnumerable<DependencyObject> EnumerateAllVisualChildren(this DependencyObject parent)
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                yield return child;

                foreach (var visualChild in EnumerateAllVisualChildren(child))
                {
                    yield return visualChild;
                }
            }
        }
    }
}
