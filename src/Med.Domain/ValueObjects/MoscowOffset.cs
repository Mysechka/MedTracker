namespace Med.Domain.ValueObjects;

/// <summary>
/// Часовой пояс профиля как целое смещение в часах относительно Москвы.
/// Пользователь не выбирает произвольную IANA-зону: 0 — Москва, +4 — Москва+4, −2 — Москва−2.
/// </summary>
/// <remarks>
/// Смещение приводится к фиксированной IANA-зоне, потому что профиль хранит зону строкой.
/// Верхняя граница — +11, а не +14 из спецификации: Москва это UTC+3, зон восточнее UTC+14
/// в базе IANA не существует, и Москва+12 была бы UTC+15.
/// </remarks>
public readonly record struct MoscowOffset
{
    /// <summary>Базовая зона: смещение 0 означает московское время.</summary>
    public const string MoscowTimeZoneId = "Europe/Moscow";

    private const int MoscowUtcOffsetHours = 3;
    private const int MinUtcOffsetHours = -12;
    private const int MaxUtcOffsetHours = 14;

    /// <summary>Минимальное смещение от Москвы: −12 из спецификации, то есть UTC−9.</summary>
    public const int MinHours = -12;

    /// <summary>Максимальное смещение от Москвы: +11, то есть UTC+14 — крайняя существующая зона.</summary>
    public const int MaxHours = MaxUtcOffsetHours - MoscowUtcOffsetHours;

    private MoscowOffset(int hours) => Hours = hours;

    /// <summary>Смещение в часах относительно Москвы.</summary>
    public int Hours { get; }

    /// <summary>Москва без смещения — значение по умолчанию для нового профиля.</summary>
    public static MoscowOffset Moscow => new(0);

    public static MoscowOffset FromHours(int hours)
    {
        if (hours is < MinHours or > MaxHours)
        {
            throw new ArgumentOutOfRangeException(
                nameof(hours),
                hours,
                $"Смещение от Москвы допустимо в диапазоне от {MinHours} до {MaxHours} часов.");
        }

        return new MoscowOffset(hours);
    }

    /// <summary>IANA-идентификатор фиксированной зоны, соответствующей смещению.</summary>
    public string ToTimeZoneId()
    {
        if (Hours == 0)
        {
            return MoscowTimeZoneId;
        }

        int utcOffset = MoscowUtcOffsetHours + Hours;

        // В зонах Etc знак инвертирован по отношению к UTC: Etc/GMT-7 это UTC+7.
        return utcOffset switch
        {
            0 => "Etc/UTC",
            > 0 => $"Etc/GMT-{utcOffset}",
            _ => $"Etc/GMT+{-utcOffset}",
        };
    }

    /// <summary>
    /// Обратное преобразование для зон, которые может выдать <see cref="ToTimeZoneId"/>,
    /// плюс исторические значения профиля (<c>UTC</c>). Для незнакомой зоны — <c>null</c>.
    /// </summary>
    public static MoscowOffset? TryFromTimeZoneId(string? timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            return null;
        }

        string id = timeZoneId.Trim();

        if (string.Equals(id, MoscowTimeZoneId, StringComparison.OrdinalIgnoreCase))
        {
            return Moscow;
        }

        if (string.Equals(id, "Asia/Novosibirsk", StringComparison.OrdinalIgnoreCase))
        {
            return FromHours(4);
        }

        if (TryParseUtcOffsetHours(id) is not int utcOffset)
        {
            return null;
        }

        int hours = utcOffset - MoscowUtcOffsetHours;

        return hours is < MinHours or > MaxHours ? null : new MoscowOffset(hours);
    }

    public override string ToString() => Hours switch
    {
        0 => "Москва",
        > 0 => $"Москва+{Hours}",
        _ => $"Москва{Hours}",
    };

    private static int? TryParseUtcOffsetHours(string id)
    {
        if (id.Equals("UTC", StringComparison.OrdinalIgnoreCase)
            || id.Equals("Etc/UTC", StringComparison.OrdinalIgnoreCase)
            || id.Equals("Etc/GMT", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (!id.StartsWith("Etc/GMT", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string tail = id["Etc/GMT".Length..];
        if (!int.TryParse(tail, out int etcValue))
        {
            return null;
        }

        int utcOffset = -etcValue;

        return utcOffset is < MinUtcOffsetHours or > MaxUtcOffsetHours ? null : utcOffset;
    }
}
