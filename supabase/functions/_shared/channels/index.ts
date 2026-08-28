import { DiscordAppChannel, isDiscordAppConfigured } from "./discord_app.ts";
import { DiscordWebhookChannel, isDiscordWebhookConfigured } from "./discord_webhook.ts";
import { isTelegramConfigured, TelegramChannel } from "./telegram.ts";
import type { INotificationChannel, MessengerLinkRow, NotificationChannelType } from "../types.ts";

export interface ResolvedChannel {
  channel: INotificationChannel;
  targetId: string;
  channelType: NotificationChannelType;
}

/**
 * Выбор канала по подтверждённой привязке и наличию секретов.
 * Discord: App с кнопками предпочтительнее; иначе webhook без кнопок.
 */
export function resolveChannelsForLink(link: MessengerLinkRow): ResolvedChannel[] {
  if (!link.is_confirmed) {
    return [];
  }

  const result: ResolvedChannel[] = [];

  if (link.channel_type === "telegram" && isTelegramConfigured() && link.chat_id) {
    result.push({
      channel: new TelegramChannel(),
      targetId: link.chat_id,
      channelType: "telegram",
    });
  }

  if (link.channel_type === "discord") {
    if (isDiscordAppConfigured() && link.channel_id) {
      result.push({
        channel: new DiscordAppChannel(),
        targetId: link.channel_id,
        channelType: "discord_app",
      });
    } else if (isDiscordWebhookConfigured()) {
      // Webhook URL глобальный; targetId не используется для send, но нужен для интерфейса.
      result.push({
        channel: new DiscordWebhookChannel(),
        targetId: link.channel_id ?? "webhook",
        channelType: "discord_webhook",
      });
    }
  }

  return result;
}

export function channelByType(type: NotificationChannelType): INotificationChannel | null {
  switch (type) {
    case "telegram":
      return isTelegramConfigured() ? new TelegramChannel() : null;
    case "discord_app":
      return isDiscordAppConfigured() ? new DiscordAppChannel() : null;
    case "discord_webhook":
      return isDiscordWebhookConfigured() ? new DiscordWebhookChannel() : null;
    default:
      return null;
  }
}
