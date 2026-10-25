using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace AeroSpot.Worker
{
    public static class Converters
    {
        public static readonly IValueConverter BoolToVisibility = new BooleanToVisibilityConverter();
        public static readonly IValueConverter InverseBoolToVisibility = new InverseBooleanToVisibilityConverter();
    }

    public sealed class InverseBooleanToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b)
            {
                return b ? Visibility.Collapsed : Visibility.Visible;
            }
            return Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
