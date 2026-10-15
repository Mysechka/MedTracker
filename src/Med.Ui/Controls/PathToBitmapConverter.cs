using System.Collections.Concurrent;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;

namespace Med.Ui.Controls;

public sealed class PathToBitmapConverter : IValueConverter
{
    public static readonly PathToBitmapConverter Instance = new();

    private static readonly ConcurrentDictionary<string, (long Ticks, WeakReference<Bitmap> Ref)> _cache =
        new(StringComparer.Ordinal);

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string path || string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        ReadOnlySpan<char> span = path.AsSpan().Trim();
        int qIndex = span.IndexOf('?');
        string filePath = (qIndex >= 0 ? span[..qIndex] : span).ToString();

        if (!File.Exists(filePath))
        {
            return null;
        }

        long writeTicks = File.GetLastWriteTimeUtc(filePath).Ticks;
        if (_cache.TryGetValue(filePath, out var entry)
            && entry.Ticks == writeTicks
            && entry.Ref.TryGetTarget(out Bitmap? cached))
        {
            return cached;
        }

        try
        {
            using FileStream stream = File.OpenRead(filePath);
            var bitmap = new Bitmap(stream);
            _cache[filePath] = (writeTicks, new WeakReference<Bitmap>(bitmap));
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
