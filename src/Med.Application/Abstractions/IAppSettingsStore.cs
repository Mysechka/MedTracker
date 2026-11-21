namespace Med.Application.Abstractions;

/// <summary>
/// Простое хранилище настроек уровня приложения (ключ-значение).
/// Не зависит от текущего пользователя и сохраняется между сессиями на устройстве.
/// </summary>
public interface IAppSettingsStore
{
    string? GetString(string key);
    void SetString(string key, string? value);
    double GetDouble(string key, double defaultValue);
    void SetDouble(string key, double value);
}
