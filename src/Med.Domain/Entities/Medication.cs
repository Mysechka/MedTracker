namespace Med.Domain.Entities;

public sealed record Medication(
    Guid Id,
    Guid UserId,
    string Name,
    string Form,
    string Dosage,
    string Unit,
    string? Barcode,
    string? Notes)
{
    public static Medication Create(
        Guid id,
        Guid userId,
        string name,
        string form,
        string dosage,
        string unit,
        string? barcode = null,
        string? notes = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(form);
        ArgumentException.ThrowIfNullOrWhiteSpace(dosage);
        ArgumentException.ThrowIfNullOrWhiteSpace(unit);

        return new Medication(
            id,
            userId,
            name.Trim(),
            form.Trim(),
            dosage.Trim(),
            unit.Trim(),
            string.IsNullOrWhiteSpace(barcode) ? null : barcode.Trim(),
            string.IsNullOrWhiteSpace(notes) ? null : notes.Trim());
    }
}
