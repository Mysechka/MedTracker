namespace Med.Domain.Entities;

/// <summary>Диагноз пользователя.</summary>
public sealed record Diagnosis(
    Guid Id,
    Guid UserId,
    string Title,
    string? Doctor,
    DateOnly? DiagnosedOn,
    string? Notes)
{
    public static Diagnosis Create(
        Guid id,
        Guid userId,
        string title,
        string? doctor = null,
        DateOnly? diagnosedOn = null,
        string? notes = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);

        return new Diagnosis(
            id,
            userId,
            title.Trim(),
            string.IsNullOrWhiteSpace(doctor) ? null : doctor.Trim(),
            diagnosedOn,
            string.IsNullOrWhiteSpace(notes) ? null : notes.Trim());
    }
}
