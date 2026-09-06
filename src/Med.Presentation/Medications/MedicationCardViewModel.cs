using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Med.Domain.Entities;

namespace Med.Presentation.Medications;

/// <summary>Слот отдельного чекбокса приема лекарства (номер 1..6 и состояние).</summary>
public sealed partial class CheckboxSlotViewModel : ViewModelBase
{
    public int Number { get; }

    [ObservableProperty]
    private bool _isChecked;

    public CheckboxSlotViewModel(int number, bool isChecked = false)
    {
        Number = number;
        _isChecked = isChecked;
    }
}

/// <summary>Карточка лекарства со списком динамических чекбоксов (от 1 до 6).</summary>
public sealed partial class MedicationCardViewModel : ViewModelBase
{
    public const int MaxCheckboxes = 6;

    public Medication Medication { get; }

    public Guid Id => Medication.Id;
    public string Name => Medication.Name;
    public string Form => Medication.Form;
    public string Dosage => Medication.Dosage;
    public string Unit => Medication.Unit;
    public string? Notes => Medication.Notes;

    public int CheckboxCount { get; }

    public ObservableCollection<CheckboxSlotViewModel> Slots { get; }

    public IReadOnlyList<string> Tags { get; }

    public IReadOnlyList<string> AllChips =>
        string.IsNullOrWhiteSpace(Dosage)
            ? Tags
            : [$"Доза: {Dosage}", ..Tags];

    public MedicationCardViewModel(Medication medication, IEnumerable<string>? tags = null)
    {
        Medication = medication;
        CheckboxCount = Math.Min(ParseSlots(medication.Barcode), MaxCheckboxes);
        Slots = new ObservableCollection<CheckboxSlotViewModel>(
            Enumerable.Range(1, Math.Min(CheckboxCount, MaxCheckboxes))
                .Take(MaxCheckboxes)
                .Select(n => new CheckboxSlotViewModel(n)));
        Tags = tags?.ToList() ?? ParseTags(medication.Barcode);
    }

    /// <summary>Извлечение количества слотов чекбоксов (1..6), по умолчанию 2.</summary>
    public static int ParseSlots(string? barcode)
    {
        if (string.IsNullOrWhiteSpace(barcode))
        {
            return 2;
        }

        string raw = barcode.Trim();

        if (raw.StartsWith("slots:", StringComparison.OrdinalIgnoreCase))
        {
            ReadOnlySpan<char> span = raw.AsSpan(6);
            int sepIdx = span.IndexOfAny(';', '|', ' ');
            if (sepIdx >= 0)
            {
                span = span[..sepIdx];
            }

            if (int.TryParse(span, out int parsed) && parsed is >= 1 and <= MaxCheckboxes)
            {
                return parsed;
            }

            return 2;
        }

        if (int.TryParse(raw, out int direct) && direct is >= 1 and <= MaxCheckboxes)
        {
            return direct;
        }

        return 2;
    }

    /// <summary>Извлечение тегов времени из штрих-кода или метаданных.</summary>
    public static IReadOnlyList<string> ParseTags(string? barcode)
    {
        if (string.IsNullOrWhiteSpace(barcode))
        {
            return [];
        }

        string raw = barcode.Trim();

        int tagsIdx = raw.IndexOf("tags:", StringComparison.OrdinalIgnoreCase);
        if (tagsIdx >= 0)
        {
            string tagsPart = raw[(tagsIdx + 5)..];
            int sepIdx = tagsPart.IndexOfAny([';', '|']);
            if (sepIdx >= 0)
            {
                tagsPart = tagsPart[..sepIdx];
            }

            return tagsPart.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(NormalizeTagName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        List<string> matched = [];
        if (raw.Contains("утром", StringComparison.OrdinalIgnoreCase) || raw.Contains("morning", StringComparison.OrdinalIgnoreCase))
        {
            matched.Add("Утром");
        }
        if (raw.Contains("днем", StringComparison.OrdinalIgnoreCase) || raw.Contains("днём", StringComparison.OrdinalIgnoreCase) || raw.Contains("afternoon", StringComparison.OrdinalIgnoreCase))
        {
            matched.Add("Днем");
        }
        if (raw.Contains("вечером", StringComparison.OrdinalIgnoreCase) || raw.Contains("evening", StringComparison.OrdinalIgnoreCase))
        {
            matched.Add("Вечером");
        }

        return matched;
    }

    private static string NormalizeTagName(string tag)
    {
        if (string.Equals(tag, "morning", StringComparison.OrdinalIgnoreCase))
        {
            return "Утром";
        }
        if (string.Equals(tag, "afternoon", StringComparison.OrdinalIgnoreCase))
        {
            return "Днем";
        }
        if (string.Equals(tag, "evening", StringComparison.OrdinalIgnoreCase))
        {
            return "Вечером";
        }

        return tag;
    }
}
