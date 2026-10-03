using FluentAssertions;
using Med.Domain.Entities;
using Xunit;
using InventoryEntity = Med.Domain.Entities.Inventory;

namespace Med.Domain.Tests.Entities;

public sealed class MedicationTests
{
    [Fact]
    public void Create_с_пустым_именем_бросает_исключение()
    {
        var act = () => Medication.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            name: "   ",
            form: "Таблетки",
            dosage: "10 мг",
            unit: "таб");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Мутация_with_пустым_именем_бросает_исключение()
    {
        Medication med = Medication.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            name: "Аспирин",
            form: "Таблетки",
            dosage: "100 мг",
            unit: "таб");

        var act = () => med with { Name = "  " };

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Medication_WithEmptyName_ThrowsArgumentException()
    {
        var med = Medication.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Аспирин",
            "Таблетки",
            "100 мг",
            "таб");

        Assert.Throws<ArgumentException>(() => med with { Name = "" });
    }

    [Fact]
    public void Инвентарь_не_позволяет_отрицательный_остаток_через_with()
    {
        InventoryEntity inv = InventoryEntity.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            quantityOnHand: 10,
            lowStockThreshold: 2);

        var act = () => inv with { QuantityOnHand = -1 };

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
