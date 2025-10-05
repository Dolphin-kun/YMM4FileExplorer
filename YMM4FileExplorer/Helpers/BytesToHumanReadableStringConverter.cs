using System.Globalization;
using System.Windows.Data;

namespace YMM4FileExplorer.Helpers
{
    public class BytesToHumanReadableStringConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not long bytes)
            {
                return string.Empty;
            }

            if (bytes < 0)
            {
                return "0 B";
            }

            const long kb = 1024;
            const long mb = kb * 1024;
            const long gb = mb * 1024;

            if (bytes >= gb)
            {
                return $"{(double)bytes / gb:0.##} GB";
            }
            if (bytes >= mb)
            {
                return $"{(double)bytes / mb:0.##} MB";
            }
            if (bytes >= kb)
            {
                return $"{(double)bytes / kb:0.##} KB";
            }

            return $"{bytes} B";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
