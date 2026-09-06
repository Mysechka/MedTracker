using CommunityToolkit.Mvvm.Messaging;
using FluentAssertions;
using Med.Application.Abstractions;
using Med.Application.Agenda;
using Med.Application.UseCases;
using Med.Domain.Entities;
using Med.Domain.Enums;
using Med.Domain.ValueObjects;
using Med.Presentation.Abstractions;
using Med.Presentation.Courses;
using Med.Presentation.Feedback;
using Med.Presentation.Medications;
using Med.Presentation.Messaging;
using Med.Presentation.Sync;
using Med.Presentation.Today;
using Xunit;

namespace Med.Presentation.Tests.Sync;

public sealed class StateSyncAndRealtimeTests
{
    private static readonly Guid TestUserId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public void Deduplicator_подавляет_повторные_эхо_события_внутри_окна()
    {
        EntityChangeDeduplicator deduplicator = new(TimeSpan.FromSeconds(2));
        Guid entityId = Guid.NewGuid();

        deduplicator.IsDuplicateOrRecentLocal<Medication>(entityId).Should().BeFalse();

        deduplicator.RecordLocalChange<Medication>(entityId);

        deduplicator.IsDuplicateOrRecentLocal<Medication>(entityId).Should().BeTrue();
        deduplicator.IsDuplicateOrRecentLocal<Course>(entityId).Should().BeFalse();
    }

    [Fact]
    public void RealtimeStateSynchronizer_транслирует_внешние_события_в_сообщения_мессенджера()
    {
        FakeEntityRealtimeSync realtime = new();
        EntityChangeDeduplicator deduplicator = new();
        WeakReferenceMessenger messenger = new();

        using RealtimeStateSynchronizer synchronizer = new(realtime, deduplicator, messenger);

        EntitySavedMessage<Medication>? receivedMedSaved = null;
        EntityDeletedMessage<Medication>? receivedMedDeleted = null;
        ScheduleUpdatedMessage? receivedScheduleUpdated = null;
        DoseEventStatusChangedMessage? receivedDoseChanged = null;

        messenger.Register<EntitySavedMessage<Medication>>(this, (_, m) => receivedMedSaved = m);
        messenger.Register<EntityDeletedMessage<Medication>>(this, (_, m) => receivedMedDeleted = m);
        messenger.Register<ScheduleUpdatedMessage>(this, (_, m) => receivedScheduleUpdated = m);
        messenger.Register<DoseEventStatusChangedMessage>(this, (_, m) => receivedDoseChanged = m);

        Guid medId = Guid.NewGuid();
        Medication medication = Medication.Create(medId, TestUserId, "Ибупрофен", "таблетка", "200", "мг", null, null);

        // 1. Внешнее сохранение лекарства
        realtime.FireChanged(new EntityChangeNotification(
            "medications",
            medId,
            EntityChangeType.Insert,
            DateTimeOffset.UtcNow,
            medication));

        receivedMedSaved.Should().NotBeNull();
        receivedMedSaved!.Value.Name.Should().Be("Ибупрофен");
        receivedMedSaved.Source.Should().Be(ChangeSource.Realtime);

        // 2. Внешнее удаление лекарства
        realtime.FireChanged(new EntityChangeNotification(
            "medications",
            medId,
            EntityChangeType.Delete,
            DateTimeOffset.UtcNow));

        receivedMedDeleted.Should().NotBeNull();
        receivedMedDeleted!.Value.Should().Be(medId);
        receivedMedDeleted.Source.Should().Be(ChangeSource.Realtime);

        // 3. Внешнее изменение расписания
        realtime.FireChanged(new EntityChangeNotification(
            "schedules",
            Guid.NewGuid(),
            EntityChangeType.Update,
            DateTimeOffset.UtcNow));

        receivedScheduleUpdated.Should().NotBeNull();
        receivedScheduleUpdated!.Source.Should().Be(ChangeSource.Realtime);

        // 4. Внешнее изменение статуса дозы
        Guid doseId = Guid.NewGuid();
        DoseEventChange doseChange = new(doseId, DoseEventState.Taken, DateTimeOffset.UtcNow, DoseEventChangeType.Update);
        realtime.FireChanged(new EntityChangeNotification(
            "dose_events",
            doseId,
            EntityChangeType.Update,
            DateTimeOffset.UtcNow,
            doseChange));

        receivedDoseChanged.Should().NotBeNull();
        receivedDoseChanged!.Value.Id.Should().Be(doseId);
        receivedDoseChanged.Value.State.Should().Be(DoseEventState.Taken);
        receivedDoseChanged.Source.Should().Be(ChangeSource.Realtime);
    }

    [Fact]
    public void RealtimeStateSynchronizer_не_пропускает_эхо_локального_изменения()
    {
        FakeEntityRealtimeSync realtime = new();
        EntityChangeDeduplicator deduplicator = new(TimeSpan.FromSeconds(5));
        WeakReferenceMessenger messenger = new();

        using RealtimeStateSynchronizer synchronizer = new(realtime, deduplicator, messenger);

        bool messageSent = false;
        messenger.Register<EntitySavedMessage<Medication>>(this, (_, _) => messageSent = true);

        Guid medId = Guid.NewGuid();
        Medication medication = Medication.Create(medId, TestUserId, "Но-Шпа", "таблетка", "40", "мг", null, null);

        // Локально зафиксировали
        deduplicator.RecordLocalChange<Medication>(medId);

        // Эхо от Realtime
        realtime.FireChanged(new EntityChangeNotification(
            "medications",
            medId,
            EntityChangeType.Update,
            DateTimeOffset.UtcNow,
            medication));

        messageSent.Should().BeFalse("локальное изменение должно быть подавлено дедупликатором");
    }

    [Fact]
    public async Task MedicationsViewModel_реагирует_на_внешнее_Realtime_добавление_и_удаление()
    {
        WeakReferenceMessenger messenger = new();
        ImmediateUiDispatcher ui = new();
        EntityChangeDeduplicator deduplicator = new();
        UserFeedback feedback = new(new FakeSnackbar());

        MedicationsViewModel vm = new(
            new FakeMedicationRepo(),
            new FakeInventoryRepo(),
            new FakeCourseRepo(),
            new FakeScheduleRepo(),
            new FakeAuth(TestUserId),
            new RestockInventoryUseCase(new FakeInventoryCommands()),
            feedback,
            ui,
            messenger,
            deduplicator);

        Guid medId = Guid.NewGuid();
        Medication medication = Medication.Create(medId, TestUserId, "Аспирин", "таблетка", "500", "мг", null, null);

        // Realtime insert
        messenger.Send(new EntitySavedMessage<Medication>(medication, ChangeSource.Realtime));

        vm.Items.Should().ContainSingle(c => c.Id == medId && c.Medication.Name == "Аспирин");
        vm.IsEmpty.Should().BeFalse();

        // Realtime delete
        messenger.Send(new EntityDeletedMessage<Medication>(medId, ChangeSource.Realtime));

        vm.Items.Should().BeEmpty();
        vm.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void CoursesViewModel_реагирует_на_внешнее_Realtime_изменение_лекарств()
    {
        WeakReferenceMessenger messenger = new();
        ImmediateUiDispatcher ui = new();
        EntityChangeDeduplicator deduplicator = new();

        CoursesViewModel vm = new(
            new FakeCourseRepo(),
            new FakeScheduleRepo(),
            new FakeMedicationRepo(),
            new FakeAuth(TestUserId),
            ui,
            messenger,
            deduplicator);

        Guid medId = Guid.NewGuid();
        Medication medication = Medication.Create(medId, TestUserId, "Парацетамол", "таблетка", "500", "мг", null, null);

        // Realtime insert
        messenger.Send(new EntitySavedMessage<Medication>(medication, ChangeSource.Realtime));

        vm.Medications.Should().ContainSingle(m => m.Id == medId && m.Name == "Парацетамол");

        // Realtime delete
        messenger.Send(new EntityDeletedMessage<Medication>(medId, ChangeSource.Realtime));

        vm.Medications.Should().BeEmpty();
    }

    private sealed class FakeEntityRealtimeSync : IEntityRealtimeSync
    {
        public event EventHandler<EntityChangeNotification>? EntityChanged;

        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public void FireChanged(EntityChangeNotification notification) =>
            EntityChanged?.Invoke(this, notification);
    }

    private sealed class FakeSnackbar : ISnackbarService
    {
        public Task ShowAsync(string message, string? title = null, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class FakeAuth(Guid userId) : IAuthService
    {
        public AuthSession? CurrentSession => new(userId, "test@example.com", "token", "refresh", DateTimeOffset.UtcNow.AddHours(1));

        public Guid? CurrentUserId => userId;

        public event EventHandler<AuthSession?>? AuthStateChanged
        {
            add { }
            remove { }
        }

        public Task<AuthSession> SignInWithPasswordAsync(string email, string password, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task SendMagicLinkAsync(string email, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task SignOutAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AuthSession> SignUpWithPasswordAsync(
            string email,
            string password,
            string? username = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeMedicationRepo : IMedicationRepository
    {
        private readonly List<Medication> _items = [];
        public Task<IReadOnlyList<Medication>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Medication>>(_items);
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
        public Task<InventoryCommandResult> RestockAsync(
            Guid medicationId,
            decimal amount,
            string? note = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new InventoryCommandResult("Applied", Guid.NewGuid(), amount));
    }

    private sealed class FakeInventoryTxRepo : IInventoryTransactionRepository
    {
        public Task<IReadOnlyList<InventoryTransaction>> ListByMedicationAsync(
            Guid medicationId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<InventoryTransaction>>([]);
    }

    private sealed class FakeCourseRepo : ICourseRepository
    {
        private readonly List<Course> _items = [];
        public Task<IReadOnlyList<Course>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Course>>(_items);
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
