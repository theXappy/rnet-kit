using System;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;

namespace RemoteNetSpy.Converters
{
    /// <summary>
    /// Multi-value converter that colorizes method signatures with Visual Studio-like syntax highlighting.
    /// Used in both ObjectViewerControl (for member grid) and TypeView (for members list).
    /// 
    /// Takes two inputs:
    /// 1. Member name/signature (string)
    /// 2. Member type (string) - "Method", "Field", "Property", etc.
    /// 
    /// Returns a TextBlock with colored syntax for methods:
    /// - Return types: Blue
    /// - Method names: Bright Yellow  
    /// - Parameter types: Green
    /// - Parameter names: Gray
    /// - Non-methods: Default light gray
    /// </summary>
    public class MemberNameToFormattedTextConverter : IMultiValueConverter
    {
        // Visual Studio C# colors (approximation for dark theme)
        private static readonly SolidColorBrush ReturnTypeBrush = new SolidColorBrush(Color.FromRgb(86, 156, 214)); // Blue
        private static readonly SolidColorBrush MethodNameBrush = new SolidColorBrush(Color.FromRgb(220, 220, 170)); // Bright Yellow
        private static readonly SolidColorBrush ParameterTypeBrush = new SolidColorBrush(Color.FromRgb(78, 201, 176)); // Green
        private static readonly SolidColorBrush ParameterNameBrush = new SolidColorBrush(Color.FromRgb(156, 156, 156)); // Gray
        private static readonly SolidColorBrush DefaultBrush = new SolidColorBrush(Color.FromRgb(220, 220, 220)); // Light Gray

        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length < 2 || values[0] is not string name || values[1] is not string memberType)
            {
                return new TextBlock { Text = values[0]?.ToString() ?? "", Foreground = DefaultBrush };
            }

            // Only colorize method signatures
            if (memberType != "Method")
            {
                return new TextBlock { Text = name, Foreground = DefaultBrush };
            }

            // Check if this is a full signature or just a method name
            if (name.Contains("(") && name.Contains(")"))
            {
                // Full signature - colorize it
                return CreateColorizedTextBlock(name);
            }
            else
            {
                // Just a method name - highlight it in yellow
                return new TextBlock { Text = name, Foreground = MethodNameBrush };
            }
        }

        private TextBlock CreateColorizedTextBlock(string signature)
        {
            var textBlock = new TextBlock();
            
            // Pattern to match method signatures:
            // returnType methodName(paramType1 paramName1, paramType2 paramName2, ...)
            var methodPattern = @"^(.+?)\s+([a-zA-Z_][a-zA-Z0-9_<>]*)\s*\((.*)\)\s*$";
            var match = Regex.Match(signature, methodPattern);

            if (!match.Success)
            {
                // Try a more flexible pattern for complex signatures
                return CreateFallbackColorizedTextBlock(signature);
            }

            var returnType = match.Groups[1].Value.Trim();
            var methodName = match.Groups[2].Value;
            var parameters = match.Groups[3].Value.Trim();

            // Add return type (blue)
            textBlock.Inlines.Add(new Run(returnType) { Foreground = ReturnTypeBrush });
            textBlock.Inlines.Add(new Run(" "));

            // Add method name (bright yellow)
            textBlock.Inlines.Add(new Run(methodName) { Foreground = MethodNameBrush });

            // Add opening parenthesis
            textBlock.Inlines.Add(new Run("(") { Foreground = DefaultBrush });

            // Parse and colorize parameters
            if (!string.IsNullOrEmpty(parameters))
            {
                ColorizeParameters(textBlock, parameters);
            }

            // Add closing parenthesis
            textBlock.Inlines.Add(new Run(")") { Foreground = DefaultBrush });

            return textBlock;
        }

        private TextBlock CreateFallbackColorizedTextBlock(string signature)
        {
            var textBlock = new TextBlock();
            
            // Look for method patterns with parentheses
            if (signature.Contains("(") && signature.Contains(")"))
            {
                // Find the last occurrence of opening parenthesis to handle complex return types
                int parenIndex = signature.LastIndexOf('(');
                if (parenIndex > 0)
                {
                    var beforeParen = signature.Substring(0, parenIndex).Trim();
                    var afterParen = signature.Substring(parenIndex);
                    
                    // Try to split return type and method name
                    // Look for the last space before the parentheses, but be careful with generic types
                    int lastSpaceIndex = FindLastSpaceOutsideGenerics(beforeParen);
                    if (lastSpaceIndex > 0)
                    {
                        var returnType = beforeParen.Substring(0, lastSpaceIndex).Trim();
                        var methodName = beforeParen.Substring(lastSpaceIndex + 1).Trim();
                        
                        // Color return type
                        textBlock.Inlines.Add(new Run(returnType) { Foreground = ReturnTypeBrush });
                        textBlock.Inlines.Add(new Run(" "));
                        
                        // Color method name
                        textBlock.Inlines.Add(new Run(methodName) { Foreground = MethodNameBrush });
                        
                        // Color parameters
                        ColorizeParametersPart(textBlock, afterParen);
                        
                        return textBlock;
                    }
                }
            }
            
            // If all else fails, just return as plain text
            textBlock.Inlines.Add(new Run(signature) { Foreground = DefaultBrush });
            return textBlock;
        }

        private int FindLastSpaceOutsideGenerics(string text)
        {
            int depth = 0;
            for (int i = text.Length - 1; i >= 0; i--)
            {
                char c = text[i];
                if (c == '>' || c == ']')
                    depth++;
                else if (c == '<' || c == '[')
                    depth--;
                else if (c == ' ' && depth == 0)
                    return i;
            }
            return -1;
        }

        private void ColorizeParameters(TextBlock textBlock, string parameters)
        {
            // Split parameters by comma, but be careful with generic types
            var paramList = SplitParameters(parameters);
            
            for (int i = 0; i < paramList.Count; i++)
            {
                if (i > 0)
                {
                    textBlock.Inlines.Add(new Run(", ") { Foreground = DefaultBrush });
                }
                
                ColorizeParameter(textBlock, paramList[i].Trim());
            }
        }

        private void ColorizeParametersPart(TextBlock textBlock, string paramsPart)
        {
            // Extract content between parentheses
            if (paramsPart.StartsWith("(") && paramsPart.EndsWith(")"))
            {
                textBlock.Inlines.Add(new Run("(") { Foreground = DefaultBrush });
                
                var innerParams = paramsPart.Substring(1, paramsPart.Length - 2);
                if (!string.IsNullOrEmpty(innerParams))
                {
                    ColorizeParameters(textBlock, innerParams);
                }
                
                textBlock.Inlines.Add(new Run(")") { Foreground = DefaultBrush });
            }
            else
            {
                textBlock.Inlines.Add(new Run(paramsPart) { Foreground = DefaultBrush });
            }
        }

        private System.Collections.Generic.List<string> SplitParameters(string parameters)
        {
            var result = new System.Collections.Generic.List<string>();
            var current = "";
            int depth = 0;
            
            for (int i = 0; i < parameters.Length; i++)
            {
                char c = parameters[i];
                
                if (c == '<' || c == '[' || c == '(')
                    depth++;
                else if (c == '>' || c == ']' || c == ')')
                    depth--;
                else if (c == ',' && depth == 0)
                {
                    result.Add(current);
                    current = "";
                    continue;
                }
                
                current += c;
            }
            
            if (!string.IsNullOrEmpty(current))
                result.Add(current);
                
            return result;
        }

        private void ColorizeParameter(TextBlock textBlock, string parameter)
        {
            // Pattern to match: type paramName
            var paramPattern = @"^(.+?)\s+([a-zA-Z_][a-zA-Z0-9_]*)$";
            var match = Regex.Match(parameter, paramPattern);
            
            if (match.Success)
            {
                var paramType = match.Groups[1].Value;
                var paramName = match.Groups[2].Value;
                
                // Color parameter type (green)
                textBlock.Inlines.Add(new Run(paramType) { Foreground = ParameterTypeBrush });
                textBlock.Inlines.Add(new Run(" "));
                
                // Color parameter name (gray)
                textBlock.Inlines.Add(new Run(paramName) { Foreground = ParameterNameBrush });
            }
            else
            {
                // No parameter name, just type (color as type)
                textBlock.Inlines.Add(new Run(parameter) { Foreground = ParameterTypeBrush });
            }
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}