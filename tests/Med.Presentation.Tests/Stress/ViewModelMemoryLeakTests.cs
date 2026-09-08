using System.Runtime.CompilerServices;
using CommunityToolkit.Mvvm.Messaging;
using FluentAssertions;
using Med.Application.Abstractions;
using Med.Application.Agenda;
using Med.Application.UseCases;
using Med.Domain.Abstractions;
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

namespace Med.Presentation.Tests.Stress;

public sealed class ViewModelMemoryLeakTests
{
    private static readonly Guid TestUserId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public void TodayViewModel_освобождается_GC_после_закрытия_экрана_и_не_удерживается_WeakReferenceMessenger()
    {
        WeakReferenceMessenger messenger = new();
        WeakReference<TodayViewModel> weakRef = CreateAndDisposeTodayViewModel(messenger);

        ForceGarbageCollection();

        weakRef.TryGetTarget(out TodayViewModel? target).Should().BeFalse(
            "TodayViewModel должен быть собран GC и не удерживаться мессенджером после Dispose");
    }

    [Fact]
    public void MedicationsViewModel_освобождается_GC_после_закрытия_экрана()
    {
        WeakReferenceMessenger messenger = new();
        WeakReference<MedicationsViewModel> weakRef = CreateAndDisposeMedicationsViewModel(messenger);

        ForceGarbageCollection();

        weakRef.TryGetTarget(out MedicationsViewModel? target).Should().BeFalse(
            "MedicationsViewModel должен быть собран GC после закрытия");
    }

    [Fact]
    public void CoursesViewModel_освобождается_GC_после_закрытия_экрана()
    {
        WeakReferenceMessenger messenger = new();
        WeakReference<CoursesViewModel> weakRef = CreateAndDisposeCoursesViewModel(messenger);

        ForceGarbageCollection();

        weakRef.TryGetTarget(out CoursesViewModel? target).Should().BeFalse(
            "CoursesViewModel должен быть собран GC после закрытия");
    }

    [Fact]
    public void Мессенджер_продолжает_работать_после_сбора_мертвых_получателей()
    {
        WeakReferenceMessenger messenger = new();

        _ = CreateAndDisposeMedicationsViewModel(messenger);
        ForceGarbageCollection();

        Action sendAction = () => messenger.Send(new EntitySavedMessage<Medication>(
            Medication.Create(Guid.NewGuid(), TestUserId, "Тест", "таблетка", "10", "мг", null, null),
            ChangeSource.Realtime));

        sendAction.Should().NotThrow();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference<TodayViewModel> CreateAndDisposeTodayViewModel(WeakReferenceMessenger messenger)
    {
        ImmediateUiDispatcher ui = new();
        UserFeedback feedback = new(new NullSnackbar());
        FakeRealtime realtime = new();
        EntityChangeDeduplicator deduplicator = new();

        GetDayAgendaUseCase agenda = new(
            new FakeDoseEventRepo(),
            new FakeScheduleRepo(),
            new FakeCourseRepo(),
            new FakeMedicationRepo(),
            new FakeProfileRepo(),
            new FakeClock());

        TodayViewModel vm = new(
            agenda,
            new ConfirmDoseUseCase(new FakeDoseTransitionService()),
            new SkipDoseUseCase(new FakeDoseTransitionService()),
            new UndoConfirmDoseUseCase(new FakeDoseTransitionService()),
            new MaterializeUpcomingDosesUseCase(new FakeMaterializerService()),
            realtime,
            new FakeAuth(TestUserId),
            ui,
            feedback,
            messenger,
            deduplicator);

        messenger.Send(new ScheduleUpdatedMessage(Guid.NewGuid(), Guid.NewGuid(), ChangeSource.Realtime));

        WeakReference<TodayViewModel> weakRef = new(vm);
        vm.Dispose();

        return weakRef;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference<MedicationsViewModel> CreateAndDisposeMedicationsViewModel(WeakReferenceMessenger messenger)
    {
        ImmediateUiDispatcher ui = new();
        UserFeedback feedback = new(new NullSnackbar());
        EntityChangeDeduplicator deduplicator = new();

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

        messenger.Send(new EntitySavedMessage<Medication>(
            Medication.Create(Guid.NewGuid(), TestUserId, "Анальгин", "таблетка", "500", "мг", null, null),
            ChangeSource.Realtime));

        WeakReference<MedicationsViewModel> weakRef = new(vm);
        vm.Dispose();

        return weakRef;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference<CoursesViewModel> CreateAndDisposeCoursesViewModel(WeakReferenceMessenger messenger)
    {
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

        messenger.Send(new ScheduleUpdatedMessage(Guid.NewGuid(), Guid.NewGuid(), ChangeSource.Realtime));

        WeakReference<CoursesViewModel> weakRef = new(vm);
        vm.Dispose();

        return weakRef;
    }

    private static void ForceGarbageCollection()
    {
        for (int i = 0; i < 3; i++)
        {
            GC.Collect(2, GCCollectionMode.Forced, true, true);
            GC.WaitForPendingFinalizers();
            GC.Collect(2, GCCollectionMode.Forced, true, true);
        }
    }

    private sealed class NullSnackbar : ISnackbarService
    {
        public Task ShowAsync(string message, string? title = null, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class FakeRealtime : IDoseEventRealtime
    {
        public event EventHandler<DoseEventChange>? Changed;
        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void RaiseChanged(DoseEventChange change) => Changed?.Invoke(this, change);
    }

    private sealed class FakeClock : ISystemClock
    {
        public DateTimeOffset UtcNow => DateTimeOffset.Parse("2026-09-01T12:00:00Z");
    }

    private sealed class FakeDoseTransitionService : IDoseTransitionService
    {
        public Task<DoseTransitionResult> ConfirmAsync(Guid doseEventId, DoseEventSource source, DateTimeOffset? takenAt = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new DoseTransitionResult("Applied", doseEventId, DoseEventState.Taken));

        public Task<DoseTransitionResult> SkipAsync(Guid doseEventId, DoseEventSource source, DateTimeOffset? skippedAt = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new DoseTransitionResult("Applied", doseEventId, DoseEventState.Skipped));

        public Task<DoseTransitionResult> UndoConfirmAsync(Guid doseEventId, DateTimeOffset? undoneAt = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new DoseTransitionResult("Applied", doseEventId, DoseEventState.Scheduled));
    }

    private sealed class FakeMaterializerService : IDoseEventMaterializer
    {
        public Task<int> MaterializeAsync(int horizonDays = 14, CancellationToken cancellationToken = default) =>
            Task.FromResult(0);
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
        public Task<IReadOnlyList<Medication>> ListAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Medication>>([]);
        public Task<Medication?> GetAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult<Medication?>(null);
        public Task UpsertAsync(Medication medication, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeInventoryRepo : IInventoryRepository
    {
        public Task<Inventory?> GetByMedicationAsync(Guid medicationId, CancellationToken cancellationToken = default) => Task.FromResult<Inventory?>(null);
        public Task UpsertAsync(Inventory inventory, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeInventoryCommands : IInventoryCommandService
    {
        public Task<InventoryCommandResult> RestockAsync(Guid medicationId, decimal amount, string? note = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new InventoryCommandResult("Applied", Guid.NewGuid(), amount));
    }

    private sealed class FakeCourseRepo : ICourseRepository
    {
        public Task<IReadOnlyList<Course>> ListAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Course>>([]);
        public Task<Course?> GetAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult<Course?>(null);
        public Task UpsertAsync(Course course, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeScheduleRepo : IScheduleRepository
    {
        public Task<IReadOnlyList<Schedule>> ListByCourseAsync(Guid courseId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Schedule>>([]);
        public Task<Schedule?> GetAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult<Schedule?>(null);
        public Task UpsertAsync(Schedule schedule, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeDoseEventRepo : IDoseEventRepository
    {
        public Task<IReadOnlyList<DoseEvent>> ListForLocalDateAsync(DateOnly localDate, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<DoseEvent>>([]);
        public Task<IReadOnlyList<DoseEvent>> ListByScheduleAsync(Guid scheduleId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<DoseEvent>>([]);
        public Task<DoseEvent?> GetAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult<DoseEvent?>(null);
        public Task UpsertManyAsync(IReadOnlyList<DoseEvent> events, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeProfileRepo : IProfileRepository
    {
        public Task<Profile?> GetCurrentAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<Profile?>(Profile.Create(TestUserId, "u", "Europe/Moscow", new MealWindows(new TimeOnly(8, 0), new TimeOnly(13, 0), new TimeOnly(19, 0)), TimeSpan.FromHours(1)));
        public Task UpdateAsync(Profile profile, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
