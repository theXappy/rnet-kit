using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace RemoteNetSpy.Controls
{
    public class SearchableContextMenu : ContextMenu
    {
        private string _searchBuffer = string.Empty;
        private int _lastFoundIndex = -1;

        static SearchableContextMenu()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(SearchableContextMenu), 
                new FrameworkPropertyMetadata(typeof(ContextMenu)));
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.Key >= Key.A && e.Key <= Key.Z || e.Key >= Key.D0 && e.Key <= Key.D9)
            {
                // Convert the key to a character
                char keyChar = e.Key.ToString().ToLower()[0];
                
                // If the same key is pressed, continue searching from the last found position
                if (_searchBuffer.Length == 1 && _searchBuffer[0] == keyChar)
                {
                    // Same key pressed again, search for next match
                    FindAndSelectNext(keyChar);
                }
                else
                {
                    // Different key or first key press
                    _searchBuffer = keyChar.ToString();
                    _lastFoundIndex = -1;
                    FindAndSelectNext(keyChar);
                }

                e.Handled = true;
                return;
            }

            base.OnKeyDown(e);
        }

        private void FindAndSelectNext(char searchChar)
        {
            if (Items.Count == 0)
                return;

            int startIndex = _lastFoundIndex + 1;
            
            // Search through the data items directly (not MenuItem containers)
            // This works even with virtualization since Items contains all data objects
            for (int i = startIndex; i < Items.Count; i++)
            {
                if (DataItemStartsWith(Items[i], searchChar))
                {
                    SelectDataItem(Items[i], i);
                    _lastFoundIndex = i;
                    return;
                }
            }

            // Wraparound: search from the beginning to the start position
            for (int i = 0; i < startIndex; i++)
            {
                if (DataItemStartsWith(Items[i], searchChar))
                {
                    SelectDataItem(Items[i], i);
                    _lastFoundIndex = i;
                    return;
                }
            }
        }

        private IEnumerable<MenuItem> GetMenuItems()
        {
            int foundCount = 0;
            // When ItemsSource is bound, Items contains data objects, not MenuItems
            // We need to get the generated MenuItem containers
            for (int i = 0; i < Items.Count; i++)
            {
                var item = Items[i];
                
                // Try to get the container (MenuItem) for this data item
                var container = ItemContainerGenerator.ContainerFromItem(item) as MenuItem;
                if (container != null)
                {
                    foundCount++;
                    yield return container;
                }
            }
        }

        private bool DataItemStartsWith(object dataItem, char searchChar)
        {
            string name = null;
            
            // Check if the data item is a MethodInfo (which includes RemoteRttiMethodInfo)
            if (dataItem is System.Reflection.MethodInfo methodInfo)
            {
                name = methodInfo.Name;
            }
            else
            {
                name = dataItem?.ToString();
            }
            
            if (string.IsNullOrEmpty(name))
                return false;

            return char.ToLower(name[0]) == char.ToLower(searchChar);
        }

        private void SelectDataItem(object dataItem, int index)
        {
            // Check if the item is already visible before scrolling
            bool needsScroll = NeedsScrolling(index);
            if (needsScroll)
            {
                // Scroll item into view
                ScrollIntoView(index);
            }
            
            // Try to get container immediately if item is already visible
            var container = ItemContainerGenerator.ContainerFromIndex(index) as MenuItem;
            if (container != null)
            {
                container.Focus();
                // Only bring into view if we didn't scroll (for fine-tuning position)
                if (!needsScroll)
                {
                    container.BringIntoView();
                }
                return;
            }
            
            // If container not available yet, wait for it to be generated after scroll
            if (needsScroll)
            {
                Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                {
                    UpdateLayout();
                    
                    var container2 = ItemContainerGenerator.ContainerFromIndex(index) as MenuItem;
                    if (container2 != null)
                        container2.Focus();
                }));
            }
        }

        // Check if an item at given index is currently visible in the viewport
        private bool NeedsScrolling(int index)
        {
            var scrollViewer = FindScrollViewer(this);
            if (scrollViewer == null)
                return true;
            
            // Calculate which items are currently visible
            double itemHeight = scrollViewer.ExtentHeight / Items.Count;
            double currentOffset = scrollViewer.VerticalOffset;
            double viewportHeight = scrollViewer.ViewportHeight;
            
            // Calculate first and last visible indices (with some margin)
            int firstVisibleIndex = (int)(currentOffset / itemHeight);
            int lastVisibleIndex = (int)((currentOffset + viewportHeight) / itemHeight);
            
            // Add margin of 2 items for safety
            firstVisibleIndex = Math.Max(0, firstVisibleIndex - 2);
            lastVisibleIndex = Math.Min(Items.Count - 1, lastVisibleIndex + 2);
            
            bool isVisible = index >= firstVisibleIndex && index <= lastVisibleIndex;
            
            return !isVisible;
        }

        // Helper method to scroll an item into view by index
        private void ScrollIntoView(int index)
        {
            var scrollViewer = FindScrollViewer(this);
            if (scrollViewer == null)
                return;
            
            // Calculate item height
            double itemHeight = scrollViewer.ExtentHeight / Items.Count;
            double currentOffset = scrollViewer.VerticalOffset;
            double viewportHeight = scrollViewer.ViewportHeight;
            
            // Calculate target position - just ensure it's visible, don't center
            double itemTop = index * itemHeight;
            double itemBottom = itemTop + itemHeight;
            
            double targetOffset = currentOffset;
            
            // If item is above viewport, scroll up to show it at top
            if (itemTop < currentOffset)
            {
                targetOffset = itemTop;
            }
            // If item is below viewport, scroll down to show it at bottom
            else if (itemBottom > currentOffset + viewportHeight)
            {
                targetOffset = itemBottom - viewportHeight;
            }
            else
            {
                return; // Item is already visible
            }
            
            // Clamp to valid range
            targetOffset = Math.Max(0, Math.Min(targetOffset, scrollViewer.ScrollableHeight));
            scrollViewer.ScrollToVerticalOffset(targetOffset);
            scrollViewer.UpdateLayout();
        }

        // Helper to find ScrollViewer in visual tree
        private ScrollViewer FindScrollViewer(DependencyObject parent)
        {
            if (parent == null) return null;
            
            int childCount = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < childCount; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                
                if (child is ScrollViewer scrollViewer)
                {
                    return scrollViewer;
                }
                
                var result = FindScrollViewer(child);
                if (result != null)
                {
                    return result;
                }
            }
            
            return null;
        }
    }
}
