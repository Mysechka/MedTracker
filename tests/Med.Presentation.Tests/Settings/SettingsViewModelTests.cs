using FluentAssertions;
using Med.Application.Abstractions;
using Med.Domain.Entities;
using Med.Domain.Enums;
using Med.Presentation.Settings;
using Med.Application.UseCases;
using Med.Domain.ValueObjects;
using Xunit;

namespace Med.Presentation.Tests.Settings;

public sealed class SettingsViewModelTests
{
    [Fact]
    public async Task GenerateTelegramCode_пишет_link_code()
    {
        Guid userId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        FakeAuth auth = new(userId);
        FakeLinks links = new();
        FakeProfiles profiles = new(Profile.Create(
            userId,
            "brenda",
            "UTC",
            new MealWindows(new TimeOnly(8, 0), new TimeOnly(13, 0), new TimeOnly(19, 0))));

        SettingsViewModel vm = new(profiles, links, auth, new UpdateProfileUseCase(profiles));

        await vm.GenerateTelegramCodeCommand.ExecuteAsync(null);

        vm.TelegramLinkCode.Should().NotBeNullOrWhiteSpace();
        links.Stored.Should().ContainSingle(l =>
            l.ChannelType == MessengerChannelType.Telegram
            && l.LinkCode == vm.TelegramLinkCode
            && !l.IsConfirmed);
    }

    private sealed class FakeAuth(Guid userId) : IAuthService
    {
        public AuthSession? CurrentSession => new(userId, "a@b.c", "token", "refresh", DateTimeOffset.UtcNow.AddHours(1));

        public Guid? CurrentUserId => userId;

        public event EventHandler<AuthSession?>? AuthStateChanged
        {
            add { }
            remove { }
        }

        public Task<AuthSession> SignUpWithPasswordAsync(
            string email,
            string password,
            string? username = null,
            CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<AuthSession> SignInWithPasswordAsync(
            string email,
            string password,
            CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task SendMagicLinkAsync(string email, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task SignOutAsync(CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
    }

    private sealed class FakeProfiles(Profile profile) : IProfileRepository
    {
        private Profile _profile = profile;

        public Task<Profile?> GetCurrentAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<Profile?>(_profile);

        public Task UpdateAsync(Profile profile, CancellationToken cancellationToken = default)
        {
            _profile = profile;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeLinks : IMessengerLinkRepository
    {
        public List<MessengerLink> Stored { get; } = [];

        public Task<IReadOnlyList<MessengerLink>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<MessengerLink>>(Stored.ToArray());

        public Task<MessengerLink?> GetByChannelAsync(
            MessengerChannelType channelType,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Stored.FirstOrDefault(l => l.ChannelType == channelType));

        public Task UpsertAsync(MessengerLink link, CancellationToken cancellationToken = default)
        {
            Stored.RemoveAll(l => l.Id == link.Id || l.ChannelType == link.ChannelType);
            Stored.Add(link);
            return Task.CompletedTask;
        }

        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
