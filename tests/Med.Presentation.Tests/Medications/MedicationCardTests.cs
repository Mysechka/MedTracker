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
    [InlineData("slots:6", 6)]
    [InlineData("slots:9", 2)]
    [InlineData("slots:0", 2)]
    [InlineData("slots:12", 2)]
    [InlineData("3", 3)]
    [InlineData("slots:4;tags:Утром,Днем", 4)]
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

    [Fact]
    public void MedicationCardViewModel_Ограничивает_чекбоксы_максимумом_6()
    {
        Medication med = Medication.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Тест",
            "tablet",
            "1 шт",
            "шт",
            barcode: "slots:9");

        MedicationCardViewModel vm = new(med);

        vm.CheckboxCount.Should().BeLessThanOrEqualTo(6);
        vm.Slots.Should().HaveCountLessThanOrEqualTo(6);
    }

    [Fact]
    public void MedicationCardViewModel_Парсит_теги_и_формирует_AllChips()
    {
        Medication med = Medication.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Пустырник",
            "tablet",
            "1 таблетка в прием пищи",
            "шт",
            barcode: "slots:2;tags:Утром,Днем,Вечером");

        MedicationCardViewModel vm = new(med);

        vm.Tags.Should().Equal("Во время завтрака", "Во время обеда", "Во время ужина");
        vm.AllChips.Should().Equal("Доза: 1 таблетка в прием пищи", "Во время завтрака", "Во время обеда", "Во время ужина");
    }

    [Theory]
    [InlineData("slots:1;tags:днем", new[] { "Во время обеда" })]
    [InlineData("slots:1;tags:утром,днем,вечером", new[] { "Во время завтрака", "Во время обеда", "Во время ужина" })]
    [InlineData("slots:1;tags:morning,evening", new[] { "Во время завтрака", "Во время ужина" })]
    [InlineData("утром и вечером", new[] { "Во время завтрака", "Во время ужина" })]
    public void ParseTags_Нормализует_названия_тегов(string barcode, string[] expected)
    {
        IReadOnlyList<string> tags = MedicationCardViewModel.ParseTags(barcode);
        tags.Should().Equal(expected);
    }
}
