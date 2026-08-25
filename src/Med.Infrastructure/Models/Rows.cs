using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace Med.Infrastructure.Models;

[Table("profiles")]
public sealed class ProfileRow : BaseModel
{
    [PrimaryKey("id", false)]
    [Column("id")]
    public Guid Id { get; set; }

    [Column("username")]
    public string Username { get; set; } = string.Empty;

    [Column("time_zone_id")]
    public string TimeZoneId { get; set; } = "UTC";

    [Column("breakfast_time")]
    public TimeSpan BreakfastTime { get; set; }

    [Column("lunch_time")]
    public TimeSpan LunchTime { get; set; }

    [Column("dinner_time")]
    public TimeSpan DinnerTime { get; set; }

    [Column("confirmation_window_minutes")]
    public int ConfirmationWindowMinutes { get; set; } = 180;
}

[Table("medications")]
public sealed class MedicationRow : BaseModel
{
    [PrimaryKey("id", false)]
    [Column("id")]
    public Guid Id { get; set; }

    [Column("user_id")]
    public Guid UserId { get; set; }

    [Column("name")]
    public string Name { get; set; } = string.Empty;

    [Column("form")]
    public string Form { get; set; } = string.Empty;

    [Column("dosage")]
    public string Dosage { get; set; } = string.Empty;

    [Column("unit")]
    public string Unit { get; set; } = string.Empty;

    [Column("barcode")]
    public string? Barcode { get; set; }

    [Column("notes")]
    public string? Notes { get; set; }
}

[Table("courses")]
public sealed class CourseRow : BaseModel
{
    [PrimaryKey("id", false)]
    [Column("id")]
    public Guid Id { get; set; }

    [Column("user_id")]
    public Guid UserId { get; set; }

    [Column("medication_id")]
    public Guid MedicationId { get; set; }

    [Column("starts_on")]
    public string StartsOn { get; set; } = string.Empty;

    [Column("ends_on")]
    public string? EndsOn { get; set; }

    [Column("duration_days")]
    public int? DurationDays { get; set; }

    [Column("is_active")]
    public bool IsActive { get; set; } = true;

    [Column("diagnosis_id")]
    public Guid? DiagnosisId { get; set; }
}

[Table("schedules")]
public sealed class ScheduleRow : BaseModel
{
    [PrimaryKey("id", false)]
    [Column("id")]
    public Guid Id { get; set; }

    [Column("user_id")]
    public Guid UserId { get; set; }

    [Column("course_id")]
    public Guid CourseId { get; set; }

    [Column("type")]
    public string Type { get; set; } = string.Empty;

    [Column("monday")]
    public bool Monday { get; set; }

    [Column("tuesday")]
    public bool Tuesday { get; set; }

    [Column("wednesday")]
    public bool Wednesday { get; set; }

    [Column("thursday")]
    public bool Thursday { get; set; }

    [Column("friday")]
    public bool Friday { get; set; }

    [Column("saturday")]
    public bool Saturday { get; set; }

    [Column("sunday")]
    public bool Sunday { get; set; }

    [Column("dose_amount")]
    public decimal DoseAmount { get; set; }

    [Column("fixed_times")]
    public List<TimeSpan>? FixedTimes { get; set; }

    [Column("interval_hours")]
    public int? IntervalHours { get; set; }

    [Column("interval_anchor_time")]
    public TimeSpan? IntervalAnchorTime { get; set; }

    [Column("every_n_days")]
    public int? EveryNDays { get; set; }

    [Column("meal_kind")]
    public string? MealKind { get; set; }

    [Column("meal_relation")]
    public string MealRelation { get; set; } = "Independent";

    [Column("offset_minutes")]
    public int OffsetMinutes { get; set; }
}

[Table("dose_events")]
public sealed class DoseEventRow : BaseModel
{
    [PrimaryKey("id", false)]
    [Column("id")]
    public Guid Id { get; set; }

    [Column("user_id")]
    public Guid UserId { get; set; }

    [Column("course_id")]
    public Guid CourseId { get; set; }

    [Column("schedule_id")]
    public Guid ScheduleId { get; set; }

    [Column("scheduled_at")]
    public DateTimeOffset ScheduledAt { get; set; }

    [Column("local_date")]
    public string LocalDate { get; set; } = string.Empty;

    [Column("state")]
    public string State { get; set; } = "Scheduled";

    [Column("taken_at")]
    public DateTimeOffset? TakenAt { get; set; }

    [Column("source")]
    public string? Source { get; set; }

    [Column("dedupe_key")]
    public string DedupeKey { get; set; } = string.Empty;
}

[Table("inventory")]
public sealed class InventoryRow : BaseModel
{
    [PrimaryKey("id", false)]
    [Column("id")]
    public Guid Id { get; set; }

    [Column("user_id")]
    public Guid UserId { get; set; }

    [Column("medication_id")]
    public Guid MedicationId { get; set; }

    [Column("quantity_on_hand")]
    public decimal QuantityOnHand { get; set; }

    [Column("low_stock_threshold")]
    public decimal LowStockThreshold { get; set; }
}

[Table("inventory_transactions")]
public sealed class InventoryTransactionRow : BaseModel
{
    [PrimaryKey("id", false)]
    [Column("id")]
    public Guid Id { get; set; }

    [Column("user_id")]
    public Guid UserId { get; set; }

    [Column("inventory_id")]
    public Guid InventoryId { get; set; }

    [Column("medication_id")]
    public Guid MedicationId { get; set; }

    [Column("dose_event_id")]
    public Guid? DoseEventId { get; set; }

    [Column("kind")]
    public string Kind { get; set; } = string.Empty;

    [Column("amount")]
    public decimal Amount { get; set; }

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [Column("note")]
    public string? Note { get; set; }
}

[Table("diagnoses")]
public sealed class DiagnosisRow : BaseModel
{
    [PrimaryKey("id", false)]
    [Column("id")]
    public Guid Id { get; set; }

    [Column("user_id")]
    public Guid UserId { get; set; }

    [Column("title")]
    public string Title { get; set; } = string.Empty;

    [Column("doctor")]
    public string? Doctor { get; set; }

    [Column("diagnosed_on")]
    public string? DiagnosedOn { get; set; }

    [Column("notes")]
    public string? Notes { get; set; }
}

[Table("documents")]
public sealed class DocumentRow : BaseModel
{
    [PrimaryKey("id", false)]
    [Column("id")]
    public Guid Id { get; set; }

    [Column("user_id")]
    public Guid UserId { get; set; }

    [Column("storage_path")]
    public string StoragePath { get; set; } = string.Empty;

    [Column("mime_type")]
    public string MimeType { get; set; } = string.Empty;

    [Column("size_bytes")]
    public long SizeBytes { get; set; }

    [Column("doc_type")]
    public string DocType { get; set; } = string.Empty;

    [Column("diagnosis_id")]
    public Guid? DiagnosisId { get; set; }

    [Column("course_id")]
    public Guid? CourseId { get; set; }
}

[Table("messenger_links")]
public sealed class MessengerLinkRow : BaseModel
{
    [PrimaryKey("id", false)]
    [Column("id")]
    public Guid Id { get; set; }

    [Column("user_id")]
    public Guid UserId { get; set; }

    [Column("channel_type")]
    public string ChannelType { get; set; } = string.Empty;

    [Column("chat_id")]
    public string? ChatId { get; set; }

    [Column("channel_id")]
    public string? ChannelId { get; set; }

    [Column("is_confirmed")]
    public bool IsConfirmed { get; set; }

    [Column("link_code")]
    public string? LinkCode { get; set; }
}

[Table("notification_deliveries")]
public sealed class NotificationDeliveryRow : BaseModel
{
    [PrimaryKey("id", false)]
    [Column("id")]
    public Guid Id { get; set; }

    [Column("user_id")]
    public Guid UserId { get; set; }

    [Column("dose_event_id")]
    public Guid DoseEventId { get; set; }

    [Column("channel_type")]
    public string ChannelType { get; set; } = string.Empty;

    [Column("message_id")]
    public string? MessageId { get; set; }

    [Column("status")]
    public string Status { get; set; } = "Pending";

    [Column("attempts")]
    public int Attempts { get; set; }

    [Column("next_retry_at")]
    public DateTimeOffset? NextRetryAt { get; set; }

    [Column("last_error")]
    public string? LastError { get; set; }
}
