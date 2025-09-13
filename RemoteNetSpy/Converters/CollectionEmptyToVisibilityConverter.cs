using System;
using System.Collections;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Data;

namespace RemoteNetSpy.Converters;

public class CollectionEmptyToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool hasItems = false;

        if (value is IEnumerable collection)
        {
            // Use LINQ to check if collection has any items - this handles disposal automatically
            hasItems = collection.Cast<object>().Any();
        }

        // Parameter can be "Inverse" to reverse the logic
        bool isInverse = parameter?.ToString()?.Equals("Inverse", StringComparison.OrdinalIgnoreCase) == true;
        
        if (isInverse)
        {
            // Show when collection has items, hide when empty
            return hasItems ? Visibility.Visible : Visibility.Collapsed;
        }
        else
        {
            // Show when collection is empty, hide when has items
            return hasItems ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}