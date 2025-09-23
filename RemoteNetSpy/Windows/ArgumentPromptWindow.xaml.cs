using RemoteNET;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;

namespace RemoteNetSpy
{
    public partial class ArgumentPromptWindow : Window
    {
        private readonly ParameterInfo[] _parameters;
        private readonly RemoteApp _remoteApp;
        private readonly List<ParameterInputControl> _parameterControls;
        private readonly string _methodName;
        
        public object[] Arguments { get; private set; }

        public ArgumentPromptWindow(ParameterInfo[] parameters, RemoteApp remoteApp, string methodName = "Method")
        {
            InitializeComponent();
            _parameters = parameters;
            _remoteApp = remoteApp;
            _methodName = methodName;
            _parameterControls = new List<ParameterInputControl>();
            
            InitializeUI();
        }

        private void InitializeUI()
        {
            // Set method signature
            var parameterSignatures = _parameters.Select(p => $"{GetTypeDisplayName(p.ParameterType)} {p.Name}");
            MethodSignatureText.Text = $"{_methodName}({string.Join(", ", parameterSignatures)})";

            // Create parameter controls
            for (int i = 0; i < _parameters.Length; i++)
            {
                var param = _parameters[i];
                var control = new ParameterInputControl(param, i + 1, _remoteApp);
                _parameterControls.Add(control);
                ParametersPanel.Children.Add(control);
            }

            // Set focus to first parameter textbox after the window is loaded
            if (_parameterControls.Count > 0)
            {
                Loaded += (s, e) => _parameterControls[0].FocusTextBox();
            }
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Arguments = new object[_parameters.Length];
                
                for (int i = 0; i < _parameterControls.Count; i++)
                {
                    var control = _parameterControls[i];
                    if (!control.TryGetValue(out object value, out string error))
                    {
                        MessageBox.Show($"Parameter {i + 1}: {error}", "Invalid Parameter", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                    Arguments[i] = value;
                }

                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private static string GetTypeDisplayName(Type type)
        {
            if (type == null) return "object";

            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>))
            {
                var underlyingType = Nullable.GetUnderlyingType(type);
                return GetTypeDisplayName(underlyingType) + "?";
            }

            return type.Name;
        }
    }

    public class ParameterInputControl : Border
    {
        private readonly ParameterInfo _parameter;
        private readonly int _index;
        private readonly RemoteApp _remoteApp;
        private readonly TextBox _valueTextBox;
        private readonly Button _allocateButton;
        private readonly TextBlock _errorText;

        public ParameterInputControl(ParameterInfo parameter, int index, RemoteApp remoteApp)
        {
            _parameter = parameter;
            _index = index;
            _remoteApp = remoteApp;

            Margin = new Thickness(0, 5, 0, 0);
            Padding = new Thickness(10);
            Background = Application.Current.Resources["ControlDefaultBackground"] as System.Windows.Media.Brush;
            BorderBrush = Application.Current.Resources["ControlDefaultBorderBrush"] as System.Windows.Media.Brush;
            BorderThickness = new Thickness(1);

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Header
            var headerPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 5) };
            Grid.SetRow(headerPanel, 0);
            Grid.SetColumnSpan(headerPanel, 3);

            var indexText = new TextBlock { Text = $"Parameter {index}:", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 10, 0) };
            var typeText = new TextBlock { Text = GetTypeDisplayName(_parameter.ParameterType), Foreground = System.Windows.Media.Brushes.Orange, Margin = new Thickness(0, 0, 10, 0) };
            var nameText = new TextBlock { Text = _parameter.Name, Foreground = System.Windows.Media.Brushes.LightBlue };

            headerPanel.Children.Add(indexText);
            headerPanel.Children.Add(typeText);
            headerPanel.Children.Add(nameText);
            grid.Children.Add(headerPanel);

            // Value input
            var valueLabel = new TextBlock { Text = "Value:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
            Grid.SetRow(valueLabel, 1);
            Grid.SetColumn(valueLabel, 0);
            grid.Children.Add(valueLabel);

            _valueTextBox = new TextBox
            {
                Background = Application.Current.Resources["ControlDefaultBackground"] as System.Windows.Media.Brush,
                Foreground = Application.Current.Resources["ControlDefaultForeground"] as System.Windows.Media.Brush,
                BorderBrush = Application.Current.Resources["ControlBrightDefaultBorderBrush"] as System.Windows.Media.Brush,
                BorderThickness = new Thickness(1)
            };
            Grid.SetRow(_valueTextBox, 1);
            Grid.SetColumn(_valueTextBox, 1);
            grid.Children.Add(_valueTextBox);

            // Allocate button for pointers
            bool isPointer = IsPointerType(_parameter.ParameterType);
            if (isPointer)
            {
                _allocateButton = new Button
                {
                    Content = "Allocate Memory",
                    Margin = new Thickness(10, 0, 0, 0),
                    Padding = new Thickness(10, 2, 10, 2)
                };
                _allocateButton.Click += AllocateButton_Click;
                Grid.SetRow(_allocateButton, 1);
                Grid.SetColumn(_allocateButton, 2);
                grid.Children.Add(_allocateButton);
            }

            // Error text
            _errorText = new TextBlock
            {
                Foreground = System.Windows.Media.Brushes.Red,
                FontStyle = FontStyles.Italic,
                Margin = new Thickness(0, 5, 0, 0),
                Visibility = Visibility.Collapsed
            };
            Grid.SetRow(_errorText, 2);
            Grid.SetColumnSpan(_errorText, 3);
            grid.Children.Add(_errorText);

            Child = grid;
        }

        private void AllocateButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new MemoryAllocationDialog(_remoteApp);
            if (dialog.ShowDialog() == true)
            {
                _valueTextBox.Text = $"0x{dialog.AllocatedAddress.ToInt64():X16}";
            }
        }

        public bool TryGetValue(out object value, out string error)
        {
            value = null;
            error = null;

            try
            {
                if (string.IsNullOrWhiteSpace(_valueTextBox.Text))
                {
                    error = "Value cannot be empty";
                    ShowError(error);
                    return false;
                }

                value = ValueParser.ParseValue(_valueTextBox.Text, _parameter.ParameterType);
                HideError();
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                ShowError(error);
                return false;
            }
        }

        public void FocusTextBox()
        {
            _valueTextBox.Focus();
        }

        private void ShowError(string message)
        {
            _errorText.Text = message;
            _errorText.Visibility = Visibility.Visible;
            _valueTextBox.BorderBrush = System.Windows.Media.Brushes.Red;
        }

        private void HideError()
        {
            _errorText.Visibility = Visibility.Collapsed;
            _valueTextBox.BorderBrush = Application.Current.Resources["ControlBrightDefaultBorderBrush"] as System.Windows.Media.Brush;
        }

        private static bool IsPointerType(Type type)
        {
            return type == typeof(IntPtr) || type == typeof(UIntPtr) || type.Name.EndsWith("*");
        }

        private static string GetTypeDisplayName(Type type)
        {
            if (type == null) return "object";

            return type.Name;
        }
    }

    public static class ValueParser
    {
        private static readonly Regex HexWithSpacesRegex = new Regex(@"^([0-9A-Fa-f]{2}(?:\s+[0-9A-Fa-f]{2})*)$", RegexOptions.Compiled);
        private static readonly Regex HexWithPrefixRegex = new Regex(@"^0[xX]([0-9A-Fa-f]+)$", RegexOptions.Compiled);
        private static readonly Regex DecimalWithCommasRegex = new Regex(@"^[\d,]+$", RegexOptions.Compiled);

        public static object ParseValue(string input, Type targetType)
        {
            if (string.IsNullOrWhiteSpace(input))
                throw new ArgumentException("Value cannot be empty");

            input = input.Trim();
            object parsedValue = null;

            // 1. Check for hex with spaces (e.g., "AA BB CC DD")
            if (HexWithSpacesRegex.IsMatch(input))
            {
                parsedValue = ParseHexWithSpaces(input);
            }
            // 2. Check for hex with prefix (e.g., "0xAABBCCDD")
            else if (HexWithPrefixRegex.IsMatch(input))
            {
                var match = HexWithPrefixRegex.Match(input);
                parsedValue = ParseHexString(match.Groups[1].Value);
            }
            // 3. Check for decimal with commas (e.g., "123,456")
            else if (DecimalWithCommasRegex.IsMatch(input))
            {
                string cleanedInput = input.Replace(",", "");
                parsedValue = ParseDecimal(cleanedInput);
            }
            // 4. Try plain decimal
            else
            {
                parsedValue = ParseDecimal(input);
            }

            return ConvertToTargetType(parsedValue, targetType);
        }

        private static object ParseHexWithSpaces(string input)
        {
            string[] parts = input.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            string combined = string.Join("", parts);
            
            if (combined.Length <= 8)
                return long.Parse(combined, NumberStyles.HexNumber);
            else if (combined.Length <= 16)
                return long.Parse(combined, NumberStyles.HexNumber);
            else
                throw new ArgumentException($"Hex value is too large (max 16 hex digits supported)");
        }

        private static object ParseHexString(string hexString)
        {
            if (hexString.Length <= 16)
                return long.Parse(hexString, NumberStyles.HexNumber);
            else
                throw new ArgumentException($"Hex value is too large (max 16 hex digits supported)");
        }

        private static object ParseDecimal(string input)
        {
            if (long.TryParse(input, out long longResult))
                return longResult;
            
            if (double.TryParse(input, out double doubleResult))
                return doubleResult;

            throw new ArgumentException($"Cannot parse '{input}' as a number");
        }

        private static object ConvertToTargetType(object value, Type targetType)
        {
            if (targetType == null)
                return value;

            if (targetType.IsGenericType && targetType.GetGenericTypeDefinition() == typeof(Nullable<>))
            {
                targetType = Nullable.GetUnderlyingType(targetType);
            }

            if (targetType == typeof(IntPtr))
                return new IntPtr(Convert.ToInt64(value));
            
            if (targetType == typeof(UIntPtr))
                return new UIntPtr(Convert.ToUInt64(value));

            // Handle pointer types - just return the raw address as long
            // The actual method invocation system expects IntPtr/UIntPtr/long values, not typed pointers
            if (targetType.Name.EndsWith("*"))
            {
                return Convert.ToInt64(value);
            }

            try
            {
                return Convert.ChangeType(value, targetType);
            }
            catch (Exception ex)
            {
                throw new ArgumentException($"Cannot convert value to {targetType.Name}: {ex.Message}");
            }
        }
    }
}
