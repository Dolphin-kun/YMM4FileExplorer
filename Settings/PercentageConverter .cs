using System.Globalization;
using System.Windows.Data;

namespace YMM4FileExplorer.Settings
{
    class PercentageConverter: IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double d)
                return (int)(d * 100);
            return 0;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int i)
                return i / 100.0;
            if (value is string s && int.TryParse(s, out var si))
                return si / 100.0;
            return 0.0;
        }
    }
}
