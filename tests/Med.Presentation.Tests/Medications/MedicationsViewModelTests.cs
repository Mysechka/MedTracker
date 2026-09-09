using CommunityToolkit.Mvvm.Messaging;
using FluentAssertions;
using Med.Application.Abstractions;
using Med.Application.UseCases;
using Med.Domain.Entities;
using Med.Presentation.Abstractions;
using Med.Presentation.Feedback;
using Med.Presentation.Medications;
using Med.Presentation.Messaging;
using Med.Presentation.Sync;
using Xunit;

namespace Med.Presentation.Tests.Medications;

public sealed class MedicationsViewModelTests
{
    private static readonly Guid TestUserId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task MedicationsViewModel_Сохраняет_выбранные_теги_времени_при_добавлении()
    {
        FakeMedicationRepo medRepo = new();
        FakeInventoryRepo invRepo = new();
        FakeCourseRepo courseRepo = new();
        FakeScheduleRepo schedRepo = new();
        FakeAuth auth = new(TestUserId);
        UserFeedback feedback = new(new FakeSnackbar());
        QueuedUiDispatcher dispatcher = new();
        WeakReferenceMessenger messenger = new();
        EntityChangeDeduplicator deduplicator = new();

        MedicationsViewModel vm = new(
            medRepo,
            invRepo,
            courseRepo,
            schedRepo,
            auth,
            new RestockInventoryUseCase(new FakeInventoryCommands()),
            feedback,
            dispatcher,
            messenger,
            deduplicator);

        vm.StartNewCommand.Execute(null);
        vm.Name = "Омега-3";
        vm.Notes = "Капсулу не разжевывать";
        vm.Dosage = "1000";
        vm.SetCheckboxCountCommand.Execute(1);
        vm.TimeAfternoon = true; // Днем

        await vm.SaveCommand.ExecuteAsync(null);

        vm.Items.Should().HaveCount(1);
        MedicationCardViewModel card = vm.Items[0];
        card.Name.Should().Be("Омега-3");
        card.Notes.Should().Be("Капсулу не разжевывать");
        card.CheckboxCount.Should().Be(1);
        card.Tags.Should().Equal("Днем");
        card.AllChips.Should().Equal("Доза: 1000", "Днем");
    }

    [Fact]
    public async Task MedicationsViewModel_Позволяет_изменить_теги_лекарства_при_редактировании()
    {
        FakeMedicationRepo medRepo = new();
        FakeInventoryRepo invRepo = new();
        FakeCourseRepo courseRepo = new();
        FakeScheduleRepo schedRepo = new();
        FakeAuth auth = new(TestUserId);
        UserFeedback feedback = new(new FakeSnackbar());
        QueuedUiDispatcher dispatcher = new();
        WeakReferenceMessenger messenger = new();
        EntityChangeDeduplicator deduplicator = new();

        Medication existingMed = Medication.Create(
            Guid.NewGuid(),
            TestUserId,
            "Витамин C",
            "tablet",
            "500 мг",
            "шт",
            barcode: "slots:2;tags:Утром");
        await medRepo.UpsertAsync(existingMed, TestContext.Current.CancellationToken);

        MedicationsViewModel vm = new(
            medRepo,
            invRepo,
            courseRepo,
            schedRepo,
            auth,
            new RestockInventoryUseCase(new FakeInventoryCommands()),
            feedback,
            dispatcher,
            messenger,
            deduplicator);

        await vm.RefreshCommand.ExecuteAsync(null);
        vm.Items.Should().HaveCount(1);
        MedicationCardViewModel initialCard = vm.Items[0];
        initialCard.Tags.Should().Equal("Утром");

        // Кликаем по карандашу редактирования
        vm.EditMedicationCommand.Execute(initialCard);
        vm.TimeMorning.Should().BeTrue();
        vm.TimeAfternoon.Should().BeFalse();
        vm.TimeEvening.Should().BeFalse();

        // Меняем теги: снимаем Утром, выбираем Днем и Вечером
        vm.TimeMorning = false;
        vm.TimeAfternoon = true;
        vm.TimeEvening = true;

        await vm.SaveCommand.ExecuteAsync(null);

        vm.Items.Should().HaveCount(1);
        MedicationCardViewModel updatedCard = vm.Items[0];
        updatedCard.Tags.Should().Equal("Днем", "Вечером");
        updatedCard.AllChips.Should().Equal("Доза: 500 мг", "Днем", "Вечером");
    }

    private sealed class QueuedUiDispatcher : IUiDispatcher
    {
        public void Post(Action action) => action();
    }

    private sealed class FakeSnackbar : ISnackbarService
    {
        public Task ShowAsync(string message, string? title = null, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    [Fact]
    public void CheckNextSlot_поочередно_отмечает_чекбоксы_и_ResetSlots_сбрасывает_их()
    {
        Medication med = Medication.Create(Guid.NewGuid(), TestUserId, "Аспирин", "таблетка", "100", "мг", "slots:3");
        MedicationCardViewModel card = new(med);

        card.Slots.Should().HaveCount(3);
        card.Slots.Select(s => s.IsChecked).Should().Equal(false, false, false);

        card.CheckNextSlot();
        card.Slots.Select(s => s.IsChecked).Should().Equal(true, false, false);

        card.CheckNextSlot();
        card.Slots.Select(s => s.IsChecked).Should().Equal(true, true, false);

        card.CheckNextSlot();
        card.Slots.Select(s => s.IsChecked).Should().Equal(true, true, true);

        // Повторный вызов при всех заполненных не падает
        card.CheckNextSlot();
        card.Slots.Select(s => s.IsChecked).Should().Equal(true, true, true);

        card.ResetSlots();
        card.Slots.Select(s => s.IsChecked).Should().Equal(false, false, false);
    }

    [Fact]
    public async Task Receive_DoseEventStatusChangedMessage_Taken_отмечает_чекбокс_лекарства()
    {
        Guid medId = Guid.NewGuid();
        Medication med = Medication.Create(medId, TestUserId, "Витамин C", "шипучая", "1000", "мг", "slots:2");
        FakeMedicationRepo medRepo = new();
        await medRepo.UpsertAsync(med, TestContext.Current.CancellationToken);

        FakeInventoryRepo invRepo = new();
        FakeCourseRepo courseRepo = new();
        FakeScheduleRepo schedRepo = new();
        FakeAuth auth = new(TestUserId);
        UserFeedback feedback = new(new FakeSnackbar());
        QueuedUiDispatcher dispatcher = new();
        WeakReferenceMessenger messenger = new();
        EntityChangeDeduplicator deduplicator = new();

        MedicationsViewModel vm = new(
            medRepo,
            invRepo,
            courseRepo,
            schedRepo,
            auth,
            new RestockInventoryUseCase(new FakeInventoryCommands()),
            feedback,
            dispatcher,
            messenger,
            deduplicator);

        await vm.RefreshCommand.ExecuteAsync(null);
        vm.Items.Should().HaveCount(1);
        MedicationCardViewModel card = vm.Items[0];
        card.Slots.Select(s => s.IsChecked).Should().Equal(false, false);

        // Отправляем первое подтверждение приёма
        messenger.Send(new DoseEventStatusChangedMessage(
            new DoseEventChange(Guid.NewGuid(), Med.Domain.Enums.DoseEventState.Taken, DateTimeOffset.UtcNow, DoseEventChangeType.Update, medId),
            ChangeSource.Local));

        card.Slots.Select(s => s.IsChecked).Should().Equal(true, false);

        // Отправляем второе подтверждение приёма
        messenger.Send(new DoseEventStatusChangedMessage(
            new DoseEventChange(Guid.NewGuid(), Med.Domain.Enums.DoseEventState.Taken, DateTimeOffset.UtcNow, DoseEventChangeType.Update, medId),
            ChangeSource.Local));

        card.Slots.Select(s => s.IsChecked).Should().Equal(true, true);
    }

    private sealed class FakeAuth(Guid userId) : IAuthService
    {
        public AuthSession? CurrentSession => new(userId, "test@example.com", "token", "refresh", DateTimeOffset.UtcNow.AddHours(1));
        public Guid? CurrentUserId => userId;
        public event EventHandler<AuthSession?>? AuthStateChanged { add { } remove { } }
        public Task<AuthSession> SignInWithPasswordAsync(string email, string password, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task SendMagicLinkAsync(string email, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task SignOutAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task UpdatePasswordAsync(string newPassword, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<AuthSession> SignUpWithPasswordAsync(string email, string password, string? username = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeMedicationRepo : IMedicationRepository
    {
        private readonly List<Medication> _items = [];
        public Task<IReadOnlyList<Medication>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Medication>>(_items.ToList());
        public Task<Medication?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(_items.FirstOrDefault(i => i.Id == id));
        public Task UpsertAsync(Medication medication, CancellationToken cancellationToken = default)
        {
            _items.RemoveAll(i => i.Id == medication.Id);
            _items.Add(medication);
            return Task.CompletedTask;
        }
        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
        {
            _items.RemoveAll(i => i.Id == id);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeInventoryRepo : IInventoryRepository
    {
        private readonly List<Inventory> _items = [];
        public Task<Inventory?> GetByMedicationAsync(Guid medicationId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_items.FirstOrDefault(i => i.MedicationId == medicationId));
        public Task UpsertAsync(Inventory inventory, CancellationToken cancellationToken = default)
        {
            _items.RemoveAll(i => i.Id == inventory.Id);
            _items.Add(inventory);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeInventoryCommands : IInventoryCommandService
    {
        public Task<InventoryCommandResult> RestockAsync(Guid medicationId, decimal amount, string? note = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new InventoryCommandResult("Applied", Guid.NewGuid(), amount));
    }

    private sealed class FakeCourseRepo : ICourseRepository
    {
        private readonly List<Course> _items = [];
        public Task<IReadOnlyList<Course>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Course>>(_items.ToList());
        public Task<Course?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(_items.FirstOrDefault(i => i.Id == id));
        public Task UpsertAsync(Course course, CancellationToken cancellationToken = default)
        {
            _items.RemoveAll(i => i.Id == course.Id);
            _items.Add(course);
            return Task.CompletedTask;
        }
        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
        {
            _items.RemoveAll(i => i.Id == id);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeScheduleRepo : IScheduleRepository
    {
        private readonly List<Schedule> _items = [];
        public Task<IReadOnlyList<Schedule>> ListByCourseAsync(Guid courseId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Schedule>>(_items.Where(s => s.CourseId == courseId).ToArray());
        public Task<Schedule?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(_items.FirstOrDefault(s => s.Id == id));
        public Task UpsertAsync(Schedule schedule, CancellationToken cancellationToken = default)
        {
            _items.RemoveAll(s => s.Id == schedule.Id);
            _items.Add(schedule);
            return Task.CompletedTask;
        }
        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
        {
            _items.RemoveAll(s => s.Id == id);
            return Task.CompletedTask;
        }
    }
}
