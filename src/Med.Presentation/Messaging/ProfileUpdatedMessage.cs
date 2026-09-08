using CommunityToolkit.Mvvm.Messaging.Messages;
using Med.Domain.Entities;

namespace Med.Presentation.Messaging;

public sealed record ProfileUpdatedPayload(Profile Profile, string? AvatarPath);

public sealed class ProfileUpdatedMessage : ValueChangedMessage<ProfileUpdatedPayload>
{
    public ProfileUpdatedMessage(Profile profile, string? avatarPath = null)
        : base(new ProfileUpdatedPayload(profile, avatarPath))
    {
    }
}
