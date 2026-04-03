using RemoteNetSpy.Controls.DragDropPlaygroundSubsystem;
using RemoteNetSpy.Models;
using RemoteNetSpy.Controls;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using RnetKit.Common;
using RemoteNET;

namespace DragDropExpressionBuilder
{
    /// <summary>
    /// Interaction logic for DragDropPlayground.xaml
    /// </summary>
    public partial class DragDropPlayground : UserControl
    {
        private PlaygroundViewModel _viewModel = new PlaygroundViewModel();
        private DroppedMethodItem _draggedItem;
        private Point _dragStartPoint;
        private bool _isDragging;
        private int _defaultDropOffsetIndex;

        private int _canvasNextZIndex = 0;

        public DragDropPlayground()
        {
            InitializeComponent();
            DataContext = _viewModel;
            ReservoirListBox.PreviewMouseMove += ReservoirListBox_PreviewMouseMove;
            MainAreaBorder.Drop += MainAreaBorder_Drop;
            MainAreaBorder.DragOver += MainAreaBorder_DragOver;
        }

        public void AddObject(object obj, string tag) => _viewModel.AddObject(obj, tag);
        public void AddObject(object obj, string tag, Type forcedType) => _viewModel.AddObject(obj, tag, forcedType);
        public void AddMethod(MethodInfo mi) => _viewModel.AddMethod(mi);
        public void AddHeapObject(HeapObjectViewModel heapObj) => _viewModel.AddHeapObject(heapObj);

        private void ReservoirListBox_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.OriginalSource is Button)
            {
                // Leave along clicks on the "methods list button"
                return;
            }

            if (e.LeftButton == MouseButtonState.Pressed)
            {
                var listBox = sender as ListBox;
                var item = listBox?.SelectedItem;
                if (item is MethodInfoWrapper methodWrapper)
                {
                    DragDrop.DoDragDrop(listBox, methodWrapper, DragDropEffects.Copy);
                }
                else if (item is Instance instance)
                {
                    DragDrop.DoDragDrop(listBox, instance, DragDropEffects.Copy);
                }
                else if (item is HeapObjectViewModel heapObj)
                {
                    DragDrop.DoDragDrop(listBox, heapObj, DragDropEffects.Copy);
                }
            }
        }

        private void MainAreaBorder_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(typeof(MethodInfoWrapper)) || e.Data.GetDataPresent(typeof(MethodInfo)))
            {
                return;
            }
            e.Effects = DragDropEffects.None;
            e.Handled = true;
        }

        private void MainAreaBorder_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(typeof(MethodInfoWrapper)))
            {
                var methodWrapper = e.Data.GetData(typeof(MethodInfoWrapper)) as MethodInfoWrapper;
                if (methodWrapper != null)
                {
                    var pos = e.GetPosition(MainAreaCanvas);
                    var invocation = new MethodInvocation(methodWrapper);
                    _viewModel.DroppedMethods.Add(new DroppedMethodItem(invocation, pos.X, pos.Y));
                }
            }
            else if (e.Data.GetDataPresent(typeof(MethodInfo)))
            {
                var method = e.Data.GetData(typeof(MethodInfo)) as MethodInfo;
                if (method != null)
                {
                    var pos = e.GetPosition(MainAreaCanvas);
                    var invocation = new MethodInvocation(new MethodInfoWrapper(method));
                    _viewModel.DroppedMethods.Add(new DroppedMethodItem(invocation, pos.X, pos.Y));
                }
            }
        }

        private void HeapObject_MouseEnter(object sender, MouseEventArgs e)
        {
            var stackPanel = sender as StackPanel;
            if (stackPanel?.DataContext is HeapObjectViewModel heapObj && !heapObj.Frozen)
            {
                // Do not show methods for unfrozen objects
                return;
            }

            if (stackPanel?.DataContext is HeapObjectViewModel heapObject && heapObject.RemoteObject != null)
            {
                var type = heapObject.RemoteObject.GetRemoteType();
                heapObject.SetTypeMethodsForDesign(new ObservableCollection<MethodInfo>(
                    type.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                        .Where(m => !m.IsSpecialName)
                ));
            }
        }

        private void HeapObject_MouseLeave(object sender, MouseEventArgs e)
        {
        }

        // Drag logic for DroppedMethodItem
        private void DroppedMethod_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var border = sender as Border;
            if (border?.DataContext is DroppedMethodItem item)
            {
                _draggedItem = item;
                // Bring the dragged item to the front
                var stackPanel = border.Parent as StackPanel;
                var container = MainAreaItemsControl.ItemContainerGenerator.ContainerFromItem(item) as UIElement;
                if (container != null)
                    Canvas.SetZIndex(container, _canvasNextZIndex++);
                // Dragging logic
                _dragStartPoint = e.GetPosition(MainAreaCanvas);
                _isDragging = true;
                border.CaptureMouse();
            }
        }

        private void DroppedMethod_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isDragging && _draggedItem != null)
            {
                var canvas = MainAreaCanvas;
                var pos = e.GetPosition(canvas);
                var border = sender as FrameworkElement;
                var stackPanel = border.Parent as FrameworkElement;
                double itemWidth = stackPanel?.ActualWidth ?? 0;
                double itemHeight = stackPanel?.ActualHeight ?? 0;
                // Clamp X and Y to keep the item inside the canvas
                double x = Math.Max(0, Math.Min(pos.X - _dragStartPoint.X + _draggedItem.X, canvas.ActualWidth - itemWidth));
                double y = Math.Max(0, Math.Min(pos.Y - _dragStartPoint.Y + _draggedItem.Y, canvas.ActualHeight - itemHeight));
                _draggedItem.X = x;
                _draggedItem.Y = y;
                _dragStartPoint = pos;
            }
        }

        private void DroppedMethod_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_isDragging)
            {
                var border = sender as Border;
                border?.ReleaseMouseCapture();
                _isDragging = false;
                _draggedItem = null;
            }
        }

        private void ParameterTextBox_PreviewDragOver(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(typeof(Instance)) && !e.Data.GetDataPresent(typeof(HeapObjectViewModel)))
            {
                e.Effects = DragDropEffects.None;
                e.Handled = true;
            }
            else
            {
                e.Effects = DragDropEffects.Copy;
                e.Handled = true;
            }
        }

        private void ParameterTextBox_PreviewDrop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(typeof(Instance)))
            {
                var instance = e.Data.GetData(typeof(Instance)) as Instance;
                var element = sender as FrameworkElement;
                if (instance != null && element != null)
                {
                    var x = element.DataContext as MethodInvocationParameter;
                    x.AssignedInstance = instance;
                    e.Handled = true;
                }
            }
            else if (e.Data.GetDataPresent(typeof(HeapObjectViewModel)))
            {
                var heapObj = e.Data.GetData(typeof(HeapObjectViewModel)) as HeapObjectViewModel;
                var element = sender as FrameworkElement;
                if (heapObj != null && heapObj.Frozen && element != null)
                {
                    var parameter = element.DataContext as MethodInvocationParameter;
                    var typeName = TypeNameUtils.NormalizeShort(heapObj.RemoteObject.GetRemoteType().Name);
                    parameter.AssignedInstance = new Instance
                    {
                        Obj = heapObj.RemoteObject,
                        Type = heapObj.RemoteObject.GetRemoteType(),
                        Tag = $"{typeName} {heapObj.HexAddress}"
                    };
                    e.Handled = true;
                }
            }
        }

        private void TextBlock_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;

            var element = sender as FrameworkElement;
            object? context = element?.DataContext;
            Instance instance = null;
            if (context is MethodInvocationParameter mip)
            {
                instance = mip.AssignedInstance;
            }
            else if (context is DroppedMethodItem droppedMethod)
            {
                instance = droppedMethod.Invocation?.ReturnValue?.AssignedInstance;
            }

            if (instance != null)
            {
                DragDrop.DoDragDrop(element, instance, DragDropEffects.Copy);
            }
        }

        private void ResultPanel_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount != 2)
            {
                // Single click - do the drag behavior
                DroppedMethod_MouseLeftButtonDown(sender, e);
                return;
            }

            // Double-click detected
            e.Handled = true;

            var border = sender as Border;
            if (border?.DataContext is not DroppedMethodItem droppedMethod)
                return;

            var instance = droppedMethod.Invocation?.ReturnValue?.AssignedInstance;
            if (instance?.Obj == null)
                return;

            // Find the MainWindow and RemoteAppModel first
            var mainWindow = Application.Current.MainWindow as RemoteNetSpy.MainWindow;
            if (mainWindow == null)
            {
                MessageBox.Show("Could not find main window.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // Get the RemoteAppModel from MainWindow
            var remoteAppModel = mainWindow.DataContext as RemoteAppModel;
            if (remoteAppModel == null)
            {
                // Try to get it through reflection if not accessible via DataContext
                var field = typeof(RemoteNetSpy.MainWindow).GetField("_remoteAppModel", BindingFlags.NonPublic | BindingFlags.Instance);
                remoteAppModel = field?.GetValue(mainWindow) as RemoteAppModel;
            }

            if (remoteAppModel == null)
            {
                MessageBox.Show("Could not access RemoteAppModel.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            RemoteObject remoteObject = null;

            // Case 1: instance.Obj is already a RemoteObject (for .NET targets)
            if (instance.Obj is RemoteObject ro)
            {
                remoteObject = ro;
            }
            // Case 2: instance.Obj is a UIntPtr (for MSVC targets) - need to resolve it
            else if (instance.Obj is UIntPtr ptr)
            {
                try
                {
                    // Get the expected return type from the method
                    var returnType = droppedMethod.Invocation?.Method?.Method?.ReturnType;
                    if (returnType == null)
                    {
                        MessageBox.Show("Could not determine return type of method.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }

                    ulong address = (ulong)ptr;
                    string typeName = returnType.FullName;

                    // Handle pointer types - get the inner type
                    if (returnType is RemoteNET.RttiReflection.PointerType pointerType)
                    {
                        typeName = pointerType.Inner.FullName;
                    }

                    // Use RemoteApp to get the RemoteObject from the pointer
                    remoteObject = remoteAppModel.App.GetRemoteObject(address, typeName);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to resolve pointer to RemoteObject: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
            }
            else
            {
                MessageBox.Show($"Unsupported return value type: {instance.Obj.GetType().Name}. Expected RemoteObject or UIntPtr.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (remoteObject == null)
                return;

            // Create a HeapObjectViewModel from the RemoteObject
            var heapObject = new HeapObjectViewModel
            {
                RemoteObject = remoteObject,
                FullTypeName = remoteObject.GetRemoteType().FullName
            };

            // Create ObjectViewerControl
            mainWindow.CreateNewInstanceTab(heapObject);
        }

        private void MethodParameter_MouseButtonDown(object sender, MouseButtonEventArgs e)
        {
            var element = sender as FrameworkElement;
            MethodInvocationParameter mip = element?.DataContext as MethodInvocationParameter;
            Type type = mip?.Type;
            if (type == null)
                return;

            // Check if the type is a primitive
            bool IsPrimitiveLike = type.IsPrimitive || type == typeof(string) || type == typeof(decimal);
            if (!IsPrimitiveLike)
                return;

            var inputWindow = new PrimitiveInputWindow($"Enter a {type.Name} value:");
            var ownerWindow = Window.GetWindow(this) ?? Application.Current?.MainWindow;
            inputWindow.Owner = ownerWindow;

            if (inputWindow.ShowDialog() == true)
            {
                string userInput = inputWindow.InputValue;
                try
                {
                    object convertedValue = Convert.ChangeType(userInput, type);
                    // Use convertedValue as needed
                    mip.AssignedInstance = new Instance()
                    {
                        Obj = convertedValue,
                        Type = type,
                        Tag = convertedValue.ToString()
                    };
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Invalid input: {ex.Message}");
                }
            }
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {

        }

        private void RemoveDroppedMethod_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is DroppedMethodItem itemToRemove)
                _viewModel.DroppedMethods.Remove(itemToRemove);
        }

        private void HeapObjectMethodsButton_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            if (button?.ContextMenu != null)
            {
                button.ContextMenu.PlacementTarget = button;
                button.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
                button.ContextMenu.IsOpen = true;
            }
        }

        private void MenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.DataContext is MethodInfo mi)
            {
                var heapObj = menuItem.Tag as HeapObjectViewModel;
                var methodWrapper = new MethodInfoWrapper(mi);
                if (methodWrapper != null)
                {
                    var invocation = new MethodInvocation(methodWrapper);
                    if (!methodWrapper.Method.IsStatic && heapObj?.RemoteObject != null)
                    {
                        var instanceTypeName = TypeNameUtils.NormalizeShort(heapObj.RemoteObject.GetRemoteType().Name);
                        invocation.ThisInstance.AssignedInstance = new Instance
                        {
                            Obj = heapObj.RemoteObject,
                            Type = heapObj.RemoteObject.GetRemoteType(),
                            Tag = $"{instanceTypeName} {heapObj.HexAddress}"
                        };
                    }
                    var position = GetDefaultDropPosition();
                    _viewModel.DroppedMethods.Add(new DroppedMethodItem(invocation, position.X, position.Y));
                }
            }
        }

        private Point GetDefaultDropPosition()
        {
            var margin = ReservoirBorder?.Margin ?? new Thickness(0);
            var padding = MainAreaBorder?.Padding ?? new Thickness(0);
            var x = padding.Left + 2;
            var y = (ReservoirBorder?.ActualHeight ?? 0) + margin.Top + margin.Bottom + 8;

            var offsetIndex = _defaultDropOffsetIndex++;
            x += offsetIndex * 20;
            y += offsetIndex * 50;

            var canvasWidth = MainAreaCanvas?.ActualWidth ?? 0;
            var canvasHeight = MainAreaCanvas?.ActualHeight ?? 0;

            if (canvasWidth > 0)
            {
                x = Math.Min(x, canvasWidth - 1);
            }

            if (canvasHeight > 0)
            {
                y = Math.Min(y, canvasHeight - 1);
            }

            return new Point(Math.Max(0, x), Math.Max(0, y));
        }
    }
}

