using System.Text.Json;
using FluentAssertions;
using Med.Application.Abstractions;
using Med.Domain.Entities;
using Med.Domain.ValueObjects;
using Med.Infrastructure.Configuration;
using Med.Infrastructure.LocalStorage;
using Microsoft.Extensions.Options;
using Xunit;

namespace Med.Application.Tests.Security;

public sealed class LocalSessionSecurityTests : IDisposable
{
    private readonly LocalDatabase _db;
    private readonly LocalProfileRepository _localProfiles;
    private readonly LocalMedicationRepository _localMedications;
    private readonly LocalCourseRepository _localCourses;
    private readonly LocalScheduleRepository _localSchedules;
    private readonly LocalDoseEventRepository _localDoseEvents;
    private readonly LocalInventoryRepository _localInventory;

    public LocalSessionSecurityTests()
    {
        string memConn = $"Data Source=InMemorySecurityTest_{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        _db = new LocalDatabase(memConn);

        _localProfiles = new LocalProfileRepository(_db);
        _localMedications = new LocalMedicationRepository(_db);
        _localCourses = new LocalCourseRepository(_db);
        _localSchedules = new LocalScheduleRepository(_db);
        _localDoseEvents = new LocalDoseEventRepository(_db);
        _localInventory = new LocalInventoryRepository(_db);
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    [Fact]
    public void LocalSession_HasDeterministicUserId()
    {
        Guid id1 = LocalAuthService.GenerateDeterministicUserId("HealthMaster");
        Guid id2 = LocalAuthService.GenerateDeterministicUserId("healthmaster");
        Guid id3 = LocalAuthService.GenerateDeterministicUserId("  HealthMaster  ");
        Guid idDifferent = LocalAuthService.GenerateDeterministicUserId("DifferentUser");

        id1.Should().NotBeEmpty();
        id2.Should().Be(id1, "регистр не должен влиять на детерминированный идентификатор");
        id3.Should().Be(id1, "пробелы по краям должны обрезаться");
        idDifferent.Should().NotBe(id1, "разные пользователи должны получать разные GUID");
    }

    [Fact]
    public async Task LocalData_NotAccessibleAfterMigration()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;

        Guid localUser = LocalAuthService.GenerateDeterministicUserId("UserBeforeMigration");
        Profile profile = Profile.Create(
            localUser,
            "UserBeforeMigration",
            "Europe/Moscow",
            new MealWindows(new TimeOnly(8, 0), new TimeOnly(13, 0), new TimeOnly(19, 0)));
        await _localProfiles.UpdateAsync(profile, ct);

        Medication med = Medication.Create(
            Guid.NewGuid(),
            localUser,
            "Аспирин",
            "Таблетка",
            "100",
            "мг");
        await _localMedications.UpsertAsync(med, ct);

        (await _localMedications.ListAsync(ct)).Should().HaveCount(1);
        (await _localProfiles.GetCurrentAsync(ct)).Should().NotBeNull();

        var migrationService = new DataMigrationService(
            new NoopAuthService(),
            _localMedications,
            _localCourses,
            _localSchedules,
            _localDoseEvents,
            _localInventory,
            _localProfiles,
            new NoopMedicationRepository(),
            new NoopCourseRepository(),
            new NoopScheduleRepository(),
            new NoopDoseEventRepository(),
            new NoopInventoryRepository(),
            new NoopProfileRepository(),
            new RepositoryModeProvider(),
            _db);

        await migrationService.ClearLocalDataAsync(ct);

        (await _localMedications.ListAsync(ct)).Should().BeEmpty();
        (await _localProfiles.GetCurrentAsync(ct)).Should().BeNull();
    }

    [Fact]
    public void SupabaseOptionsValidator_StillBlocksServiceRole()
    {
        var validator = new SupabaseOptionsValidator();

        string header = Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(new { alg = "HS256", typ = "JWT" }));
        string payload = Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(new { role = "service_role" }));
        string serviceRoleJwt = $"{header}.{payload}.fake-signature";

        var badOptions = new SupabaseOptions
        {
            Url = "https://test.supabase.co",
            AnonKey = serviceRoleJwt,
        };

        ValidateOptionsResult result = validator.Validate(null, badOptions);
        result.Failed.Should().BeTrue("service_role ключ не должен проходить валидацию");
        result.FailureMessage.Should().Contain("service_role");

        string anonPayload = Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(new { role = "anon" }));
        string anonJwt = $"{header}.{anonPayload}.fake-signature";

        var goodOptions = new SupabaseOptions
        {
            Url = "https://test.supabase.co",
            AnonKey = anonJwt,
        };

        ValidateOptionsResult goodResult = validator.Validate(null, goodOptions);
        goodResult.Succeeded.Should().BeTrue();
    }

    private sealed class NoopAuthService : IAuthService
    {
        public AuthSession? CurrentSession => null;
        public Guid? CurrentUserId => null;
        public bool IsLocalOnly => false;
        public event EventHandler<AuthSession?>? AuthStateChanged { add { } remove { } }
        public Task<AuthSession> SignUpWithPasswordAsync(string email, string password, string? username = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new AuthSession(Guid.NewGuid(), email, "token", "refresh", DateTimeOffset.MaxValue, username, false));
        public Task<AuthSession> SignInWithPasswordAsync(string email, string password, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public Task SendMagicLinkAsync(string email, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task UpdatePasswordAsync(string newPassword, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SignOutAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task MigrateToCloudAsync(string email, string password, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class NoopMedicationRepository : IMedicationRepository
    {
        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<Medication?> GetAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult<Medication?>(null);
        public Task<IReadOnlyList<Medication>> ListAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Medication>>([]);
        public Task UpsertAsync(Medication entity, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class NoopCourseRepository : ICourseRepository
    {
        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<Course?> GetAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult<Course?>(null);
        public Task<IReadOnlyList<Course>> ListAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Course>>([]);
        public Task<IReadOnlyList<Course>> ListByMedicationAsync(Guid medicationId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Course>>([]);
        public Task UpsertAsync(Course entity, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class NoopScheduleRepository : IScheduleRepository
    {
        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<Schedule?> GetAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult<Schedule?>(null);
        public Task<IReadOnlyList<Schedule>> ListAllAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Schedule>>([]);
        public Task<IReadOnlyList<Schedule>> ListByCourseAsync(Guid courseId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Schedule>>([]);
        public Task UpsertAsync(Schedule entity, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class NoopDoseEventRepository : IDoseEventRepository
    {
        public Task<DoseEvent?> GetAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult<DoseEvent?>(null);
        public Task<IReadOnlyList<DoseEvent>> ListForLocalDateAsync(DateOnly localDate, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<DoseEvent>>([]);
        public Task<IReadOnlyList<DoseEvent>> ListByScheduleAsync(Guid scheduleId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<DoseEvent>>([]);
        public Task UpsertManyAsync(IReadOnlyList<DoseEvent> events, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class NoopInventoryRepository : IInventoryRepository
    {
        public Task<Inventory?> GetByMedicationAsync(Guid medicationId, CancellationToken cancellationToken = default) => Task.FromResult<Inventory?>(null);
        public Task UpsertAsync(Inventory entity, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class NoopProfileRepository : IProfileRepository
    {
        public Task<Profile?> GetCurrentAsync(CancellationToken cancellationToken = default) => Task.FromResult<Profile?>(null);
        public Task UpdateAsync(Profile profile, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
