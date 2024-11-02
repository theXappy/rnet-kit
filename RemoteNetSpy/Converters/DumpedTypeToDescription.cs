using System;
using System.Globalization;
using System.Windows.Data;
using RemoteNetSpy.Models;

namespace RemoteNetSpy.Converters;

public class DumpedTypeToDescription : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        DumpedTypeModel a = value as DumpedTypeModel;
        if (a == null)
            return "Error: null";

        string desc = a.FullTypeName;
        if (a.HaveInstances)
        {
            int difference = a.NumInstances.GetValueOrDefault() - a.PreviousNumInstances.GetValueOrDefault();
            string differenceText = difference != 0 ? difference > 0 ? $"+{difference}" : difference.ToString() : string.Empty;
            if (string.IsNullOrEmpty(differenceText))
            {
                desc += $" (Count: {a.NumInstances})";
            }
            else
            {
                desc += $" (Count: {a.NumInstances}, Diff: {differenceText})";
            }
        }
        return desc;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
