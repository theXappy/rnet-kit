using System;
using System.Collections;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace DragDropExpressionBuilder.Converters
{
    public class IsLastItemToVisibilityConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            var item = values[0];
            var itemsControl = values[1] as ItemsControl;

            if (item == null || itemsControl == null)
                return Visibility.Visible;

            var collection = itemsControl.ItemsSource as IEnumerable;
            if (collection == null)
                return Visibility.Visible;

            object last = null;
            foreach (var obj in collection)
                last = obj;

            return ReferenceEquals(item, last) ? Visibility.Collapsed : Visibility.Visible;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

}
