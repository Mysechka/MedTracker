using FluentAssertions;
using Med.Domain.Enums;
using Med.Domain.Inventory;
using Med.Domain.State;
using Med.Domain.Tests.Fakes;
using Med.Domain.Tests.Support;
using Xunit;
using DoseEvent = Med.Domain.Entities.DoseEvent;
using InventoryEntity = Med.Domain.Entities.Inventory;

namespace Med.Domain.Tests.Inventory;

public sealed class InventoryRulesTests
{
    private static DoseEvent Scheduled() =>
        DoseEvent.CreateScheduled(
            Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            DomainFixtures.CourseId,
            DomainFixtures.ScheduleId,
            new DateTimeOffset(2026, 8, 25, 6, 0, 0, TimeSpan.Zero),
            new DateOnly(2026, 8, 25));

    private static InventoryEntity Stock(decimal qty = 10) =>
        InventoryEntity.Create(
            Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
            DomainFixtures.UserId,
            DomainFixtures.MedicationId,
            qty,
            lowStockThreshold: 2);

    [Fact]
    public void Taken_списывает_ровно_один_раз()
    {
        DoseEvent dose = Scheduled();
        InventoryEntity inventory = Stock();
        DateTimeOffset at = FakeClock.At("2026-08-25T06:05:00Z").UtcNow;
        Guid tx1 = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
        Guid tx2 = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");

        InventoryMutationResult first = InventoryRules.ApplyTaken(
            dose, inventory, doseAmount: 1, at, DoseEventSource.App, tx1);
        InventoryMutationResult second = InventoryRules.ApplyTaken(
            first.DoseEvent, first.Inventory, doseAmount: 1, at, DoseEventSource.App, tx2);

        first.Outcome.Should().Be(TransitionOutcome.Applied);
        first.Inventory.QuantityOnHand.Should().Be(9);
        first.Transaction!.Kind.Should().Be(InventoryTransactionKind.Debit);
        first.Transaction.Amount.Should().Be(1);

        second.Outcome.Should().Be(TransitionOutcome.NoOp);
        second.Transaction.Should().BeNull();
        second.Inventory.QuantityOnHand.Should().Be(9);
    }

    [Fact]
    public void Undo_создаёт_компенсирующий_Credit_а_не_удаляет_Debit()
    {
        DoseEvent dose = Scheduled();
        InventoryEntity inventory = Stock(10);
        DateTimeOffset at = FakeClock.At("2026-08-25T06:05:00Z").UtcNow;

        InventoryMutationResult taken = InventoryRules.ApplyTaken(
            dose, inventory, 1.5m, at, DoseEventSource.App,
            Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"));

        InventoryMutationResult undo = InventoryRules.UndoTaken(
            taken.DoseEvent,
            taken.Inventory,
            doseAmount: 1.5m,
            FakeClock.At("2026-08-25T06:20:00Z").UtcNow,
            Guid.Parse("12345678-1234-1234-1234-1234567890ab"));

        undo.Outcome.Should().Be(TransitionOutcome.Applied);
        undo.Inventory.QuantityOnHand.Should().Be(10);
        undo.Transaction!.Kind.Should().Be(InventoryTransactionKind.Credit);
        undo.Transaction.Amount.Should().Be(1.5m);
        undo.Transaction.Note.Should().Be("undo");
        taken.Transaction.Should().NotBeNull("исходный Debit остаётся в журнале");
    }
}
