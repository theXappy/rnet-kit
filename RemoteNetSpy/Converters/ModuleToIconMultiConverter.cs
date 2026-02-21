using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using RemoteNET;

namespace RemoteNetSpy.Converters;

public class ModuleToIconMultiConverter : IMultiValueConverter
{
    private static readonly BitmapImage CppProjectNodeIcon;
    private static readonly BitmapImage CppProjectNodeGrayedIcon;
    private static readonly BitmapImage ModulePublicIcon;
    private static readonly BitmapImage ModulePublicGrayedIcon;
    private static readonly DrawingImage ModuleErrorIcon;

    static ModuleToIconMultiConverter()
    {
        CppProjectNodeIcon = new BitmapImage(new Uri("/icons/CPPProjectNode.png", UriKind.RelativeOrAbsolute));
        CppProjectNodeGrayedIcon = new BitmapImage(new Uri("/icons/CPPProjectNode_grayed.png", UriKind.RelativeOrAbsolute));
        ModulePublicIcon = new BitmapImage(new Uri("/icons/ModulePublic.png", UriKind.RelativeOrAbsolute));
        ModulePublicGrayedIcon = new BitmapImage(new Uri("/icons/ModulePublic_grayed.png", UriKind.RelativeOrAbsolute));
        ModuleErrorIcon = CreateModuleErrorIcon();
    }

    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2) return null;

        if (values[0] is not RuntimeType runtime)
            return null;
        bool anyTypes;
        if (values[1] is int typesCount)
        {
            anyTypes = typesCount > 0;
        }
        else
        {
            // Probably "UnsetValue"
            anyTypes = false;
        }

        bool hasLoadErrors = values.Length > 2 && values[2] is bool hasErrors && hasErrors;
        if (hasLoadErrors)
        {
            return ModuleErrorIcon;
        }

        if (runtime == RuntimeType.Unmanaged)
        {
            return anyTypes ? CppProjectNodeIcon : CppProjectNodeGrayedIcon;
        }
        else
        {
            return anyTypes ? ModulePublicIcon : ModulePublicGrayedIcon;
        }
    }

    private static DrawingImage CreateModuleErrorIcon()
    {
        var circle = new EllipseGeometry(new System.Windows.Point(8, 8), 7.5, 7.5);
        var bar = new RectangleGeometry(new System.Windows.Rect(7, 3.5, 2, 6.5));
        var dot = new RectangleGeometry(new System.Windows.Rect(7, 11.5, 2, 2));

        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(220, 60, 60)), null, circle));
        group.Children.Add(new GeometryDrawing(System.Windows.Media.Brushes.White, null, bar));
        group.Children.Add(new GeometryDrawing(System.Windows.Media.Brushes.White, null, dot));
        return new DrawingImage(group);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

