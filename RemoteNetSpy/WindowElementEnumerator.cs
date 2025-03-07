using System.Collections.Generic;
using System.Windows;

namespace RemoteNetSpy
{
    public static class WindowElementEnumerator
    {
        public static IEnumerable<FrameworkElement> EnumerateAllElementsInWindow(Window window)
        {
            foreach (var child in window.EnumerateAllVisualChildren())
            {
                if (child is FrameworkElement frameworkElement)
                {
                    yield return frameworkElement;
                }
            }
        }
    }
}
