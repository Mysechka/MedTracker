using System.Globalization;
using Med.Application.Abstractions;
using Med.Domain.Entities;
using Med.Domain.ValueObjects;
using Microsoft.Data.Sqlite;

namespace Med.Infrastructure.LocalStorage;

public sealed class LocalProfileRepository : IProfileRepository
{
    private readonly LocalDatabase _db;

    public LocalProfileRepository(LocalDatabase db)
    {
        _db = db;
    }

    public LocalDatabase Database => _db;

    public Profile? GetById(Guid id)
    {
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, username, timezone_id, breakfast_time, lunch_time, dinner_time, confirmation_window_minutes FROM profiles WHERE id = @id LIMIT 1;";
        cmd.Parameters.AddWithValue("@id", id.ToString());

        using var reader = cmd.ExecuteReader();
        return reader.Read() ? ReadProfile(reader) : null;
    }

    public async Task<Profile?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, username, timezone_id, breakfast_time, lunch_time, dinner_time, confirmation_window_minutes FROM profiles WHERE id = @id LIMIT 1;";
        cmd.Parameters.AddWithValue("@id", id.ToString());

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? ReadProfile(reader) : null;
    }

    public Profile? GetCurrent()
    {
        string? activeUserId = _db.GetSetting("active_local_user_id");
        if (!string.IsNullOrEmpty(activeUserId) && Guid.TryParse(activeUserId, out Guid guid))
        {
            Profile? profile = GetById(guid);
            if (profile is not null)
            {
                return profile;
            }
        }

        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, username, timezone_id, breakfast_time, lunch_time, dinner_time, confirmation_window_minutes FROM profiles ORDER BY updated_at DESC, created_at DESC, rowid DESC LIMIT 1;";

        using var reader = cmd.ExecuteReader();
        return reader.Read() ? ReadProfile(reader) : null;
    }

    public async Task<Profile?> GetCurrentAsync(CancellationToken cancellationToken = default)
    {
        string? activeUserId = _db.GetSetting("active_local_user_id");
        if (!string.IsNullOrEmpty(activeUserId) && Guid.TryParse(activeUserId, out Guid guid))
        {
            Profile? profile = await GetByIdAsync(guid, cancellationToken).ConfigureAwait(false);
            if (profile is not null)
            {
                return profile;
            }
        }

        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, username, timezone_id, breakfast_time, lunch_time, dinner_time, confirmation_window_minutes FROM profiles ORDER BY updated_at DESC, created_at DESC, rowid DESC LIMIT 1;";

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? ReadProfile(reader) : null;
    }

    private static Profile ReadProfile(SqliteDataReader reader)
    {
        Guid id = Guid.Parse(reader.GetString(0));
        string username = reader.GetString(1);
        string timeZoneId = reader.GetString(2);
        TimeOnly breakfast = TimeOnly.Parse(reader.GetString(3), CultureInfo.InvariantCulture);
        TimeOnly lunch = TimeOnly.Parse(reader.GetString(4), CultureInfo.InvariantCulture);
        TimeOnly dinner = TimeOnly.Parse(reader.GetString(5), CultureInfo.InvariantCulture);
        int windowMinutes = reader.GetInt32(6);

        return Profile.Create(
            id,
            username,
            timeZoneId,
            new MealWindows(breakfast, lunch, dinner),
            TimeSpan.FromMinutes(windowMinutes));
    }

    public async Task UpdateAsync(Profile profile, CancellationToken cancellationToken = default)
    {
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO profiles (id, username, timezone_id, breakfast_time, lunch_time, dinner_time, confirmation_window_minutes, created_at, updated_at)
            VALUES (@id, @username, @tz, @breakfast, @lunch, @dinner, @window, strftime('%Y-%m-%d %H:%M:%f', 'now'), strftime('%Y-%m-%d %H:%M:%f', 'now'))
            ON CONFLICT(id) DO UPDATE SET
                username = excluded.username,
                timezone_id = excluded.timezone_id,
                breakfast_time = excluded.breakfast_time,
                lunch_time = excluded.lunch_time,
                dinner_time = excluded.dinner_time,
                confirmation_window_minutes = excluded.confirmation_window_minutes,
                updated_at = strftime('%Y-%m-%d %H:%M:%f', 'now');
            """;

        cmd.Parameters.AddWithValue("@id", profile.UserId.ToString());
        cmd.Parameters.AddWithValue("@username", profile.Username);
        cmd.Parameters.AddWithValue("@tz", profile.TimeZoneId);
        cmd.Parameters.AddWithValue("@breakfast", profile.Meals.Breakfast.ToString("HH:mm:ss", CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("@lunch", profile.Meals.Lunch.ToString("HH:mm:ss", CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("@dinner", profile.Meals.Dinner.ToString("HH:mm:ss", CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("@window", (int)profile.ConfirmationWindow.TotalMinutes);

        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
