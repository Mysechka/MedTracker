using FluentAssertions;
using Med.Domain.Abstractions;
using Med.Domain.Tests.Fakes;
using Xunit;

namespace Med.Domain.Tests.Abstractions;

public sealed class SystemClockTests
{
    [Fact]
    public void FakeClock_возвращает_время_в_utc()
    {
        ISystemClock clock = FakeClock.At("2026-03-29T00:30:00+03:00");

        clock.UtcNow.Offset.Should().Be(TimeSpan.Zero);
        clock.UtcNow.Should().Be(new DateTimeOffset(2026, 3, 28, 21, 30, 0, TimeSpan.Zero));
    }

    [Fact]
    public void Advance_сдвигает_время_ровно_на_переданный_интервал()
    {
        FakeClock clock = FakeClock.At("2026-08-24T12:00:00Z");

        clock.Advance(TimeSpan.FromHours(3));

        clock.UtcNow.Should().Be(new DateTimeOffset(2026, 8, 24, 15, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void Iana_таймзоны_доступны_в_рантайме()
    {
        // Материализация dose_events опирается на IANA-идентификаторы,
        // поэтому InvariantGlobalization обязан быть выключен на всех платформах.
        TimeZoneInfo moscow = TimeZoneInfo.FindSystemTimeZoneById("Europe/Moscow");

        TimeSpan winter = moscow.GetUtcOffset(new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc));
        TimeSpan summer = moscow.GetUtcOffset(new DateTime(2026, 7, 15, 12, 0, 0, DateTimeKind.Utc));

        winter.Should().Be(TimeSpan.FromHours(3));
        summer.Should().Be(winter, "Москва не переходит на летнее время с 2011 года");
    }

    [Fact]
    public void Зона_с_переходом_на_летнее_время_доступна_для_тестов_dst()
    {
        // Основные тесты DST стадии 1 нужно писать на зоне с переходами:
        // в московской зоне пропущенного и удвоенного часа не бывает.
        TimeZoneInfo berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

        DateTime skipped = new(2026, 3, 29, 2, 30, 0, DateTimeKind.Unspecified);
        DateTime doubled = new(2026, 10, 25, 2, 30, 0, DateTimeKind.Unspecified);

        berlin.IsInvalidTime(skipped).Should().BeTrue("2:30 в ночь перехода не существует");
        berlin.IsAmbiguousTime(doubled).Should().BeTrue("2:30 в ночь возврата случается дважды");
    }
}
