using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Med.Application.Abstractions;
using Med.Application.UseCases;
using Med.Domain.Entities;

namespace Med.Presentation.Medications;

public sealed partial class MedicationsViewModel : ViewModelBase
{
    private readonly IMedicationRepository _medications;
    private readonly IInventoryRepository _inventory;
    private readonly IAuthService _auth;
    private readonly RestockInventoryUseCase _restock;

    public MedicationsViewModel(
        IMedicationRepository medications,
        IInventoryRepository inventory,
        IAuthService auth,
        RestockInventoryUseCase restock)
    {
        _medications = medications;
        _inventory = inventory;
        _auth = auth;
        _restock = restock;
    }

    public ObservableCollection<Medication> Items { get; } = [];

    [ObservableProperty]
    private Medication? _selected;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _form = "tablet";

    [ObservableProperty]
    private string _dosage = "1";

    [ObservableProperty]
    private string _unit = "шт";

    [ObservableProperty]
    private string _barcode = string.Empty;

    [ObservableProperty]
    private string _notes = string.Empty;

    [ObservableProperty]
    private string _quantityOnHand = "0";

    [ObservableProperty]
    private string _lowStockThreshold = "0";

    [ObservableProperty]
    private string _restockAmount = "10";

    [ObservableProperty]
    private string _message = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    partial void OnSelectedChanged(Medication? value)
    {
        if (value is null)
        {
            return;
        }

        Name = value.Name;
        Form = value.Form;
        Dosage = value.Dosage;
        Unit = value.Unit;
        Barcode = value.Barcode ?? string.Empty;
        Notes = value.Notes ?? string.Empty;
        _ = LoadInventoryAsync(value.Id);
    }

    [RelayCommand]
    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        await RunAsync(async () =>
        {
            Items.Clear();
            foreach (Medication med in await _medications.ListAsync(cancellationToken))
            {
                Items.Add(med);
            }

            Message = $"Лекарств: {Items.Count}";
        });
    }

    [RelayCommand]
    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        await RunAsync(async () =>
        {
            Guid userId = _auth.CurrentUserId
                ?? throw new InvalidOperationException("Нужна сессия.");

            Guid id = Selected?.Id ?? Guid.NewGuid();
            Medication medication = Medication.Create(
                id,
                userId,
                Name,
                Form,
                Dosage,
                Unit,
                string.IsNullOrWhiteSpace(Barcode) ? null : Barcode,
                string.IsNullOrWhiteSpace(Notes) ? null : Notes);

            await _medications.UpsertAsync(medication, cancellationToken);

            if (!decimal.TryParse(QuantityOnHand, out decimal qty))
            {
                qty = 0;
            }

            if (!decimal.TryParse(LowStockThreshold, out decimal threshold))
            {
                threshold = 0;
            }

            Inventory? existing = await _inventory.GetByMedicationAsync(id, cancellationToken);
            Inventory inventory = Inventory.Create(
                existing?.Id ?? Guid.NewGuid(),
                userId,
                id,
                qty,
                threshold);
            await _inventory.UpsertAsync(inventory, cancellationToken);

            Selected = medication;
            await RefreshAsync(cancellationToken);
            Message = "Сохранено.";
        });
    }

    [RelayCommand]
    private async Task DeleteAsync(CancellationToken cancellationToken)
    {
        if (Selected is null)
        {
            Message = "Выберите лекарство.";
            return;
        }

        await RunAsync(async () =>
        {
            await _medications.DeleteAsync(Selected.Id, cancellationToken);
            Selected = null;
            await RefreshAsync(cancellationToken);
            Message = "Удалено.";
        });
    }

    [RelayCommand]
    private async Task RestockAsync(CancellationToken cancellationToken)
    {
        if (Selected is null)
        {
            Message = "Выберите лекарство.";
            return;
        }

        if (!decimal.TryParse(RestockAmount, out decimal amount) || amount <= 0)
        {
            Message = "Количество пополнения должно быть > 0.";
            return;
        }

        await RunAsync(async () =>
        {
            InventoryCommandResult result = await _restock.ExecuteAsync(
                Selected.Id,
                amount,
                note: "UI restock",
                cancellationToken);
            Message = $"{result.Outcome}; on_hand={result.QuantityOnHand}";
            await LoadInventoryAsync(Selected.Id);
        });
    }

    private async Task LoadInventoryAsync(Guid medicationId)
    {
        try
        {
            Inventory? inventory = await _inventory.GetByMedicationAsync(medicationId);
            QuantityOnHand = inventory?.QuantityOnHand.ToString() ?? "0";
            LowStockThreshold = inventory?.LowStockThreshold.ToString() ?? "0";
        }
        catch (Exception ex)
        {
            Message = ex.Message;
        }
    }

    private async Task RunAsync(Func<Task> action)
    {
        if (IsBusy)
        {
            return;
        }

        try
        {
            IsBusy = true;
            await action();
        }
        catch (Exception ex)
        {
            Message = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
