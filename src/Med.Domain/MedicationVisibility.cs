using Med.Domain.Entities;

namespace Med.Domain;

/// <summary>
/// Доменные правила видимости лекарств на основе назначенных курсов.
/// </summary>
public static class MedicationVisibility
{
    /// <summary>
    /// Проверяет видимость лекарства на указанную дату по списку его курсов.
    /// <list type="bullet">
    /// <item><description>Список курсов пуст — лекарство видимо (true).</description></item>
    /// <item><description>Хотя бы один курс бессрочный (EffectiveEndsOn == DateOnly.MaxValue) — лекарство видимо (true).</description></item>
    /// <item><description>Хотя бы один курс включает указанную дату (ContainsDate) — лекарство видимо (true).</description></item>
    /// <item><description>Все курсы завершились — лекарство скрывается (false).</description></item>
    /// </list>
    /// </summary>
    public static bool IsVisibleOnDate(IReadOnlyList<Course> coursesForMedication, DateOnly today)
    {
        if (coursesForMedication is null || coursesForMedication.Count == 0)
        {
            return true;
        }

        foreach (Course course in coursesForMedication)
        {
            if (course.EffectiveEndsOn == DateOnly.MaxValue)
            {
                return true;
            }

            if (course.ContainsDate(today))
            {
                return true;
            }
        }

        return false;
    }
}
