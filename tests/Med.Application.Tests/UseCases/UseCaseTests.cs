using FluentAssertions;
using Med.Application.Abstractions;
using Med.Application.UseCases;
using Med.Domain.Abstractions;
using Med.Domain.Entities;
using Med.Domain.Enums;
using Med.Domain.Tests.Fakes;
using Med.Domain.ValueObjects;
using Xunit;

namespace Med.Application.Tests.UseCases;

public sealed class MaterializeUpcomingDosesUseCaseTests
{
    [Fact]
    public async Task Делегирует_в_IDoseEventMaterializer()
    {
        FakeMaterializer materializer = new(42);
        MaterializeUpcomingDosesUseCase useCase = new(materializer);

        int count = await useCase.ExecuteAsync(TestContext.Current.CancellationToken);

        count.Should().Be(42);
        materializer.Calls.Should().Be(1);
    }

    private sealed class FakeMaterializer(int result) : IDoseEventMaterializer
    {
        public int Calls { get; private set; }

        public Task<int> MaterializeAsync(int horizonDays = 14, CancellationToken cancellationToken = default)
        {
            Calls++;
            horizonDays.Should().Be(14);
            return Task.FromResult(result);
        }
    }
}

public sealed class UpdateProfileUseCaseTests
{
    [Fact]
    public async Task Меняет_username_через_WithUsername()
    {
        Profile profile = Profile.Create(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            "old",
            "UTC",
            new MealWindows(new TimeOnly(8, 0), new TimeOnly(13, 0), new TimeOnly(19, 0)));

        FakeProfiles profiles = new(profile);
        UpdateProfileUseCase useCase = new(profiles);

        Profile updated = await useCase.ExecuteAsync(
            username: "new_name",
            cancellationToken: TestContext.Current.CancellationToken);

        updated.Username.Should().Be("new_name");
        profiles.Current!.Username.Should().Be("new_name");
    }
}

public sealed class ConfirmDoseUseCaseTests
{
    [Fact]
    public async Task Делегирует_в_IDoseTransitionService()
    {
        FakeTransitions transitions = new();
        ConfirmDoseUseCase useCase = new(transitions);
        Guid id = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

        DoseTransitionResult result = await useCase.ExecuteAsync(
            id,
            DoseEventSource.App,
            cancellationToken: TestContext.Current.CancellationToken);

        result.Outcome.Should().Be("Applied");
        transitions.ConfirmCalls.Should().ContainSingle().Which.Should().Be(id);
    }
}

file sealed class FakeProfiles(Profile current) : IProfileRepository
{
    public Profile? Current { get; private set; } = current;

    public Task<Profile?> GetCurrentAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Current);

    public Task UpdateAsync(Profile profile, CancellationToken cancellationToken = default)
    {
        Current = profile;
        return Task.CompletedTask;
    }
}

file sealed class FakeCourses(IReadOnlyList<Course> items) : ICourseRepository
{
    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<Course?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(items.FirstOrDefault(c => c.Id == id));

    public Task<IReadOnlyList<Course>> ListAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(items);

    public Task UpsertAsync(Course course, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
}

file sealed class FakeSchedules(IReadOnlyList<Schedule> items) : IScheduleRepository
{
    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<Schedule?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(items.FirstOrDefault(s => s.Id == id));

    public Task<IReadOnlyList<Schedule>> ListByCourseAsync(
        Guid courseId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Schedule>>(items.Where(s => s.CourseId == courseId).ToArray());

    public Task UpsertAsync(Schedule schedule, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
}

file sealed class FakeDoseEvents : IDoseEventRepository
{
    public List<DoseEvent> Stored { get; } = [];

    public Task<DoseEvent?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(Stored.FirstOrDefault(e => e.Id == id));

    public Task<IReadOnlyList<DoseEvent>> ListByScheduleAsync(
        Guid scheduleId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<DoseEvent>>(Stored.Where(e => e.ScheduleId == scheduleId).ToArray());

    public Task<IReadOnlyList<DoseEvent>> ListForLocalDateAsync(
        DateOnly localDate,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<DoseEvent>>(Stored.Where(e => e.LocalDate == localDate).ToArray());

    public Task UpsertManyAsync(
        IReadOnlyList<DoseEvent> events,
        CancellationToken cancellationToken = default)
    {
        foreach (DoseEvent e in events)
        {
            int index = Stored.FindIndex(x => x.DedupeKey == e.DedupeKey);
            if (index >= 0)
            {
                Stored[index] = e;
            }
            else
            {
                Stored.Add(e);
            }
        }

        return Task.CompletedTask;
    }
}

file sealed class FakeTransitions : IDoseTransitionService
{
    public List<Guid> ConfirmCalls { get; } = [];

    public Task<DoseTransitionResult> ConfirmAsync(
        Guid doseEventId,
        DoseEventSource source,
        DateTimeOffset? takenAt = null,
        CancellationToken cancellationToken = default)
    {
        ConfirmCalls.Add(doseEventId);
        return Task.FromResult(new DoseTransitionResult("Applied", doseEventId, DoseEventState.Taken));
    }

    public Task<DoseTransitionResult> SkipAsync(
        Guid doseEventId,
        DoseEventSource source,
        DateTimeOffset? skippedAt = null,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<DoseTransitionResult> UndoConfirmAsync(
        Guid doseEventId,
        DateTimeOffset? undoneAt = null,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
}
