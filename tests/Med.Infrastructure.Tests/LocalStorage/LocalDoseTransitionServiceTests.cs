using FluentAssertions;
using Med.Application.Abstractions;
using Med.Domain.Entities;
using Med.Domain.Enums;
using Med.Infrastructure.LocalStorage;
using Xunit;

namespace Med.Infrastructure.Tests.LocalStorage;

public sealed class LocalDoseTransitionServiceTests : IDisposable
{
    private readonly LocalDatabase _db;
    private readonly LocalDoseEventRepository _repo;
    private readonly LocalDoseTransitionService _transitions;
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _medicationId = Guid.NewGuid();
    private readonly Guid _courseId = Guid.NewGuid();
    private readonly Guid _scheduleId = Guid.NewGuid();

    public LocalDoseTransitionServiceTests()
    {
        string connStr = $"Data Source=InMemoryTransitionTest_{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        _db = new LocalDatabase(connStr);
        _repo = new LocalDoseEventRepository(_db);
        _transitions = new LocalDoseTransitionService(_repo);

        SeedPrerequisites();
    }

    private void SeedPrerequisites()
    {
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO medications (id, user_id, name, form, dosage, unit)
            VALUES (@medId, @userId, 'Магний', 'таблетка', '500', 'мг');

            INSERT INTO courses (id, user_id, medication_id, starts_on, is_active)
            VALUES (@courseId, @userId, @medId, '2026-08-01', 1);

            INSERT INTO schedules (id, course_id, type, dose_amount)
            VALUES (@schedId, @courseId, 'FixedTimes', 1);
        ";
        cmd.Parameters.AddWithValue("@medId", _medicationId.ToString());
        cmd.Parameters.AddWithValue("@userId", _userId.ToString());
        cmd.Parameters.AddWithValue("@courseId", _courseId.ToString());
        cmd.Parameters.AddWithValue("@schedId", _scheduleId.ToString());
        cmd.ExecuteNonQuery();
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    [Fact]
    public async Task Confirm_на_Scheduled_переводит_в_Taken()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        Guid doseId = Guid.NewGuid();
        var dose = DoseEvent.CreateScheduled(
            doseId,
            _courseId,
            _scheduleId,
            DateTimeOffset.UtcNow,
            DateOnly.FromDateTime(DateTime.UtcNow));

        await _repo.UpsertManyAsync([dose], ct);

        DoseTransitionResult result = await _transitions.ConfirmAsync(doseId, DoseEventSource.App, cancellationToken: ct);

        result.IsApplied.Should().BeTrue();
        result.State.Should().Be(DoseEventState.Taken);

        DoseEvent? updated = await _repo.GetAsync(doseId, ct);
        updated.Should().NotBeNull();
        updated!.State.Should().Be(DoseEventState.Taken);
        updated.TakenAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Confirm_на_ранее_Skipped_из_App_сбрасывает_пропуск_и_переводит_в_Taken()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        Guid doseId = Guid.NewGuid();
        var dose = DoseEvent.CreateScheduled(
            doseId,
            _courseId,
            _scheduleId,
            DateTimeOffset.UtcNow,
            DateOnly.FromDateTime(DateTime.UtcNow)) with { State = DoseEventState.Skipped, TakenAt = DateTimeOffset.UtcNow };

        await _repo.UpsertManyAsync([dose], ct);

        DoseTransitionResult result = await _transitions.ConfirmAsync(doseId, DoseEventSource.App, cancellationToken: ct);

        result.IsApplied.Should().BeTrue();
        result.State.Should().Be(DoseEventState.Taken);

        DoseEvent? updated = await _repo.GetAsync(doseId, ct);
        updated.Should().NotBeNull();
        updated!.State.Should().Be(DoseEventState.Taken);
        updated.TakenAt.Should().NotBeNull();
    }

    [Fact]
    public async Task UndoConfirm_на_Skipped_возвращает_в_Scheduled()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        Guid doseId = Guid.NewGuid();
        var dose = DoseEvent.CreateScheduled(
            doseId,
            _courseId,
            _scheduleId,
            DateTimeOffset.UtcNow,
            DateOnly.FromDateTime(DateTime.UtcNow)) with { State = DoseEventState.Skipped, TakenAt = DateTimeOffset.UtcNow };

        await _repo.UpsertManyAsync([dose], ct);

        DoseTransitionResult result = await _transitions.UndoConfirmAsync(doseId, cancellationToken: ct);

        result.IsApplied.Should().BeTrue();
        result.State.Should().Be(DoseEventState.Scheduled);

        DoseEvent? updated = await _repo.GetAsync(doseId, ct);
        updated.Should().NotBeNull();
        updated!.State.Should().Be(DoseEventState.Scheduled);
        updated.TakenAt.Should().BeNull();
    }

    [Fact]
    public async Task UndoConfirm_на_Taken_возвращает_в_Scheduled()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        Guid doseId = Guid.NewGuid();
        var dose = DoseEvent.CreateScheduled(
            doseId,
            _courseId,
            _scheduleId,
            DateTimeOffset.UtcNow,
            DateOnly.FromDateTime(DateTime.UtcNow)) with { State = DoseEventState.Taken, TakenAt = DateTimeOffset.UtcNow };

        await _repo.UpsertManyAsync([dose], ct);

        DoseTransitionResult result = await _transitions.UndoConfirmAsync(doseId, cancellationToken: ct);

        result.IsApplied.Should().BeTrue();
        result.State.Should().Be(DoseEventState.Scheduled);

        DoseEvent? updated = await _repo.GetAsync(doseId, ct);
        updated.Should().NotBeNull();
        updated!.State.Should().Be(DoseEventState.Scheduled);
        updated.TakenAt.Should().BeNull();
    }
}
