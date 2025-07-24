using RemoteNetSpy.Controls.DragDropPlaygroundSubsystem;
using System;
using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

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

        private void ReservoirListBox_PreviewMouseMove(object sender, MouseEventArgs e)
        {
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
            }
        }

        private void MainAreaBorder_DragOver(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(typeof(MethodInfoWrapper)))
            {
                e.Effects = DragDropEffects.None;
                e.Handled = true;
            }
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
            if (!e.Data.GetDataPresent(typeof(Instance)))
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
        }

        private void TextBlock_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // lol
            e.Handled = true;

            object? context = (sender as FrameworkElement)?.DataContext;
            if (context is MethodInvocationParameter mip)
            {
                var item = mip.AssignedInstance;
                if (item is Instance instance)
                {
                    DragDrop.DoDragDrop(sender as DependencyObject, instance, DragDropEffects.Copy);
                }
            }
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
    }
}

