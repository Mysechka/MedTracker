using Med.Application.Abstractions;
using Med.Domain.Entities;

namespace Med.Infrastructure.LocalStorage;

public sealed class DataMigrationService
{
    private readonly IAuthService _cloudAuth;
    private readonly LocalMedicationRepository _localMedications;
    private readonly LocalCourseRepository _localCourses;
    private readonly LocalScheduleRepository _localSchedules;
    private readonly LocalDoseEventRepository _localDoseEvents;
    private readonly LocalInventoryRepository _localInventory;
    private readonly LocalProfileRepository _localProfiles;
    private readonly IMedicationRepository _cloudMedications;
    private readonly ICourseRepository _cloudCourses;
    private readonly IScheduleRepository _cloudSchedules;
    private readonly IDoseEventRepository _cloudDoseEvents;
    private readonly IInventoryRepository _cloudInventory;
    private readonly IProfileRepository _cloudProfiles;
    private readonly RepositoryModeProvider _modeProvider;
    private readonly LocalDatabase _db;

    public DataMigrationService(
        IAuthService cloudAuth,
        LocalMedicationRepository localMedications,
        LocalCourseRepository localCourses,
        LocalScheduleRepository localSchedules,
        LocalDoseEventRepository localDoseEvents,
        LocalInventoryRepository localInventory,
        LocalProfileRepository localProfiles,
        IMedicationRepository cloudMedications,
        ICourseRepository cloudCourses,
        IScheduleRepository cloudSchedules,
        IDoseEventRepository cloudDoseEvents,
        IInventoryRepository cloudInventory,
        IProfileRepository cloudProfiles,
        RepositoryModeProvider modeProvider,
        LocalDatabase db)
    {
        _cloudAuth = cloudAuth;
        _localMedications = localMedications;
        _localCourses = localCourses;
        _localSchedules = localSchedules;
        _localDoseEvents = localDoseEvents;
        _localInventory = localInventory;
        _localProfiles = localProfiles;
        _cloudMedications = cloudMedications;
        _cloudCourses = cloudCourses;
        _cloudSchedules = cloudSchedules;
        _cloudDoseEvents = cloudDoseEvents;
        _cloudInventory = cloudInventory;
        _cloudProfiles = cloudProfiles;
        _modeProvider = modeProvider;
        _db = db;
    }

    public Task<AuthSession> MigrateAsync(
        string email,
        string password,
        CancellationToken cancellationToken = default) =>
        MigrateAsync(email, password, null, cancellationToken);

    public async Task<AuthSession> MigrateAsync(
        string email,
        string password,
        Action<AuthSession>? onSessionObtained,
        CancellationToken cancellationToken = default)
    {
        Profile? localProfile = await _localProfiles.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
        string? username = localProfile?.Username;

        AuthSession cloudSession;
        try
        {
            cloudSession = await _cloudAuth.SignUpWithPasswordAsync(
                email,
                password,
                username,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex.Message.Contains("already", StringComparison.OrdinalIgnoreCase) || ex.Message.Contains("422") || ex.ToString().Contains("already", StringComparison.OrdinalIgnoreCase))
        {
            cloudSession = await _cloudAuth.SignInWithPasswordAsync(
                email,
                password,
                cancellationToken).ConfigureAwait(false);
        }

        Guid cloudUserId = cloudSession.UserId;
        onSessionObtained?.Invoke(cloudSession);

        if (localProfile is not null)
        {
            Profile cloudProfile = Profile.Create(
                cloudUserId,
                localProfile.Username,
                localProfile.TimeZoneId,
                localProfile.Meals,
                localProfile.ConfirmationWindow);
            await _cloudProfiles.UpdateAsync(cloudProfile, cancellationToken).ConfigureAwait(false);

            try
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string avatarDir = Path.Combine(appData, "MedTracker", "avatars");
                if (Directory.Exists(avatarDir))
                {
                    string[] possible = [".png", ".jpg", ".jpeg", ".webp"];
                    foreach (string ext in possible)
                    {
                        string localPath = Path.Combine(avatarDir, $"{localProfile.UserId}{ext}");
                        if (File.Exists(localPath))
                        {
                            string cloudPath = Path.Combine(avatarDir, $"{cloudUserId}{ext}");
                            File.Copy(localPath, cloudPath, overwrite: true);
                            break;
                        }
                    }
                }
            }
            catch { }
        }

        var medications = await _localMedications.ListAsync(cancellationToken).ConfigureAwait(false);
        foreach (Medication med in medications)
        {
            Medication cloudMed = Medication.Create(
                med.Id,
                cloudUserId,
                med.Name,
                med.Form,
                med.Dosage,
                med.Unit,
                med.Barcode,
                med.Notes);
            await _cloudMedications.UpsertAsync(cloudMed, cancellationToken).ConfigureAwait(false);
        }

        var courses = await _localCourses.ListAsync(cancellationToken).ConfigureAwait(false);
        foreach (Course course in courses)
        {
            Course cloudCourse = Course.Create(
                course.Id,
                cloudUserId,
                course.MedicationId,
                course.StartsOn,
                course.EndsOn,
                course.DurationDays,
                course.IsActive,
                course.DiagnosisId);
            await _cloudCourses.UpsertAsync(cloudCourse, cancellationToken).ConfigureAwait(false);

            var schedules = await _localSchedules.ListByCourseAsync(course.Id, cancellationToken).ConfigureAwait(false);
            foreach (Schedule sched in schedules)
            {
                await _cloudSchedules.UpsertAsync(sched, cancellationToken).ConfigureAwait(false);
            }
        }

        var inventory = await _localInventory.ListAllAsync(cancellationToken).ConfigureAwait(false);
        foreach (Inventory inv in inventory)
        {
            Inventory cloudInv = Inventory.Create(
                inv.Id,
                cloudUserId,
                inv.MedicationId,
                inv.QuantityOnHand,
                inv.LowStockThreshold);
            await _cloudInventory.UpsertAsync(cloudInv, cancellationToken).ConfigureAwait(false);
        }

        var doseEvents = await _localDoseEvents.ListAllAsync(cancellationToken).ConfigureAwait(false);
        if (doseEvents.Count > 0)
        {
            await _cloudDoseEvents.UpsertManyAsync(doseEvents, cancellationToken).ConfigureAwait(false);
        }

        using (var conn = _db.CreateConnection())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "INSERT INTO app_settings (key, value) VALUES ('migration_completed', 'true') ON CONFLICT(key) DO UPDATE SET value = 'true';";
            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        _modeProvider.Mode = RepositoryMode.Cloud;
        return cloudSession;
    }

    public async Task ClearLocalDataAsync(CancellationToken cancellationToken = default)
    {
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            DELETE FROM dose_events;
            DELETE FROM inventory;
            DELETE FROM schedules;
            DELETE FROM courses;
            DELETE FROM medications;
            DELETE FROM profiles;
            """;
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
