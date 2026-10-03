using Med.Application.Abstractions;
using Med.Domain.Entities;
using Med.Infrastructure.Repositories;

namespace Med.Infrastructure.LocalStorage;

public sealed class SwitchingMedicationRepository : IMedicationRepository
{
    private readonly RepositoryModeProvider _mode;
    private readonly LocalMedicationRepository _local;
    private readonly MedicationRepository _cloud;

    public SwitchingMedicationRepository(
        RepositoryModeProvider mode,
        LocalMedicationRepository local,
        MedicationRepository cloud)
    {
        _mode = mode;
        _local = local;
        _cloud = cloud;
    }

    private IMedicationRepository Target => _mode.Mode == RepositoryMode.Local ? _local : _cloud;

    public Task<IReadOnlyList<Medication>> ListAsync(CancellationToken cancellationToken = default) =>
        Target.ListAsync(cancellationToken);

    public Task<Medication?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        Target.GetAsync(id, cancellationToken);

    public Task UpsertAsync(Medication medication, CancellationToken cancellationToken = default) =>
        Target.UpsertAsync(medication, cancellationToken);

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) =>
        Target.DeleteAsync(id, cancellationToken);
}

public sealed class SwitchingCourseRepository : ICourseRepository
{
    private readonly RepositoryModeProvider _mode;
    private readonly LocalCourseRepository _local;
    private readonly CourseRepository _cloud;

    public SwitchingCourseRepository(
        RepositoryModeProvider mode,
        LocalCourseRepository local,
        CourseRepository cloud)
    {
        _mode = mode;
        _local = local;
        _cloud = cloud;
    }

    private ICourseRepository Target => _mode.Mode == RepositoryMode.Local ? _local : _cloud;

    public Task<IReadOnlyList<Course>> ListAsync(CancellationToken cancellationToken = default) =>
        Target.ListAsync(cancellationToken);

    public Task<Course?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        Target.GetAsync(id, cancellationToken);

    public Task UpsertAsync(Course course, CancellationToken cancellationToken = default) =>
        Target.UpsertAsync(course, cancellationToken);

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) =>
        Target.DeleteAsync(id, cancellationToken);
}

public sealed class SwitchingScheduleRepository : IScheduleRepository
{
    private readonly RepositoryModeProvider _mode;
    private readonly LocalScheduleRepository _local;
    private readonly ScheduleRepository _cloud;

    public SwitchingScheduleRepository(
        RepositoryModeProvider mode,
        LocalScheduleRepository local,
        ScheduleRepository cloud)
    {
        _mode = mode;
        _local = local;
        _cloud = cloud;
    }

    private IScheduleRepository Target => _mode.Mode == RepositoryMode.Local ? _local : _cloud;

    public Task<IReadOnlyList<Schedule>> ListByCourseAsync(Guid courseId, CancellationToken cancellationToken = default) =>
        Target.ListByCourseAsync(courseId, cancellationToken);

    public Task<Schedule?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        Target.GetAsync(id, cancellationToken);

    public Task UpsertAsync(Schedule schedule, CancellationToken cancellationToken = default) =>
        Target.UpsertAsync(schedule, cancellationToken);

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) =>
        Target.DeleteAsync(id, cancellationToken);
}

public sealed class SwitchingDoseEventRepository : IDoseEventRepository
{
    private readonly RepositoryModeProvider _mode;
    private readonly LocalDoseEventRepository _local;
    private readonly DoseEventRepository _cloud;

    public SwitchingDoseEventRepository(
        RepositoryModeProvider mode,
        LocalDoseEventRepository local,
        DoseEventRepository cloud)
    {
        _mode = mode;
        _local = local;
        _cloud = cloud;
    }

    private IDoseEventRepository Target => _mode.Mode == RepositoryMode.Local ? _local : _cloud;

    public Task<IReadOnlyList<DoseEvent>> ListForLocalDateAsync(DateOnly localDate, CancellationToken cancellationToken = default) =>
        Target.ListForLocalDateAsync(localDate, cancellationToken);

    public Task<IReadOnlyList<DoseEvent>> ListByScheduleAsync(Guid scheduleId, CancellationToken cancellationToken = default) =>
        Target.ListByScheduleAsync(scheduleId, cancellationToken);

    public Task UpsertManyAsync(IReadOnlyList<DoseEvent> events, CancellationToken cancellationToken = default) =>
        Target.UpsertManyAsync(events, cancellationToken);

    public Task<DoseEvent?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        Target.GetAsync(id, cancellationToken);
}

public sealed class SwitchingInventoryRepository : IInventoryRepository
{
    private readonly RepositoryModeProvider _mode;
    private readonly LocalInventoryRepository _local;
    private readonly InventoryRepository _cloud;

    public SwitchingInventoryRepository(
        RepositoryModeProvider mode,
        LocalInventoryRepository local,
        InventoryRepository cloud)
    {
        _mode = mode;
        _local = local;
        _cloud = cloud;
    }

    private IInventoryRepository Target => _mode.Mode == RepositoryMode.Local ? _local : _cloud;

    public Task<Inventory?> GetByMedicationAsync(Guid medicationId, CancellationToken cancellationToken = default) =>
        Target.GetByMedicationAsync(medicationId, cancellationToken);

    public Task UpsertAsync(Inventory inventory, CancellationToken cancellationToken = default) =>
        Target.UpsertAsync(inventory, cancellationToken);
}

public sealed class SwitchingProfileRepository : IProfileRepository
{
    private readonly RepositoryModeProvider _mode;
    private readonly LocalProfileRepository _local;
    private readonly ProfileRepository _cloud;

    public SwitchingProfileRepository(
        RepositoryModeProvider mode,
        LocalProfileRepository local,
        ProfileRepository cloud)
    {
        _mode = mode;
        _local = local;
        _cloud = cloud;
    }

    private IProfileRepository Target => _mode.Mode == RepositoryMode.Local ? _local : _cloud;

    public Task<Profile?> GetCurrentAsync(CancellationToken cancellationToken = default) =>
        Target.GetCurrentAsync(cancellationToken);

    public Task UpdateAsync(Profile profile, CancellationToken cancellationToken = default) =>
        Target.UpdateAsync(profile, cancellationToken);
}
