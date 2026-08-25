using System.Diagnostics;
using FluentAssertions;
using Med.Domain.Entities;
using Med.Domain.Enums;
using Med.Domain.ValueObjects;
using Med.Infrastructure.Mapping;
using Med.Infrastructure.Models;
using Xunit;

namespace Med.Infrastructure.Tests.Mapping;

public sealed class EntityMapperRoundtripTests
{
    private static readonly Guid UserId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public void Profile_roundtrip()
    {
        Profile profile = Profile.Create(
            UserId,
            "brenda",
            "Europe/Berlin",
            new MealWindows(new TimeOnly(8, 0), new TimeOnly(13, 0), new TimeOnly(19, 30)),
            TimeSpan.FromHours(3));

        Profile again = EntityMappers.ToDomain(EntityMappers.ToRow(profile));

        again.Should().BeEquivalentTo(profile);
    }

    [Fact]
    public void Schedule_FixedTimes_и_WeekDays_All_roundtrip()
    {
        Schedule schedule = Schedule.CreateFixedTimes(
            Guid.Parse("44444444-4444-4444-4444-444444444444"),
            Guid.Parse("33333333-3333-3333-3333-333333333333"),
            WeekDays.All,
            1.5m,
            [new TimeOnly(8, 0), new TimeOnly(20, 0)]);

        Schedule again = EntityMappers.ToDomain(EntityMappers.ToRow(schedule, UserId));

        again.Should().BeEquivalentTo(schedule);
        EntityMappers.ToRow(schedule, UserId).Monday.Should().BeTrue();
        EntityMappers.ToRow(schedule, UserId).Sunday.Should().BeTrue();
    }

    [Fact]
    public void DoseEvent_roundtrip_сохраняет_dedupe_key_и_UTC()
    {
        DoseEvent dose = DoseEvent.CreateScheduled(
            Guid.NewGuid(),
            Guid.Parse("33333333-3333-3333-3333-333333333333"),
            Guid.Parse("44444444-4444-4444-4444-444444444444"),
            new DateTimeOffset(2026, 8, 25, 6, 0, 0, TimeSpan.Zero),
            new DateOnly(2026, 8, 25));

        DoseEvent again = EntityMappers.ToDomain(EntityMappers.ToRow(dose, UserId));

        again.Should().BeEquivalentTo(dose);
        again.ScheduledAt.Offset.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void Messenger_и_Notification_каналы_мапятся_в_snake_case_БД()
    {
        EntityMappers.ToMessengerChannelDb(MessengerChannelType.Telegram).Should().Be("telegram");
        EntityMappers.ToMessengerChannelDb(MessengerChannelType.Discord).Should().Be("discord");
        EntityMappers.ToNotificationChannelDb(NotificationChannelType.DiscordApp).Should().Be("discord_app");
        EntityMappers.ToNotificationChannelType("discord_webhook")
            .Should().Be(NotificationChannelType.DiscordWebhook);
    }

    [Fact]
    public void Document_path_формат()
    {
        Guid docId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        string path = Document.BuildStoragePath(UserId, docId, "scan.pdf");

        path.Should().Be($"{UserId}/{docId}/scan.pdf");
    }
}

public sealed class EntityMapperPerformanceTests
{
    [Fact]
    public void Roundtrip_10000_dose_events_укладывается_в_бюджет()
    {
        Guid userId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        List<DoseEvent> source = Enumerable.Range(0, 10_000)
            .Select(i => DoseEvent.CreateScheduled(
                Guid.NewGuid(),
                Guid.Parse("33333333-3333-3333-3333-333333333333"),
                Guid.Parse("44444444-4444-4444-4444-444444444444"),
                new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).AddMinutes(i),
                new DateOnly(2026, 1, 1).AddDays(i / (24 * 60))))
            .ToList();

        Stopwatch sw = Stopwatch.StartNew();
        List<DoseEventRow> rows = source.Select(e => EntityMappers.ToRow(e, userId)).ToList();
        List<DoseEvent> back = rows.Select(EntityMappers.ToDomain).ToList();
        sw.Stop();

        back.Should().HaveCount(10_000);
        sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(2));
    }
}
