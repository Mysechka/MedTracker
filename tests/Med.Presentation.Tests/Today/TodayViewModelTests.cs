using FluentAssertions;
using Med.Application.Abstractions;
using Med.Application.UseCases;
using Med.Domain.Abstractions;
using Med.Domain.Entities;
using Med.Domain.Enums;
using Med.Domain.ValueObjects;
using Med.Presentation.Abstractions;
using Med.Presentation.Today;
using Xunit;

namespace Med.Presentation.Tests.Today;

public sealed class TodayViewModelTests
{
    [Fact]
    public async Task Confirm_вызывает_use_case_и_обновляет_список()
    {
        Guid userId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        Guid doseId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        DateTimeOffset at = DateTimeOffset.Parse("2026-08-27T08:00:00Z");

        DoseEvent dose = DoseEvent.CreateScheduled(
            doseId,
            Guid.Parse("33333333-3333-3333-3333-333333333333"),
            Guid.Parse("44444444-4444-4444-4444-444444444444"),
            at,
            DateOnly.FromDateTime(at.UtcDateTime));

        FakeDoseEvents doses = new([dose]);
        FakeProfiles profiles = new(Profile.Create(
            userId,
            "brenda",
            "UTC",
            new MealWindows(new TimeOnly(8, 0), new TimeOnly(13, 0), new TimeOnly(19, 0))));
        FakeClock clock = new(at);
        FakeTransitions transitions = new();
        FakeRealtime realtime = new();

        TodayViewModel vm = new(
            doses,
            profiles,
            clock,
            new ConfirmDoseUseCase(transitions),
            new SkipDoseUseCase(transitions),
            new UndoConfirmDoseUseCase(transitions),
            new MaterializeUpcomingDosesUseCase(new FakeMaterializer()),
            realtime,
            new ImmediateUiDispatcher());

        await vm.RefreshCommand.ExecuteAsync(null);
        vm.Items.Should().HaveCount(1);
        vm.Selected = vm.Items[0];

        await vm.ConfirmCommand.ExecuteAsync(null);

        transitions.ConfirmCalls.Should().ContainSingle().Which.Should().Be(doseId);
        vm.Message.Should().Contain("Applied");
    }

    [Fact]
    public async Task Realtime_событие_обновляет_список_через_диспетчер_UI()
    {
        Guid userId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        Guid doseId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        DateTimeOffset at = DateTimeOffset.Parse("2026-08-27T08:00:00Z");

        DoseEvent dose = DoseEvent.CreateScheduled(
            doseId,
            Guid.Parse("33333333-3333-3333-3333-333333333333"),
            Guid.Parse("44444444-4444-4444-4444-444444444444"),
            at,
            DateOnly.FromDateTime(at.UtcDateTime));

        FakeRealtime realtime = new();
        RecordingUiDispatcher ui = new();
        FakeTransitions transitions = new();

        TodayViewModel vm = new(
            new FakeDoseEvents([dose]),
            new FakeProfiles(Profile.Create(
                userId,
                "brenda",
                MoscowOffset.Moscow.ToTimeZoneId(),
                new MealWindows(new TimeOnly(8, 0), new TimeOnly(13, 0), new TimeOnly(19, 0)))),
            new FakeClock(at),
            new ConfirmDoseUseCase(transitions),
            new SkipDoseUseCase(transitions),
            new UndoConfirmDoseUseCase(transitions),
            new MaterializeUpcomingDosesUseCase(new FakeMaterializer()),
            realtime,
            ui);

        realtime.Raise(new DoseEventChange(doseId, DoseEventState.Taken, at, DoseEventChangeType.Update));

        // Даём продолжению отработать: обработчик не ждёт завершения загрузки.
        await Task.Yield();

        ui.PostCount.Should().Be(1);
        vm.Items.Should().HaveCount(1);
    }

    private sealed class FakeMaterializer : IDoseEventMaterializer
    {
        public Task<int> MaterializeAsync(int horizonDays = 14, CancellationToken cancellationToken = default) =>
            Task.FromResult(0);
    }

    private sealed class FakeClock(DateTimeOffset utcNow) : ISystemClock
    {
        public DateTimeOffset UtcNow => utcNow;
    }

    private sealed class FakeProfiles(Profile profile) : IProfileRepository
    {
        public Task<Profile?> GetCurrentAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<Profile?>(profile);

        public Task UpdateAsync(Profile profile, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class FakeDoseEvents(IEnumerable<DoseEvent> seed) : IDoseEventRepository
    {
        private readonly List<DoseEvent> _items = seed.ToList();

        public Task<IReadOnlyList<DoseEvent>> ListForLocalDateAsync(
            DateOnly localDate,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<DoseEvent>>(_items.Where(e => e.LocalDate == localDate).ToArray());

        public Task<IReadOnlyList<DoseEvent>> ListByScheduleAsync(
            Guid scheduleId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<DoseEvent>>(_items.Where(e => e.ScheduleId == scheduleId).ToArray());

        public Task UpsertManyAsync(
            IReadOnlyList<DoseEvent> events,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<DoseEvent?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(_items.FirstOrDefault(e => e.Id == id));
    }

    private sealed class FakeTransitions : IDoseTransitionService
    {
        public List<Guid> ConfirmCalls { get; } = [];

        public Task<DoseTransitionResult> ConfirmAsync(
            Guid doseEventId,
            DoseEventSource source,
            DateTimeOffset? takenAt = null,
            CancellationToken cancellationToken = default)
        {
            ConfirmCalls.Add(doseEventId);
            return Task.FromResult(new DoseTransitionResult(
                "Applied",
                doseEventId,
                DoseEventState.Taken,
                null,
                null,
                null));
        }

        public Task<DoseTransitionResult> SkipAsync(
            Guid doseEventId,
            DoseEventSource source,
            DateTimeOffset? skippedAt = null,
            CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<DoseTransitionResult> UndoConfirmAsync(
            Guid doseEventId,
            DateTimeOffset? undoneAt = null,
            CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
    }

    private sealed class FakeRealtime : IDoseEventRealtime
    {
        public event EventHandler<DoseEventChange>? Changed;

        public void Raise(DoseEventChange change) => Changed?.Invoke(this, change);

        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class RecordingUiDispatcher : IUiDispatcher
    {
        public int PostCount { get; private set; }

        public void Post(Action action)
        {
            PostCount++;
            action();
        }
    }
}
