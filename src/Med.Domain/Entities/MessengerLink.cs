using Med.Domain.Enums;

namespace Med.Domain.Entities;

/// <summary>Привязка мессенджера к профилю.</summary>
public sealed record MessengerLink(
    Guid Id,
    Guid UserId,
    MessengerChannelType ChannelType,
    string? ChatId,
    string? ChannelId,
    bool IsConfirmed,
    string? LinkCode)
{
    public static MessengerLink Create(
        Guid id,
        Guid userId,
        MessengerChannelType channelType,
        string? chatId = null,
        string? channelId = null,
        bool isConfirmed = false,
        string? linkCode = null)
    {
        if (string.IsNullOrWhiteSpace(chatId)
            && string.IsNullOrWhiteSpace(channelId)
            && string.IsNullOrWhiteSpace(linkCode))
        {
            throw new ArgumentException("Нужен chat_id, channel_id или link_code.");
        }

        return new MessengerLink(
            id,
            userId,
            channelType,
            string.IsNullOrWhiteSpace(chatId) ? null : chatId.Trim(),
            string.IsNullOrWhiteSpace(channelId) ? null : channelId.Trim(),
            isConfirmed,
            string.IsNullOrWhiteSpace(linkCode) ? null : linkCode.Trim());
    }
}
