namespace Med.Domain.Entities;

/// <summary>
/// Лекарственный препарат. Защищён инвариантами от некорректных мутаций через with.
/// </summary>
public sealed record Medication
{
    private readonly string _name = string.Empty;
    private readonly string _form = string.Empty;
    private readonly string _dosage = string.Empty;
    private readonly string _unit = string.Empty;

    public Guid Id { get; init; }
    public Guid UserId { get; init; }

    public string Name
    {
        get => _name;
        init
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
            _name = value.Trim();
        }
    }

    public string Form
    {
        get => _form;
        init
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
            _form = value.Trim();
        }
    }

    public string Dosage
    {
        get => _dosage;
        init
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
            _dosage = value.Trim();
        }
    }

    public string Unit
    {
        get => _unit;
        init
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
            _unit = value.Trim();
        }
    }

    public string? Barcode { get; init; }
    public string? Notes { get; init; }

    public Medication(
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

        Id = id;
        UserId = userId;
        Name = name;
        Form = form;
        Dosage = dosage;
        Unit = unit;
        Barcode = string.IsNullOrWhiteSpace(barcode) ? null : barcode.Trim();
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
    }

    public static Medication Create(
        Guid id,
        Guid userId,
        string name,
        string form,
        string dosage,
        string unit,
        string? barcode = null,
        string? notes = null) =>
        new(id, userId, name, form, dosage, unit, barcode, notes);
}
