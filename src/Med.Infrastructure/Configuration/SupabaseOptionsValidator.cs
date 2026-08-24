using System.Text;
using Microsoft.Extensions.Options;

namespace Med.Infrastructure.Configuration;

/// <summary>
/// Не даёт запустить клиент с ключом service_role: такой ключ обходит RLS,
/// а значит утечка одного устройства открывает медкарты всех пользователей.
/// </summary>
internal sealed class SupabaseOptionsValidator : IValidateOptions<SupabaseOptions>
{
    private const string ForbiddenRole = "service_role";

    public ValidateOptionsResult Validate(string? name, SupabaseOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (LooksLikeServiceRoleKey(options.AnonKey))
        {
            return ValidateOptionsResult.Fail(
                "Supabase:AnonKey содержит ключ service_role. В клиентском приложении допустим только anon key.");
        }

        return ValidateOptionsResult.Success;
    }

    private static bool LooksLikeServiceRoleKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        // Новые ключи Supabase (sb_secret_...) не являются JWT, но точно секретные.
        if (key.StartsWith("sb_secret_", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        string[] parts = key.Split('.');
        if (parts.Length != 3)
        {
            return false;
        }

        string payload = DecodeBase64Url(parts[1]);
        return payload.Contains(ForbiddenRole, StringComparison.OrdinalIgnoreCase);
    }

    private static string DecodeBase64Url(string value)
    {
        string padded = value.Replace('-', '+').Replace('_', '/');
        padded = padded.PadRight(padded.Length + ((4 - (padded.Length % 4)) % 4), '=');

        try
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(padded));
        }
        catch (FormatException)
        {
            // Ключ не является JWT — проверить роль невозможно, но и глушить нечего:
            // строгая валидация формата остаётся за DataAnnotations.
            return string.Empty;
        }
    }
}
