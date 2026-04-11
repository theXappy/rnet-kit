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
        private DateTime _lastKeyPressTime = DateTime.MinValue;
        private const int SearchTimeoutMs = 500;

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
                
                // Check if enough time has elapsed since last keypress
                var now = DateTime.Now;
                var elapsed = (now - _lastKeyPressTime).TotalMilliseconds;
                _lastKeyPressTime = now;
                
                if (elapsed > SearchTimeoutMs || string.IsNullOrEmpty(_searchBuffer))
                {
                    // Timeout elapsed or first key press
                    // Check if it's the same key - if so, continue cycling instead of resetting
                    if (_searchBuffer.Length == 1 && _searchBuffer[0] == keyChar)
                    {
                        // Same key after timeout - continue cycling through matches
                        FindAndSelectNext(_searchBuffer);
                    }
                    else
                    {
                        // Different key or first key press - start new search
                        _searchBuffer = keyChar.ToString();
                        _lastFoundIndex = -1;
                        FindAndSelectNext(_searchBuffer);
                    }
                }
                else if (_searchBuffer.Length == 1 && _searchBuffer[0] == keyChar)
                {
                    // Same single key pressed again quickly - search for next match
                    FindAndSelectNext(_searchBuffer);
                }
                else
                {
                    // Different key pressed within timeout - append to search buffer
                    _searchBuffer += keyChar;
                    _lastFoundIndex = -1;
                    FindAndSelectNext(_searchBuffer);
                }

                e.Handled = true;
                return;
            }

            base.OnKeyDown(e);
        }

        private void FindAndSelectNext(string searchString)
        {
            if (Items.Count == 0 || string.IsNullOrEmpty(searchString))
                return;

            int startIndex = _lastFoundIndex + 1;
            
            // Search through the data items directly (not MenuItem containers)
            // This works even with virtualization since Items contains all data objects
            for (int i = startIndex; i < Items.Count; i++)
            {
                if (DataItemStartsWith(Items[i], searchString))
                {
                    SelectDataItem(Items[i], i);
                    _lastFoundIndex = i;
                    return;
                }
            }

            // Wraparound: search from the beginning to the start position
            for (int i = 0; i < startIndex; i++)
            {
                if (DataItemStartsWith(Items[i], searchString))
                {
                    SelectDataItem(Items[i], i);
                    _lastFoundIndex = i;
                    return;
                }
            }
        }

        private bool DataItemStartsWith(object dataItem, string searchString)
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
            
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(searchString))
                return false;

            return name.StartsWith(searchString, StringComparison.OrdinalIgnoreCase);
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
