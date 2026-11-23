namespace Med.Presentation.Settings;

public sealed record WorldTimezoneCity(
    string CityName,
    string Country,
    int UtcOffsetHours,
    int MoscowOffsetHours,
    string TimeZoneId,
    double XPercent,
    double YPercent)
{
    public string FormattedUtc => UtcOffsetHours >= 0 ? $"UTC+{UtcOffsetHours}" : $"UTC{UtcOffsetHours}";
    public string FormattedMsk => MoscowOffsetHours switch
    {
        0 => "МСК",
        > 0 => $"МСК+{MoscowOffsetHours}",
        _ => $"МСК{MoscowOffsetHours}"
    };
    public string FullTitle => $"{FormattedUtc} ({FormattedMsk}) · {CityName}";
    public string ShortTitle => $"{FormattedUtc} · {CityName}";
    public string ZoneBadge => $"{FormattedUtc} · {FormattedMsk}";
}
