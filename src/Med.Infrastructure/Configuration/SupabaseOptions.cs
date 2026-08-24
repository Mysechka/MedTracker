using System.ComponentModel.DataAnnotations;

namespace Med.Infrastructure.Configuration;

/// <summary>
/// Клиентская конфигурация Supabase. Здесь допустим только anon key.
/// service_role в клиентском приложении недопустим ни при каких условиях —
/// он живёт исключительно в переменных окружения Edge Functions.
/// </summary>
public sealed class SupabaseOptions
{
    public const string SectionName = "Supabase";

    [Required]
    [Url]
    public string Url { get; init; } = string.Empty;

    [Required]
    public string AnonKey { get; init; } = string.Empty;
}
