using FluentAssertions;
using Med.Domain.Scheduling;
using Xunit;

namespace Med.Domain.Tests.Scheduling;

public sealed class LocalTimeConverterTests
{
    [Fact]
    public void Приём_в_00_30_по_Москве_остаётся_на_следующей_локальной_дате()
    {
        TimeZoneInfo moscow = TimeZoneInfo.FindSystemTimeZoneById("Europe/Moscow");
        DateTimeOffset utc = DateTimeOffset.Parse("2026-08-27T21:30:00Z");

        LocalTimeConverter.ToLocalTime(utc, moscow).Should().Be(new TimeOnly(0, 30));
        LocalTimeConverter.ToLocalDate(utc, moscow).Should().Be(new DateOnly(2026, 8, 28));
    }

    [Theory]
    // Лето в Берлине: UTC+2 — 06:00Z даёт 08:00 локально.
    [InlineData("2026-07-15T06:00:00Z", 8, 0)]
    // Зима в Берлине: UTC+1 — те же 06:00Z дают 07:00 локально.
    [InlineData("2026-01-15T06:00:00Z", 7, 0)]
    public void Локальное_время_учитывает_переход_на_летнее_время(string utcIso, int hour, int minute)
    {
        TimeZoneInfo berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");
        DateTimeOffset utc = DateTimeOffset.Parse(utcIso);

        LocalTimeConverter.ToLocalTime(utc, berlin).Should().Be(new TimeOnly(hour, minute));
    }
}
