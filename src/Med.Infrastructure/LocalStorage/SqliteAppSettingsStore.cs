using System.Globalization;
using Med.Application.Abstractions;

namespace Med.Infrastructure.LocalStorage;

public sealed class SqliteAppSettingsStore(LocalDatabase database) : IAppSettingsStore
{
    private readonly LocalDatabase _database = database ?? throw new ArgumentNullException(nameof(database));

    public string? GetString(string key) => _database.GetSetting(key);

    public void SetString(string key, string? value) => _database.SetSetting(key, value);

    public double GetDouble(string key, double defaultValue)
    {
        string? raw = _database.GetSetting(key);
        return raw is not null && double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
            ? value
            : defaultValue;
    }

    public void SetDouble(string key, double value) =>
        _database.SetSetting(key, value.ToString("R", CultureInfo.InvariantCulture));
}
