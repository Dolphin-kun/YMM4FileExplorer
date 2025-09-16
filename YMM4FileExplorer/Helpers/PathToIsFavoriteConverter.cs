using System.Globalization;
using System.Windows.Data;
using YMM4FileExplorer.Settings;

namespace YMM4FileExplorer.Helpers
{
    public class PathToIsFavoriteConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string path && !string.IsNullOrEmpty(path))
            {
                return FileExplorerSettings.Default.Favorites.Any(f => f.FullPath.Equals(path, StringComparison.OrdinalIgnoreCase));
            }
            return false;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
