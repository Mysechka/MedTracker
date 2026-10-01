using Med.Domain.Entities;
using Med.Domain.Enums;
using Med.Domain.State;

namespace Med.Domain.Inventory;

public sealed record InventoryMutationResult(
    TransitionOutcome Outcome,
    DoseEvent DoseEvent,
    Entities.Inventory Inventory,
    InventoryTransaction? Transaction,
    string? Reason = null);

/// <summary>
/// Списание ровно при переходе в Taken, один раз; undo — компенсирующий Credit.
/// </summary>
public static class InventoryRules
{
    public static InventoryMutationResult ApplyTaken(
        DoseEvent doseEvent,
        Entities.Inventory inventory,
        decimal doseAmount,
        DateTimeOffset takenAtUtc,
        DoseEventSource source,
        Guid transactionId)
    {
        if (inventory.MedicationId == Guid.Empty)
        {
            throw new ArgumentException("Inventory без medication_id.", nameof(inventory));
        }

        TransitionResult transition = DoseEventTransitions.MarkTaken(doseEvent, takenAtUtc, source);
        if (transition.Outcome == TransitionOutcome.NoOp)
        {
            return new InventoryMutationResult(
                TransitionOutcome.NoOp,
                transition.Event,
                inventory,
                Transaction: null);
        }

        if (transition.Outcome == TransitionOutcome.Rejected)
        {
            return new InventoryMutationResult(
                TransitionOutcome.Rejected,
                transition.Event,
                inventory,
                Transaction: null,
                transition.Reason);
        }

        if (doseAmount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(doseAmount));
        }

        decimal newQuantity = inventory.QuantityOnHand - doseAmount;
        if (newQuantity < 0)
        {
            return new InventoryMutationResult(
                TransitionOutcome.Rejected,
                transition.Event,
                inventory,
                Transaction: null,
                "Недостаточно остатка на складе.");
        }

        InventoryTransaction tx = InventoryTransaction.Debit(
            transactionId,
            inventory.Id,
            inventory.MedicationId,
            transition.Event.Id,
            doseAmount,
            takenAtUtc);

        Entities.Inventory updated = inventory with
        {
            QuantityOnHand = newQuantity,
        };

        return new InventoryMutationResult(TransitionOutcome.Applied, transition.Event, updated, tx);
    }

    public static InventoryMutationResult UndoTaken(
        DoseEvent doseEvent,
        Entities.Inventory inventory,
        decimal doseAmount,
        DateTimeOffset undoneAtUtc,
        Guid transactionId)
    {
        TransitionResult transition = DoseEventTransitions.UndoTaken(doseEvent);
        if (transition.Outcome != TransitionOutcome.Applied)
        {
            return new InventoryMutationResult(
                transition.Outcome,
                transition.Event,
                inventory,
                Transaction: null,
                transition.Reason);
        }

        if (doseAmount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(doseAmount));
        }

        InventoryTransaction tx = InventoryTransaction.Credit(
            transactionId,
            inventory.Id,
            inventory.MedicationId,
            doseEvent.Id,
            doseAmount,
            undoneAtUtc);

        Entities.Inventory updated = inventory with
        {
            QuantityOnHand = inventory.QuantityOnHand + doseAmount,
        };

        return new InventoryMutationResult(TransitionOutcome.Applied, transition.Event, updated, tx);
    }

    public static (Entities.Inventory Inventory, InventoryTransaction Transaction) Restock(
        Entities.Inventory inventory,
        decimal amount,
        DateTimeOffset atUtc,
        Guid transactionId,
        string? note = null)
    {
        InventoryTransaction tx = InventoryTransaction.Restock(
            transactionId,
            inventory.Id,
            inventory.MedicationId,
            amount,
            atUtc,
            note);

        Entities.Inventory updated = inventory with
        {
            QuantityOnHand = inventory.QuantityOnHand + amount,
        };

        return (updated, tx);
    }
}
