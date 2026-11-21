using FluentAssertions;
using Med.Infrastructure.LocalStorage;
using Xunit;

namespace Med.Infrastructure.Tests.LocalStorage;

public sealed class SqliteAppSettingsStoreTests : IDisposable
{
    private readonly LocalDatabase _db;
    private readonly SqliteAppSettingsStore _store;

    public SqliteAppSettingsStoreTests()
    {
        string connStr = $"Data Source=InMemorySettingsTest_{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        _db = new LocalDatabase(connStr);
        _store = new SqliteAppSettingsStore(_db);
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    [Fact]
    public void GetString_ВозвращаетNull_ЕслиКлючаНет()
    {
        _store.GetString("unknown_key").Should().BeNull();
    }

    [Fact]
    public void SetString_И_GetString_СохраняютИВозвращаютЗначение()
    {
        _store.SetString("test_key", "test_value");
        _store.GetString("test_key").Should().Be("test_value");

        _store.SetString("test_key", null);
        _store.GetString("test_key").Should().BeNull();
    }

    [Fact]
    public void GetDouble_ВозвращаетDefault_ЕслиКлючаНет()
    {
        _store.GetDouble("non_existent_double", 1.0).Should().Be(1.0);
    }

    [Fact]
    public void SetDouble_И_GetDouble_КорректноСохраняютИВозвращаютЧисло()
    {
        _store.SetDouble("ui_scale", 1.25);
        _store.GetDouble("ui_scale", 1.0).Should().Be(1.25);

        _store.SetDouble("ui_scale", 0.85);
        _store.GetDouble("ui_scale", 1.0).Should().Be(0.85);
    }
}
