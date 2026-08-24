using Med.Domain.Abstractions;

namespace Med.Infrastructure.Time;

/// <summary>
/// Реализация <see cref="ISystemClock"/> поверх <see cref="TimeProvider"/>.
/// Домен зависит от своего интерфейса, а подмена времени в интеграционных тестах
/// делается штатным <c>FakeTimeProvider</c>.
/// </summary>
internal sealed class SystemClock(TimeProvider timeProvider) : ISystemClock
{
    public DateTimeOffset UtcNow => timeProvider.GetUtcNow();
}
