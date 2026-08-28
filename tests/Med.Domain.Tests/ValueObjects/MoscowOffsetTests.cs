using FluentAssertions;
using Med.Domain.ValueObjects;
using Xunit;

namespace Med.Domain.Tests.ValueObjects;

public sealed class MoscowOffsetTests
{
    [Fact]
    public void По_умолчанию_Москва_без_смещения()
    {
        MoscowOffset offset = MoscowOffset.Moscow;

        offset.Hours.Should().Be(0);
        offset.ToTimeZoneId().Should().Be("Europe/Moscow");
        offset.ToString().Should().Be("Москва");
    }

    [Theory]
    [InlineData(0, "Europe/Moscow")]
    [InlineData(4, "Etc/GMT-7")]
    [InlineData(-2, "Etc/GMT-1")]
    [InlineData(-3, "Etc/UTC")]
    [InlineData(-4, "Etc/GMT+1")]
    [InlineData(MoscowOffset.MinHours, "Etc/GMT+9")]
    [InlineData(MoscowOffset.MaxHours, "Etc/GMT-14")]
    public void Смещение_приводится_к_фиксированной_зоне(int hours, string expectedId)
    {
        MoscowOffset.FromHours(hours).ToTimeZoneId().Should().Be(expectedId);
    }

    [Theory]
    [InlineData(0, 3)]
    [InlineData(4, 7)]
    [InlineData(-2, 1)]
    [InlineData(-3, 0)]
    [InlineData(MoscowOffset.MinHours, -9)]
    [InlineData(MoscowOffset.MaxHours, 14)]
    public void Зона_смещения_даёт_ожидаемый_offset_от_UTC(int hours, int expectedUtcOffsetHours)
    {
        TimeZoneInfo zone = TimeZoneInfo.FindSystemTimeZoneById(MoscowOffset.FromHours(hours).ToTimeZoneId());

        zone.GetUtcOffset(new DateTime(2026, 8, 25, 12, 0, 0, DateTimeKind.Unspecified))
            .Should().Be(TimeSpan.FromHours(expectedUtcOffsetHours));
    }

    [Theory]
    [InlineData(MoscowOffset.MinHours - 1)]
    [InlineData(MoscowOffset.MaxHours + 1)]
    [InlineData(-100)]
    [InlineData(24)]
    public void Смещение_вне_диапазона_отклоняется(int hours)
    {
        Action act = () => MoscowOffset.FromHours(hours);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Обратное_преобразование_работает_для_всего_диапазона()
    {
        for (int hours = MoscowOffset.MinHours; hours <= MoscowOffset.MaxHours; hours++)
        {
            string id = MoscowOffset.FromHours(hours).ToTimeZoneId();

            MoscowOffset.TryFromTimeZoneId(id)!.Value.Hours.Should().Be(hours);
        }
    }

    [Theory]
    [InlineData("UTC", -3)]
    [InlineData("Etc/GMT", -3)]
    [InlineData("europe/moscow", 0)]
    public void Исторические_значения_профиля_читаются(string timeZoneId, int expectedHours)
    {
        MoscowOffset.TryFromTimeZoneId(timeZoneId)!.Value.Hours.Should().Be(expectedHours);
    }

    [Theory]
    [InlineData("Europe/Berlin")]
    [InlineData("Etc/GMT-15")]
    [InlineData("Etc/GMTX")]
    [InlineData("")]
    [InlineData(null)]
    public void Незнакомая_зона_не_сводится_к_смещению(string? timeZoneId)
    {
        MoscowOffset.TryFromTimeZoneId(timeZoneId).Should().BeNull();
    }

    [Theory]
    [InlineData(0, "Москва")]
    [InlineData(4, "Москва+4")]
    [InlineData(-2, "Москва-2")]
    public void Подпись_смещения_читаема(int hours, string expected)
    {
        MoscowOffset.FromHours(hours).ToString().Should().Be(expected);
    }
}
