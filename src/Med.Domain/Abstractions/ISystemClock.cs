namespace Med.Domain.Abstractions;

/// <summary>
/// Единственный допустимый источник времени. Обращения к <see cref="DateTime.UtcNow"/>
/// и <see cref="DateTimeOffset.Now"/> из доменного и прикладного кода запрещены:
/// иначе тесты на границы времени (переход на летнее время, приём в 00:30) невоспроизводимы.
/// </summary>
public interface ISystemClock
{
    /// <summary>Текущий момент в UTC. Локальное время получается конвертацией с явной таймзоной.</summary>
    DateTimeOffset UtcNow { get; }
}
