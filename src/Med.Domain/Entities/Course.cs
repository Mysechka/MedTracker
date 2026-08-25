namespace Med.Domain.Entities;

/// <summary>
/// Курс приёма. <see cref="EndsOn"/> и <see cref="DurationDays"/> хранятся оба;
/// при наличии обоих обязаны согласовываться: EndsOn = StartsOn + (DurationDays - 1).
/// </summary>
public sealed record Course(
    Guid Id,
    Guid UserId,
    Guid MedicationId,
    DateOnly StartsOn,
    DateOnly? EndsOn,
    int? DurationDays,
    bool IsActive,
    Guid? DiagnosisId)
{
    public static Course Create(
        Guid id,
        Guid userId,
        Guid medicationId,
        DateOnly startsOn,
        DateOnly? endsOn,
        int? durationDays,
        bool isActive = true,
        Guid? diagnosisId = null)
    {
        if (endsOn is null && durationDays is null)
        {
            throw new ArgumentException("Нужно указать EndsOn и/или DurationDays.");
        }

        if (durationDays is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(durationDays), "Длительность курса должна быть > 0.");
        }

        if (endsOn is { } end && end < startsOn)
        {
            throw new ArgumentException("EndsOn не может быть раньше StartsOn.", nameof(endsOn));
        }

        if (endsOn is { } e && durationDays is { } d)
        {
            DateOnly expected = startsOn.AddDays(d - 1);
            if (e != expected)
            {
                throw new ArgumentException(
                    $"EndsOn и DurationDays не согласованы: ожидалось EndsOn={expected:O} при DurationDays={d}.");
            }
        }

        return new Course(id, userId, medicationId, startsOn, endsOn, durationDays, isActive, diagnosisId);
    }

    /// <summary>Дата последнего дня курса включительно, либо null если открытый (не должно случаться после Create).</summary>
    public DateOnly EffectiveEndsOn
    {
        get
        {
            if (EndsOn is { } end)
            {
                return end;
            }

            if (DurationDays is { } days)
            {
                return StartsOn.AddDays(days - 1);
            }

            throw new InvalidOperationException("У курса нет ни EndsOn, ни DurationDays.");
        }
    }

    public bool ContainsDate(DateOnly date) => date >= StartsOn && date <= EffectiveEndsOn;
}
