using Microsoft.Data.Sqlite;

namespace Med.Infrastructure.LocalStorage;

public sealed class LocalDatabase : IDisposable
{
    private readonly string _connectionString;
    private readonly SqliteConnection? _keepAliveConnection;
    private readonly object _lock = new();
    private bool _initialized;

    public LocalDatabase(string? connectionString = null)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string dbDir = Path.Combine(appData, "MedTracker");
            Directory.CreateDirectory(dbDir);
            string dbPath = Path.Combine(dbDir, "local.db");
            _connectionString = $"Data Source={dbPath}";
        }
        else
        {
            _connectionString = connectionString;
        }

        if (_connectionString.Contains(":memory:", StringComparison.OrdinalIgnoreCase) ||
            _connectionString.Contains("Mode=Memory", StringComparison.OrdinalIgnoreCase))
        {
            if (!_connectionString.Contains("Cache=Shared", StringComparison.OrdinalIgnoreCase))
            {
                _connectionString = _connectionString.TrimEnd(';') + ";Cache=Shared";
            }

            _keepAliveConnection = new SqliteConnection(_connectionString);
            _keepAliveConnection.Open();
        }

        Initialize();
    }

    public SqliteConnection CreateConnection()
    {
        var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA foreign_keys = ON; PRAGMA busy_timeout = 5000;";
        cmd.ExecuteNonQuery();
        return conn;
    }

    public void Initialize()
    {
        lock (_lock)
        {
            if (_initialized)
            {
                return;
            }

            using var conn = _keepAliveConnection is not null ? null : new SqliteConnection(_connectionString);
            var activeConn = _keepAliveConnection ?? conn!;
            if (_keepAliveConnection is null)
            {
                activeConn.Open();
            }

            using var cmd = activeConn.CreateCommand();
            cmd.CommandText = """
                PRAGMA foreign_keys = ON;
                PRAGMA busy_timeout = 5000;
                PRAGMA journal_mode = WAL;

                CREATE TABLE IF NOT EXISTS profiles (
                    id TEXT PRIMARY KEY,
                    username TEXT NOT NULL,
                    timezone_id TEXT NOT NULL DEFAULT 'Europe/Moscow',
                    breakfast_time TEXT NOT NULL DEFAULT '08:00:00',
                    lunch_time TEXT NOT NULL DEFAULT '13:00:00',
                    dinner_time TEXT NOT NULL DEFAULT '19:00:00',
                    confirmation_window_minutes INTEGER NOT NULL DEFAULT 30,
                    created_at TEXT NOT NULL DEFAULT (strftime('%Y-%m-%d %H:%M:%f', 'now')),
                    updated_at TEXT NOT NULL DEFAULT (strftime('%Y-%m-%d %H:%M:%f', 'now'))
                );

                CREATE TABLE IF NOT EXISTS medications (
                    id TEXT PRIMARY KEY,
                    user_id TEXT NOT NULL,
                    name TEXT NOT NULL,
                    form TEXT NOT NULL,
                    dosage TEXT NOT NULL,
                    unit TEXT NOT NULL,
                    barcode TEXT,
                    notes TEXT,
                    created_at TEXT NOT NULL DEFAULT (datetime('now')),
                    updated_at TEXT NOT NULL DEFAULT (datetime('now'))
                );

                CREATE TABLE IF NOT EXISTS courses (
                    id TEXT PRIMARY KEY,
                    user_id TEXT NOT NULL,
                    medication_id TEXT NOT NULL,
                    starts_on TEXT NOT NULL,
                    ends_on TEXT,
                    duration_days INTEGER,
                    is_active INTEGER NOT NULL DEFAULT 1,
                    diagnosis_id TEXT,
                    created_at TEXT NOT NULL DEFAULT (datetime('now')),
                    updated_at TEXT NOT NULL DEFAULT (datetime('now')),
                    FOREIGN KEY (medication_id) REFERENCES medications(id) ON DELETE CASCADE
                );

                CREATE TABLE IF NOT EXISTS schedules (
                    id TEXT PRIMARY KEY,
                    course_id TEXT NOT NULL,
                    type TEXT NOT NULL,
                    day_mon INTEGER NOT NULL DEFAULT 1,
                    day_tue INTEGER NOT NULL DEFAULT 1,
                    day_wed INTEGER NOT NULL DEFAULT 1,
                    day_thu INTEGER NOT NULL DEFAULT 1,
                    day_fri INTEGER NOT NULL DEFAULT 1,
                    day_sat INTEGER NOT NULL DEFAULT 1,
                    day_sun INTEGER NOT NULL DEFAULT 1,
                    dose_amount NUMERIC NOT NULL DEFAULT 1,
                    fixed_times TEXT,
                    interval_hours INTEGER,
                    interval_anchor_time TEXT,
                    every_n_days INTEGER,
                    meal_kind TEXT,
                    meal_relation TEXT,
                    offset_minutes INTEGER NOT NULL DEFAULT 0,
                    created_at TEXT NOT NULL DEFAULT (datetime('now')),
                    updated_at TEXT NOT NULL DEFAULT (datetime('now')),
                    FOREIGN KEY (course_id) REFERENCES courses(id) ON DELETE CASCADE
                );

                CREATE TABLE IF NOT EXISTS dose_events (
                    id TEXT PRIMARY KEY,
                    user_id TEXT NOT NULL,
                    course_id TEXT NOT NULL,
                    schedule_id TEXT NOT NULL,
                    scheduled_at TEXT NOT NULL,
                    local_date TEXT NOT NULL,
                    state TEXT NOT NULL DEFAULT 'Scheduled',
                    taken_at TEXT,
                    source TEXT,
                    dedupe_key TEXT NOT NULL UNIQUE,
                    created_at TEXT NOT NULL DEFAULT (datetime('now')),
                    updated_at TEXT NOT NULL DEFAULT (datetime('now')),
                    FOREIGN KEY (course_id) REFERENCES courses(id) ON DELETE CASCADE,
                    FOREIGN KEY (schedule_id) REFERENCES schedules(id) ON DELETE CASCADE
                );

                CREATE TABLE IF NOT EXISTS inventory (
                    id TEXT PRIMARY KEY,
                    user_id TEXT NOT NULL,
                    medication_id TEXT NOT NULL UNIQUE,
                    quantity_on_hand NUMERIC NOT NULL DEFAULT 0,
                    low_stock_threshold NUMERIC NOT NULL DEFAULT 0,
                    created_at TEXT NOT NULL DEFAULT (datetime('now')),
                    updated_at TEXT NOT NULL DEFAULT (datetime('now')),
                    FOREIGN KEY (medication_id) REFERENCES medications(id) ON DELETE CASCADE
                );

                CREATE TABLE IF NOT EXISTS app_settings (
                    key TEXT PRIMARY KEY,
                    value TEXT NOT NULL
                );
                """;
            cmd.ExecuteNonQuery();
            _initialized = true;
        }
    }

    public string? GetSetting(string key)
    {
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT value FROM app_settings WHERE key = @key LIMIT 1;";
        cmd.Parameters.AddWithValue("@key", key);
        var result = cmd.ExecuteScalar();
        return result is null || result is DBNull ? null : (string)result;
    }

    public void SetSetting(string key, string? value)
    {
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        if (value is null)
        {
            cmd.CommandText = "DELETE FROM app_settings WHERE key = @key;";
            cmd.Parameters.AddWithValue("@key", key);
        }
        else
        {
            cmd.CommandText = """
                INSERT INTO app_settings (key, value)
                VALUES (@key, @value)
                ON CONFLICT(key) DO UPDATE SET value = excluded.value;
                """;
            cmd.Parameters.AddWithValue("@key", key);
            cmd.Parameters.AddWithValue("@value", value);
        }
        cmd.ExecuteNonQuery();
    }

    public void Dispose()
    {
        _keepAliveConnection?.Dispose();
    }
}
