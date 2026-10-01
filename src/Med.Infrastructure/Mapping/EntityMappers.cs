using System.Globalization;
using Med.Domain.Entities;
using Med.Domain.Enums;
using Med.Domain.ValueObjects;
using Med.Infrastructure.Models;

namespace Med.Infrastructure.Mapping;

public static class EntityMappers
{
    public static Profile ToDomain(ProfileRow row) =>
        Profile.Create(
            row.Id,
            row.Username,
            row.TimeZoneId,
            new MealWindows(
                TimeOnly.FromTimeSpan(row.BreakfastTime),
                TimeOnly.FromTimeSpan(row.LunchTime),
                TimeOnly.FromTimeSpan(row.DinnerTime)),
            TimeSpan.FromMinutes(row.ConfirmationWindowMinutes));

    public static ProfileRow ToRow(Profile profile) => new()
    {
        Id = profile.UserId,
        Username = profile.Username,
        TimeZoneId = profile.TimeZoneId,
        BreakfastTime = profile.Meals.Breakfast.ToTimeSpan(),
        LunchTime = profile.Meals.Lunch.ToTimeSpan(),
        DinnerTime = profile.Meals.Dinner.ToTimeSpan(),
        ConfirmationWindowMinutes = (int)profile.ConfirmationWindow.TotalMinutes,
    };

    public static Medication ToDomain(MedicationRow row) =>
        Medication.Create(
            row.Id,
            row.UserId,
            row.Name,
            row.Form,
            row.Dosage,
            row.Unit,
            row.Barcode,
            row.Notes);

    public static MedicationRow ToRow(Medication medication) => new()
    {
        Id = medication.Id,
        UserId = medication.UserId,
        Name = medication.Name,
        Form = medication.Form,
        Dosage = medication.Dosage,
        Unit = medication.Unit,
        Barcode = medication.Barcode,
        Notes = medication.Notes,
    };

    public static Course ToDomain(CourseRow row) =>
        Course.Create(
            row.Id,
            row.UserId,
            row.MedicationId,
            ParseDateOnly(row.StartsOn),
            row.EndsOn is null ? null : ParseDateOnly(row.EndsOn),
            row.DurationDays,
            row.IsActive,
            row.DiagnosisId);

    public static CourseRow ToRow(Course course) => new()
    {
        Id = course.Id,
        UserId = course.UserId,
        MedicationId = course.MedicationId,
        StartsOn = FormatDateOnly(course.StartsOn),
        EndsOn = course.EndsOn is null ? null : FormatDateOnly(course.EndsOn.Value),
        DurationDays = course.DurationDays,
        IsActive = course.IsActive,
        DiagnosisId = course.DiagnosisId,
    };

    public static Schedule ToDomain(ScheduleRow row)
    {
        WeekDays days = FromDayBools(row);
        ScheduleType type = ParseEnum<ScheduleType>(row.Type);
        IReadOnlyList<TimeOnly> fixedTimes = (row.FixedTimes ?? [])
            .Select(TimeOnly.FromTimeSpan)
            .OrderBy(static t => t)
            .ToArray();

        return type switch
        {
            ScheduleType.FixedTimes => Schedule.CreateFixedTimes(
                row.Id,
                row.CourseId,
                days,
                row.DoseAmount,
                fixedTimes),
            ScheduleType.Interval => Schedule.CreateInterval(
                row.Id,
                row.CourseId,
                days,
                row.DoseAmount,
                row.IntervalHours ?? throw new InvalidOperationException("interval_hours обязателен для Interval."),
                TimeOnly.FromTimeSpan(
                    row.IntervalAnchorTime
                        ?? throw new InvalidOperationException("interval_anchor_time обязателен для Interval.")),
                row.EveryNDays),
            ScheduleType.MealRelative => Schedule.CreateMealRelative(
                row.Id,
                row.CourseId,
                days,
                row.DoseAmount,
                ParseEnum<MealKind>(row.MealKind ?? throw new InvalidOperationException("meal_kind обязателен.")),
                ParseEnum<MealRelation>(row.MealRelation),
                row.OffsetMinutes),
            ScheduleType.AsNeeded => Schedule.CreateAsNeeded(row.Id, row.CourseId, row.DoseAmount),
            _ => throw new InvalidOperationException($"Неизвестный тип расписания: {row.Type}."),
        };
    }

    public static ScheduleRow ToRow(Schedule schedule, Guid userId)
    {
        ApplyDayBools(schedule.DaysOfWeek, out bool monday, out bool tuesday, out bool wednesday,
            out bool thursday, out bool friday, out bool saturday, out bool sunday);

        return new ScheduleRow
        {
            Id = schedule.Id,
            UserId = userId,
            CourseId = schedule.CourseId,
            Type = schedule.Type.ToString(),
            Monday = monday,
            Tuesday = tuesday,
            Wednesday = wednesday,
            Thursday = thursday,
            Friday = friday,
            Saturday = saturday,
            Sunday = sunday,
            DoseAmount = schedule.DoseAmount,
            FixedTimes = schedule.FixedTimes.Select(static t => t.ToTimeSpan()).ToList(),
            IntervalHours = schedule.IntervalHours,
            IntervalAnchorTime = schedule.IntervalAnchorTime?.ToTimeSpan(),
            EveryNDays = schedule.EveryNDays,
            MealKind = schedule.MealKind?.ToString(),
            MealRelation = schedule.MealRelation.ToString(),
            OffsetMinutes = schedule.OffsetMinutes,
        };
    }

    public static DoseEvent ToDomain(DoseEventRow row) =>
        new(
            row.Id,
            row.CourseId,
            row.ScheduleId,
            row.ScheduledAt.ToUniversalTime(),
            ParseDateOnly(row.LocalDate),
            ParseEnum<DoseEventState>(row.State),
            row.TakenAt?.ToUniversalTime(),
            row.Source is null ? null : ParseEnum<DoseEventSource>(row.Source),
            row.DedupeKey);

    public static DoseEventRow ToRow(DoseEvent doseEvent, Guid userId) => new()
    {
        Id = doseEvent.Id,
        UserId = userId,
        CourseId = doseEvent.CourseId,
        ScheduleId = doseEvent.ScheduleId,
        ScheduledAt = doseEvent.ScheduledAt,
        LocalDate = FormatDateOnly(doseEvent.LocalDate),
        State = doseEvent.State.ToString(),
        TakenAt = doseEvent.TakenAt,
        Source = doseEvent.Source?.ToString(),
        DedupeKey = doseEvent.DedupeKey,
    };

    public static Inventory ToDomain(InventoryRow row) =>
        Inventory.Create(row.Id, row.UserId, row.MedicationId, row.QuantityOnHand, row.LowStockThreshold);

    public static InventoryRow ToRow(Inventory inventory) => new()
    {
        Id = inventory.Id,
        UserId = inventory.UserId,
        MedicationId = inventory.MedicationId,
        QuantityOnHand = inventory.QuantityOnHand,
        LowStockThreshold = inventory.LowStockThreshold,
    };

    public static InventoryTransaction ToDomain(InventoryTransactionRow row) =>
        new(
            row.Id,
            row.InventoryId,
            row.MedicationId,
            row.DoseEventId,
            ParseEnum<InventoryTransactionKind>(row.Kind),
            row.Amount,
            row.CreatedAt.ToUniversalTime(),
            row.Note);

    public static Diagnosis ToDomain(DiagnosisRow row) =>
        Diagnosis.Create(
            row.Id,
            row.UserId,
            row.Title,
            row.Doctor,
            row.DiagnosedOn is null ? null : ParseDateOnly(row.DiagnosedOn),
            row.Notes);

    public static DiagnosisRow ToRow(Diagnosis diagnosis) => new()
    {
        Id = diagnosis.Id,
        UserId = diagnosis.UserId,
        Title = diagnosis.Title,
        Doctor = diagnosis.Doctor,
        DiagnosedOn = diagnosis.DiagnosedOn is null ? null : FormatDateOnly(diagnosis.DiagnosedOn.Value),
        Notes = diagnosis.Notes,
    };

    public static Document ToDomain(DocumentRow row) =>
        Document.Create(
            row.Id,
            row.UserId,
            row.StoragePath,
            row.MimeType,
            row.SizeBytes,
            ParseEnum<DocumentType>(row.DocType),
            row.DiagnosisId,
            row.CourseId);

    public static DocumentRow ToRow(Document document) => new()
    {
        Id = document.Id,
        UserId = document.UserId,
        StoragePath = document.StoragePath,
        MimeType = document.MimeType,
        SizeBytes = document.SizeBytes,
        DocType = document.DocType.ToString(),
        DiagnosisId = document.DiagnosisId,
        CourseId = document.CourseId,
    };

    public static MessengerLink ToDomain(MessengerLinkRow row) =>
        MessengerLink.Create(
            row.Id,
            row.UserId,
            ToMessengerChannelType(row.ChannelType),
            row.ChatId,
            row.ChannelId,
            row.IsConfirmed,
            row.LinkCode);

    public static MessengerLinkRow ToRow(MessengerLink link) => new()
    {
        Id = link.Id,
        UserId = link.UserId,
        ChannelType = ToMessengerChannelDb(link.ChannelType),
        ChatId = link.ChatId,
        ChannelId = link.ChannelId,
        IsConfirmed = link.IsConfirmed,
        LinkCode = link.LinkCode,
    };

    public static NotificationDelivery ToDomain(NotificationDeliveryRow row) =>
        new(
            row.Id,
            row.UserId,
            row.DoseEventId,
            ToNotificationChannelType(row.ChannelType),
            row.MessageId,
            ParseEnum<NotificationDeliveryStatus>(row.Status),
            row.Attempts,
            row.NextRetryAt?.ToUniversalTime(),
            row.LastError);

    public static NotificationDeliveryRow ToRow(NotificationDelivery delivery) => new()
    {
        Id = delivery.Id,
        UserId = delivery.UserId,
        DoseEventId = delivery.DoseEventId,
        ChannelType = ToNotificationChannelDb(delivery.ChannelType),
        MessageId = delivery.MessageId,
        Status = delivery.Status.ToString(),
        Attempts = delivery.Attempts,
        NextRetryAt = delivery.NextRetryAt,
        LastError = delivery.LastError,
    };

    public static MessengerChannelType ToMessengerChannelType(string value) => value switch
    {
        "telegram" => MessengerChannelType.Telegram,
        "discord" => MessengerChannelType.Discord,
        _ => throw new InvalidOperationException($"Неизвестный messenger channel_type: {value}."),
    };

    public static string ToMessengerChannelDb(MessengerChannelType value) => value switch
    {
        MessengerChannelType.Telegram => "telegram",
        MessengerChannelType.Discord => "discord",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };

    public static NotificationChannelType ToNotificationChannelType(string value) => value switch
    {
        "telegram" => NotificationChannelType.Telegram,
        "discord_app" => NotificationChannelType.DiscordApp,
        "discord_webhook" => NotificationChannelType.DiscordWebhook,
        _ => throw new InvalidOperationException($"Неизвестный notification channel_type: {value}."),
    };

    public static string ToNotificationChannelDb(NotificationChannelType value) => value switch
    {
        NotificationChannelType.Telegram => "telegram",
        NotificationChannelType.DiscordApp => "discord_app",
        NotificationChannelType.DiscordWebhook => "discord_webhook",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };

    private static WeekDays FromDayBools(ScheduleRow row)
    {
        WeekDays days = WeekDays.None;
        if (row.Monday)
        {
            days |= WeekDays.Monday;
        }

        if (row.Tuesday)
        {
            days |= WeekDays.Tuesday;
        }

        if (row.Wednesday)
        {
            days |= WeekDays.Wednesday;
        }

        if (row.Thursday)
        {
            days |= WeekDays.Thursday;
        }

        if (row.Friday)
        {
            days |= WeekDays.Friday;
        }

        if (row.Saturday)
        {
            days |= WeekDays.Saturday;
        }

        if (row.Sunday)
        {
            days |= WeekDays.Sunday;
        }

        return days;
    }

    private static void ApplyDayBools(
        WeekDays days,
        out bool monday,
        out bool tuesday,
        out bool wednesday,
        out bool thursday,
        out bool friday,
        out bool saturday,
        out bool sunday)
    {
        monday = days.HasFlag(WeekDays.Monday);
        tuesday = days.HasFlag(WeekDays.Tuesday);
        wednesday = days.HasFlag(WeekDays.Wednesday);
        thursday = days.HasFlag(WeekDays.Thursday);
        friday = days.HasFlag(WeekDays.Friday);
        saturday = days.HasFlag(WeekDays.Saturday);
        sunday = days.HasFlag(WeekDays.Sunday);
    }

    private static DateOnly ParseDateOnly(string value) =>
        DateOnly.Parse(value, CultureInfo.InvariantCulture);

    private static string FormatDateOnly(DateOnly value) =>
        value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static TEnum ParseEnum<TEnum>(string value)
        where TEnum : struct, Enum =>
        Enum.Parse<TEnum>(value, ignoreCase: true);
}
