using FluentAssertions;
using Med.Domain.Entities;
using Med.Domain.ValueObjects;
using Xunit;

namespace Med.Domain.Tests.Entities;

public sealed class ProfileTests
{
    [Fact]
    public void ResolveTimeZone_WithValidTimeZone_ReturnsCorrectTimeZoneInfo()
    {
        Profile profile = Profile.Create(
            Guid.NewGuid(),
            "User",
            "UTC",
            new MealWindows(new TimeOnly(8, 0), new TimeOnly(13, 0), new TimeOnly(19, 0)),
            TimeSpan.FromMinutes(30));

        TimeZoneInfo tz = profile.ResolveTimeZone();
        tz.Should().NotBeNull();
        tz.Id.Should().Be("UTC");
    }

    [Fact]
    public void ResolveTimeZone_WithInvalidTimeZone_FallsBackSafelyWithoutThrowing()
    {
        Profile profile = Profile.Create(
            Guid.NewGuid(),
            "User",
            "NonExistent/Invalid_TimeZone_Id_12345",
            new MealWindows(new TimeOnly(8, 0), new TimeOnly(13, 0), new TimeOnly(19, 0)),
            TimeSpan.FromMinutes(30));

        TimeZoneInfo tz = profile.ResolveTimeZone();
        tz.Should().NotBeNull();
        tz.Should().BeOneOf(TimeZoneInfo.Local, TimeZoneInfo.Utc);
    }
}
