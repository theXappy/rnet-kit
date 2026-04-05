using System;
using System.Globalization;
using System.Reflection;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;

namespace RemoteNetSpy.Converters
{
    public class MethodInfoToFormattedTextConverter : IValueConverter
    {
        private static readonly SolidColorBrush TypeBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4EC9B0"));
        private static readonly SolidColorBrush MethodBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#DCDCAA"));
        private static readonly SolidColorBrush WhiteBrush = new SolidColorBrush(Colors.White);

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not MethodInfo methodInfo)
                return null;

            var textBlock = new System.Windows.Controls.TextBlock();
            textBlock.Inlines.Add(new Run(methodInfo.Name) { Foreground = MethodBrush, FontWeight = System.Windows.FontWeights.Bold });
            textBlock.Inlines.Add(new Run("(") { Foreground = WhiteBrush });

            var parameters = methodInfo.GetParameters();
            for (int i = 0; i < parameters.Length; i++)
            {
                var param = parameters[i];
                var typeName = GetShortTypeName(param.ParameterType);
                textBlock.Inlines.Add(new Run(typeName) { Foreground = TypeBrush });

                if (i < parameters.Length - 1)
                {
                    textBlock.Inlines.Add(new Run(", ") { Foreground = WhiteBrush });
                }
            }

            textBlock.Inlines.Add(new Run(") → ") { Foreground = WhiteBrush });

            var returnTypeName = GetShortTypeName(methodInfo.ReturnType);
            textBlock.Inlines.Add(new Run(returnTypeName) { Foreground = TypeBrush });

            return textBlock;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }

        private string GetShortTypeName(Type type)
        {
            if (type == null)
                return "void";

            // Handle common types
            if (type == typeof(void)) return "void";
            if (type == typeof(int)) return "int";
            if (type == typeof(string)) return "string";
            if (type == typeof(bool)) return "bool";
            if (type == typeof(double)) return "double";
            if (type == typeof(float)) return "float";
            if (type == typeof(long)) return "long";
            if (type == typeof(short)) return "short";
            if (type == typeof(byte)) return "byte";
            if (type == typeof(char)) return "char";
            if (type == typeof(decimal)) return "decimal";
            if (type == typeof(object)) return "object";

            // For generic types, simplify the name
            if (type.IsGenericType)
            {
                var genericTypeName = type.Name.Split('`')[0];
                var genericArgs = type.GetGenericArguments();
                var args = string.Join(", ", Array.ConvertAll(genericArgs, GetShortTypeName));
                return $"{genericTypeName}<{args}>";
            }

            // Use the simple name for non-generic types
            return type.Name;
        }
    }
}
