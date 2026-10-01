using System.Collections.Concurrent;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;

namespace Med.Ui.Controls;

public sealed class PathToBitmapConverter : IValueConverter
{
    public static readonly PathToBitmapConverter Instance = new();

    private static readonly ConcurrentDictionary<string, WeakReference<Bitmap>> _cache = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string path || string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        string cleanPath = path.Trim();
        int qIndex = cleanPath.IndexOf('?');
        string filePath = qIndex >= 0 ? cleanPath[..qIndex] : cleanPath;

        if (!File.Exists(filePath))
        {
            return null;
        }

        if (_cache.TryGetValue(cleanPath, out var weakRef) && weakRef.TryGetTarget(out var cached))
        {
            return cached;
        }

        try
        {
            using FileStream stream = File.OpenRead(filePath);
            var bitmap = new Bitmap(stream);
            _cache[cleanPath] = new WeakReference<Bitmap>(bitmap);
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
