using FluentAssertions;
using Med.Domain.Entities;
using Med.Domain.ValueObjects;
using Med.Infrastructure.LocalStorage;
using Xunit;

namespace Med.Infrastructure.Tests.LocalStorage;

public sealed class LocalProfileRepositoryTests : IDisposable
{
    private readonly LocalDatabase _db;
    private readonly LocalProfileRepository _repo;

    public LocalProfileRepositoryTests()
    {
        string connStr = $"Data Source=InMemoryProfileTest_{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        _db = new LocalDatabase(connStr);
        _repo = new LocalProfileRepository(_db);
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    [Fact]
    public async Task GetCurrent_WithMultipleProfiles_ReturnsActiveUserIfSet()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;

        var profile1 = Profile.Create(
            Guid.NewGuid(),
            "Мусечка",
            "Europe/Moscow",
            new MealWindows(new TimeOnly(8, 0), new TimeOnly(13, 0), new TimeOnly(19, 0)));

        var profile2 = Profile.Create(
            Guid.NewGuid(),
            "муся",
            "Europe/Moscow",
            new MealWindows(new TimeOnly(8, 0), new TimeOnly(13, 0), new TimeOnly(19, 0)));

        await _repo.UpdateAsync(profile1, ct);
        await Task.Delay(20, ct);
        await _repo.UpdateAsync(profile2, ct);

        // Explicitly set active user to profile2
        _db.SetSetting("active_local_user_id", profile2.UserId.ToString());

        Profile? current = _repo.GetCurrent();
        current.Should().NotBeNull();
        current!.UserId.Should().Be(profile2.UserId);
        current.Username.Should().Be("муся");

        Profile? currentAsync = await _repo.GetCurrentAsync(ct);
        currentAsync.Should().NotBeNull();
        currentAsync!.UserId.Should().Be(profile2.UserId);
        currentAsync.Username.Should().Be("муся");
    }

    [Fact]
    public async Task GetCurrent_WhenActiveUserNotSet_ReturnsMostRecentlyUpdatedProfile()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;

        var profile1 = Profile.Create(
            Guid.NewGuid(),
            "UserOld",
            "Europe/Moscow",
            new MealWindows(new TimeOnly(8, 0), new TimeOnly(13, 0), new TimeOnly(19, 0)));

        var profile2 = Profile.Create(
            Guid.NewGuid(),
            "UserNew",
            "Europe/Moscow",
            new MealWindows(new TimeOnly(8, 0), new TimeOnly(13, 0), new TimeOnly(19, 0)));

        await _repo.UpdateAsync(profile1, ct);
        await Task.Delay(50, ct);
        await _repo.UpdateAsync(profile2, ct);

        Profile? current = _repo.GetCurrent();
        current.Should().NotBeNull();
        current!.UserId.Should().Be(profile2.UserId);
        current.Username.Should().Be("UserNew");
    }
}
