using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;

namespace Med.Ui.Controls;

public sealed class PathToBitmapConverter : IValueConverter
{
    public static readonly PathToBitmapConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string path && !string.IsNullOrWhiteSpace(path))
        {
            string cleanPath = path;
            int qIndex = path.IndexOf('?');
            if (qIndex >= 0)
            {
                cleanPath = path[..qIndex];
            }

            if (File.Exists(cleanPath))
            {
                try
                {
                    using FileStream stream = File.OpenRead(cleanPath);
                    return new Bitmap(stream);
                }
                catch
                {
                    return null;
                }
            }
        }

        return null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
