using FluentAssertions;
using Med.Domain.Entities;
using Med.Presentation.Medications;
using Xunit;

namespace Med.Presentation.Tests.Medications;

public sealed class MedicationCardTests
{
    [Theory]
    [InlineData(null, 2)]
    [InlineData("", 2)]
    [InlineData("invalid", 2)]
    [InlineData("slots:1", 1)]
    [InlineData("slots:5", 5)]
    [InlineData("slots:9", 9)]
    [InlineData("slots:0", 2)]
    [InlineData("slots:12", 2)]
    [InlineData("3", 3)]
    public void ParseSlots_Корректно_определяет_количество_чекбоксов(string? barcode, int expected)
    {
        int actual = MedicationCardViewModel.ParseSlots(barcode);
        actual.Should().Be(expected);
    }

    [Fact]
    public void MedicationCardViewModel_Создает_слоты_от_1_до_N()
    {
        Medication med = Medication.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Тест",
            "tablet",
            "1 шт",
            "шт",
            barcode: "slots:4");

        MedicationCardViewModel vm = new(med);

        vm.CheckboxCount.Should().Be(4);
        vm.Slots.Should().HaveCount(4);
        vm.Slots.Select(s => s.Number).Should().Equal(1, 2, 3, 4);
        vm.Slots.All(s => !s.IsChecked).Should().BeTrue();
    }
}
