using Med.Domain.Abstractions;

namespace Med.Domain.Tests.Fakes;

/// <summary>
/// Управляемые часы для тестов границ времени. Файл линкуется и в Med.Application.Tests,
/// чтобы не заводить отдельный проект тест-кита.
/// </summary>
public sealed class FakeClock(DateTimeOffset utcNow) : ISystemClock
{
    public DateTimeOffset UtcNow { get; private set; } = utcNow.ToUniversalTime();

    public static FakeClock At(string utcIso) =>
        new(DateTimeOffset.Parse(utcIso, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal));

    public void Advance(TimeSpan delta) => UtcNow = UtcNow.Add(delta);

    public void Set(DateTimeOffset utcNow) => UtcNow = utcNow.ToUniversalTime();
}
