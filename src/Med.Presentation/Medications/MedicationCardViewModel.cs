using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Med.Domain.Entities;

namespace Med.Presentation.Medications;

/// <summary>Слот отдельного чекбокса приема лекарства (номер 1..9 и состояние).</summary>
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

/// <summary>Карточка лекарства со списком динамических чекбоксов (от 1 до 9).</summary>
public sealed partial class MedicationCardViewModel : ViewModelBase
{
    public Medication Medication { get; }

    public Guid Id => Medication.Id;
    public string Name => Medication.Name;
    public string Form => Medication.Form;
    public string Dosage => Medication.Dosage;
    public string Unit => Medication.Unit;
    public string? Notes => Medication.Notes;

    public int CheckboxCount { get; }

    public ObservableCollection<CheckboxSlotViewModel> Slots { get; }

    public MedicationCardViewModel(Medication medication)
    {
        Medication = medication;
        CheckboxCount = ParseSlots(medication.Barcode);
        Slots = new ObservableCollection<CheckboxSlotViewModel>(
            Enumerable.Range(1, CheckboxCount).Select(n => new CheckboxSlotViewModel(n)));
    }

    /// <summary>Извлечение количества слотов чекбоксов (1..9), по умолчанию 2.</summary>
    public static int ParseSlots(string? barcode)
    {
        if (string.IsNullOrWhiteSpace(barcode))
        {
            return 2;
        }

        if (barcode.StartsWith("slots:", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(barcode.AsSpan(6), out int parsed) &&
            parsed is >= 1 and <= 9)
        {
            return parsed;
        }

        if (int.TryParse(barcode, out int direct) && direct is >= 1 and <= 9)
        {
            return direct;
        }

        return 2;
    }
}
