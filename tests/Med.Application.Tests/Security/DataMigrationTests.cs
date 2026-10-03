using FluentAssertions;
using Med.Application.Abstractions;
using Med.Domain.Entities;
using Med.Domain.Enums;
using Med.Domain.ValueObjects;
using Med.Infrastructure.LocalStorage;
using Xunit;

namespace Med.Application.Tests.Security;

public sealed class DataMigrationTests : IDisposable
{
    private readonly LocalDatabase _db;
    private readonly LocalProfileRepository _localProfiles;
    private readonly LocalMedicationRepository _localMedications;
    private readonly LocalCourseRepository _localCourses;
    private readonly LocalScheduleRepository _localSchedules;
    private readonly LocalDoseEventRepository _localDoseEvents;
    private readonly LocalInventoryRepository _localInventory;

    private readonly InMemoryCloudMedicationRepository _cloudMedications = new();
    private readonly InMemoryCloudCourseRepository _cloudCourses = new();
    private readonly InMemoryCloudScheduleRepository _cloudSchedules = new();
    private readonly InMemoryCloudDoseEventRepository _cloudDoseEvents = new();
    private readonly InMemoryCloudInventoryRepository _cloudInventory = new();
    private readonly InMemoryCloudProfileRepository _cloudProfiles = new();
    private readonly InMemoryCloudAuthService _cloudAuth = new();
    private readonly RepositoryModeProvider _modeProvider = new();

    private readonly DataMigrationService _migrationService;
    private readonly Guid _localUserId = Guid.NewGuid();
    private readonly Guid _cloudUserId = Guid.NewGuid();

    public DataMigrationTests()
    {
        string memConn = $"Data Source=InMemoryMigrationTest_{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        _db = new LocalDatabase(memConn);

        _localProfiles = new LocalProfileRepository(_db);
        _localMedications = new LocalMedicationRepository(_db);
        _localCourses = new LocalCourseRepository(_db);
        _localSchedules = new LocalScheduleRepository(_db);
        _localDoseEvents = new LocalDoseEventRepository(_db);
        _localInventory = new LocalInventoryRepository(_db);

        _cloudAuth.ConfigureSignUpResult(_cloudUserId);

        _migrationService = new DataMigrationService(
            _cloudAuth,
            _localMedications,
            _localCourses,
            _localSchedules,
            _localDoseEvents,
            _localInventory,
            _localProfiles,
            _cloudMedications,
            _cloudCourses,
            _cloudSchedules,
            _cloudDoseEvents,
            _cloudInventory,
            _cloudProfiles,
            _modeProvider,
            _db);
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    private async Task SeedLocalDataAsync(CancellationToken ct)
    {
        Profile profile = Profile.Create(
            _localUserId,
            "LocalUser",
            "Europe/Moscow",
            new MealWindows(new TimeOnly(8, 0), new TimeOnly(13, 0), new TimeOnly(19, 0)));
        await _localProfiles.UpdateAsync(profile, ct);

        Medication med1 = Medication.Create(
            Guid.NewGuid(),
            _localUserId,
            "Парацетамол",
            "Таблетка",
            "500",
            "мг",
            notes: "При температуре");

        Medication med2 = Medication.Create(
            Guid.NewGuid(),
            _localUserId,
            "Ибупрофен",
            "Капсула",
            "200",
            "мг");

        await _localMedications.UpsertAsync(med1, ct);
        await _localMedications.UpsertAsync(med2, ct);

        Course course1 = Course.Create(
            Guid.NewGuid(),
            _localUserId,
            med1.Id,
            new DateOnly(2026, 10, 1),
            endsOn: null,
            durationDays: null);

        await _localCourses.UpsertAsync(course1, ct);

        Schedule schedule1 = Schedule.CreateFixedTimes(
            Guid.NewGuid(),
            course1.Id,
            WeekDays.All,
            1,
            [new TimeOnly(9, 0), new TimeOnly(21, 0)]);

        await _localSchedules.UpsertAsync(schedule1, ct);

        Inventory inv1 = Inventory.Create(
            Guid.NewGuid(),
            _localUserId,
            med1.Id,
            20,
            5);

        await _localInventory.UpsertAsync(inv1, ct);
    }

    [Fact]
    public async Task MigrateToCloud_TransfersAllMedications()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await SeedLocalDataAsync(ct);

        AuthSession session = await _migrationService.MigrateAsync("cloud@test.com", "pass123", ct);

        session.UserId.Should().Be(_cloudUserId);
        _cloudMedications.Items.Should().HaveCount(2);
        _cloudMedications.Items.Should().Contain(m => m.Name == "Парацетамол" && m.UserId == _cloudUserId);
        _cloudMedications.Items.Should().Contain(m => m.Name == "Ибупрофен" && m.UserId == _cloudUserId);
    }

    [Fact]
    public async Task MigrateToCloud_TransfersAllCourses()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await SeedLocalDataAsync(ct);

        await _migrationService.MigrateAsync("cloud@test.com", "pass123", ct);

        _cloudCourses.Items.Should().HaveCount(1);
        Course migratedCourse = _cloudCourses.Items[0];
        migratedCourse.UserId.Should().Be(_cloudUserId);
        migratedCourse.StartsOn.Should().Be(new DateOnly(2026, 10, 1));
        migratedCourse.EndsOn.Should().BeNull();
    }

    [Fact]
    public async Task MigrateToCloud_SwitchesToCloudMode()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await SeedLocalDataAsync(ct);

        _modeProvider.Mode.Should().Be(RepositoryMode.Local);

        AuthSession session = await _migrationService.MigrateAsync("cloud@test.com", "pass123", ct);

        _modeProvider.Mode.Should().Be(RepositoryMode.Cloud);
        session.IsLocalOnly.Should().BeFalse();
    }

    [Fact]
    public async Task MigrateToCloud_PreservesRelationships()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await SeedLocalDataAsync(ct);

        await _migrationService.MigrateAsync("cloud@test.com", "pass123", ct);

        Medication cloudMed = _cloudMedications.Items.Single(m => m.Name == "Парацетамол");
        Course cloudCourse = _cloudCourses.Items.Single(c => c.MedicationId == cloudMed.Id);
        cloudCourse.Should().NotBeNull();

        Schedule cloudSchedule = _cloudSchedules.Items.Single(s => s.CourseId == cloudCourse.Id);
        cloudSchedule.Should().NotBeNull();
        cloudSchedule.FixedTimes.Should().Contain(new TimeOnly(9, 0));
        cloudSchedule.FixedTimes.Should().Contain(new TimeOnly(21, 0));
    }

    private sealed class InMemoryCloudMedicationRepository : IMedicationRepository
    {
        public List<Medication> Items { get; } = [];

        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
        {
            Items.RemoveAll(x => x.Id == id);
            return Task.CompletedTask;
        }

        public Task<Medication?> GetAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult(Items.FirstOrDefault(x => x.Id == id));

        public Task<IReadOnlyList<Medication>> ListAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<Medication>>(Items);

        public Task UpsertAsync(Medication entity, CancellationToken cancellationToken = default)
        {
            Items.RemoveAll(x => x.Id == entity.Id);
            Items.Add(entity);
            return Task.CompletedTask;
        }
    }

    private sealed class InMemoryCloudCourseRepository : ICourseRepository
    {
        public List<Course> Items { get; } = [];

        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
        {
            Items.RemoveAll(x => x.Id == id);
            return Task.CompletedTask;
        }

        public Task<Course?> GetAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult(Items.FirstOrDefault(x => x.Id == id));

        public Task<IReadOnlyList<Course>> ListAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<Course>>(Items);

        public Task<IReadOnlyList<Course>> ListByMedicationAsync(Guid medicationId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<Course>>(Items.Where(x => x.MedicationId == medicationId).ToList());

        public Task UpsertAsync(Course entity, CancellationToken cancellationToken = default)
        {
            Items.RemoveAll(x => x.Id == entity.Id);
            Items.Add(entity);
            return Task.CompletedTask;
        }
    }

    private sealed class InMemoryCloudScheduleRepository : IScheduleRepository
    {
        public List<Schedule> Items { get; } = [];

        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
        {
            Items.RemoveAll(x => x.Id == id);
            return Task.CompletedTask;
        }

        public Task<Schedule?> GetAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult(Items.FirstOrDefault(x => x.Id == id));

        public Task<IReadOnlyList<Schedule>> ListAllAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<Schedule>>(Items);

        public Task<IReadOnlyList<Schedule>> ListByCourseAsync(Guid courseId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<Schedule>>(Items.Where(x => x.CourseId == courseId).ToList());

        public Task UpsertAsync(Schedule entity, CancellationToken cancellationToken = default)
        {
            Items.RemoveAll(x => x.Id == entity.Id);
            Items.Add(entity);
            return Task.CompletedTask;
        }
    }

    private sealed class InMemoryCloudDoseEventRepository : IDoseEventRepository
    {
        public List<DoseEvent> Items { get; } = [];

        public Task<IReadOnlyList<DoseEvent>> ListForLocalDateAsync(DateOnly localDate, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<DoseEvent>>(Items.Where(x => x.LocalDate == localDate).ToList());

        public Task<IReadOnlyList<DoseEvent>> ListByScheduleAsync(Guid scheduleId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<DoseEvent>>(Items.Where(x => x.ScheduleId == scheduleId).ToList());

        public Task<DoseEvent?> GetAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult(Items.FirstOrDefault(x => x.Id == id));

        public Task UpsertManyAsync(IReadOnlyList<DoseEvent> events, CancellationToken cancellationToken = default)
        {
            foreach (DoseEvent e in events)
            {
                Items.RemoveAll(x => x.Id == e.Id);
                Items.Add(e);
            }
            return Task.CompletedTask;
        }
    }

    private sealed class InMemoryCloudInventoryRepository : IInventoryRepository
    {
        public List<Inventory> Items { get; } = [];

        public Task<Inventory?> GetByMedicationAsync(Guid medicationId, CancellationToken cancellationToken = default)
            => Task.FromResult(Items.FirstOrDefault(x => x.MedicationId == medicationId));

        public Task UpsertAsync(Inventory entity, CancellationToken cancellationToken = default)
        {
            Items.RemoveAll(x => x.Id == entity.Id);
            Items.Add(entity);
            return Task.CompletedTask;
        }
    }

    private sealed class InMemoryCloudProfileRepository : IProfileRepository
    {
        public Profile? Current { get; private set; }

        public Task<Profile?> GetCurrentAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(Current);

        public Task UpdateAsync(Profile profile, CancellationToken cancellationToken = default)
        {
            Current = profile;
            return Task.CompletedTask;
        }
    }

    private sealed class InMemoryCloudAuthService : IAuthService
    {
        private Guid _configuredUserId = Guid.NewGuid();

        public void ConfigureSignUpResult(Guid userId)
        {
            _configuredUserId = userId;
        }

        public AuthSession? CurrentSession { get; private set; }

        public Guid? CurrentUserId => CurrentSession?.UserId;

        public bool IsLocalOnly => false;

        public event EventHandler<AuthSession?>? AuthStateChanged;

        public Task<AuthSession> SignUpWithPasswordAsync(
            string email,
            string password,
            string? username = null,
            CancellationToken cancellationToken = default)
        {
            CurrentSession = new AuthSession(
                _configuredUserId,
                Email: email,
                AccessToken: "cloud-access-token",
                RefreshToken: "cloud-refresh-token",
                ExpiresAt: DateTimeOffset.UtcNow.AddDays(7),
                Username: username,
                IsLocalOnly: false);

            AuthStateChanged?.Invoke(this, CurrentSession);
            return Task.FromResult(CurrentSession);
        }

        public Task<AuthSession> SignInWithPasswordAsync(string email, string password, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task SendMagicLinkAsync(string email, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task UpdatePasswordAsync(string newPassword, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task SignOutAsync(CancellationToken cancellationToken = default)
        {
            CurrentSession = null;
            AuthStateChanged?.Invoke(this, null);
            return Task.CompletedTask;
        }

        public Task MigrateToCloudAsync(string email, string password, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
