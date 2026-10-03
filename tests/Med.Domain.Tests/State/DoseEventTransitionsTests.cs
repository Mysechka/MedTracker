using FluentAssertions;
using Med.Domain.Entities;
using Med.Domain.Enums;
using Med.Domain.State;
using Med.Domain.Tests.Fakes;
using Med.Domain.Tests.Support;
using Xunit;

namespace Med.Domain.Tests.State;

public sealed class DoseEventTransitionsTests
{
    private static DoseEvent Scheduled() =>
        DoseEvent.CreateScheduled(
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            DomainFixtures.CourseId,
            DomainFixtures.ScheduleId,
            new DateTimeOffset(2026, 8, 25, 6, 0, 0, TimeSpan.Zero),
            new DateOnly(2026, 8, 25));

    [Fact]
    public void Taken_из_Scheduled_применяется_один_раз()
    {
        DoseEvent dose = Scheduled();
        DateTimeOffset at = FakeClock.At("2026-08-25T06:10:00Z").UtcNow;

        TransitionResult first = DoseEventTransitions.MarkTaken(dose, at, DoseEventSource.App);
        TransitionResult second = DoseEventTransitions.MarkTaken(first.Event, at, DoseEventSource.Telegram);

        first.Outcome.Should().Be(TransitionOutcome.Applied);
        first.Event.State.Should().Be(DoseEventState.Taken);
        second.Outcome.Should().Be(TransitionOutcome.NoOp);
        second.Event.Source.Should().Be(DoseEventSource.App);
    }

    [Fact]
    public void Терминальный_Skipped_блокирует_Taken()
    {
        DoseEvent dose = Scheduled();
        DateTimeOffset at = FakeClock.At("2026-08-25T06:10:00Z").UtcNow;

        TransitionResult skipped = DoseEventTransitions.MarkSkipped(dose, at, DoseEventSource.App);
        TransitionResult taken = DoseEventTransitions.MarkTaken(skipped.Event, at, DoseEventSource.Telegram);

        skipped.Outcome.Should().Be(TransitionOutcome.Applied);
        taken.Outcome.Should().Be(TransitionOutcome.Rejected);
        taken.Event.State.Should().Be(DoseEventState.Skipped);
    }

    [Fact]
    public void Missed_после_окна_подтверждения()
    {
        DoseEvent dose = Scheduled();
        TransitionResult notified = DoseEventTransitions.MarkNotified(dose);
        DateTimeOffset before = FakeClock.At("2026-08-25T08:59:00Z").UtcNow;
        DateTimeOffset after = FakeClock.At("2026-08-25T09:00:00Z").UtcNow;

        DoseEventTransitions.MarkMissedIfExpired(notified.Event, before, TimeSpan.FromHours(3))
            .Outcome.Should().Be(TransitionOutcome.Rejected);

        TransitionResult missed = DoseEventTransitions.MarkMissedIfExpired(
            notified.Event, after, TimeSpan.FromHours(3));

        missed.Outcome.Should().Be(TransitionOutcome.Applied);
        missed.Event.State.Should().Be(DoseEventState.Missed);
    }

    [Fact]
    public void UndoTaken_возвращает_в_Scheduled()
    {
        DoseEvent dose = Scheduled();
        DateTimeOffset at = FakeClock.At("2026-08-25T06:10:00Z").UtcNow;
        TransitionResult taken = DoseEventTransitions.MarkTaken(dose, at, DoseEventSource.App);

        TransitionResult undo = DoseEventTransitions.UndoTaken(taken.Event);

        undo.Outcome.Should().Be(TransitionOutcome.Applied);
        undo.Event.State.Should().Be(DoseEventState.Scheduled);
        undo.Event.TakenAt.Should().BeNull();
    }

    [Fact]
    public void Undo_из_Skipped_возвращает_в_Scheduled()
    {
        DoseEvent dose = Scheduled();
        DateTimeOffset at = FakeClock.At("2026-08-25T06:10:00Z").UtcNow;
        TransitionResult skipped = DoseEventTransitions.MarkSkipped(dose, at, DoseEventSource.App);

        TransitionResult undo = DoseEventTransitions.Undo(skipped.Event);

        undo.Outcome.Should().Be(TransitionOutcome.Applied);
        undo.Event.State.Should().Be(DoseEventState.Scheduled);
        undo.Event.TakenAt.Should().BeNull();
    }
}
