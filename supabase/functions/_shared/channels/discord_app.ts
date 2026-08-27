import type {
  EditReminderInput,
  EditReminderResult,
  INotificationChannel,
  NotificationChannelType,
  SendReminderInput,
  SendReminderResult,
} from "../types.ts";
import { formatReminderText, optionalEnv, requireEnv } from "../types.ts";

function discordApi(path: string): string {
  return `https://discord.com/api/v10${path}`;
}

/** Discord Application (бот) с компонентами-кнопками. */
export class DiscordAppChannel implements INotificationChannel {
  readonly channelType: NotificationChannelType = "discord_app";

  async sendReminder(input: SendReminderInput): Promise<SendReminderResult> {
    const token = requireEnv("DISCORD_BOT_TOKEN");
    const text = formatReminderText(
      input.medicationName,
      input.dosageLabel,
      input.scheduledAtIso,
    );

    const body = {
      content: text,
      components: [{
        type: 1,
        components: [
          {
            type: 2,
            style: 3,
            label: "Принял",
            custom_id: `dose:taken:${input.doseEventId}`,
          },
          {
            type: 2,
            style: 2,
            label: "Пропустить",
            custom_id: `dose:skip:${input.doseEventId}`,
          },
          {
            type: 2,
            style: 1,
            label: "Отложить 15 мин",
            custom_id: `dose:snooze:${input.doseEventId}`,
          },
        ],
      }],
    };

    const response = await fetch(discordApi(`/channels/${input.targetId}/messages`), {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        Authorization: `Bot ${token}`,
      },
      body: JSON.stringify(body),
    });

    if (response.status === 429) {
      const payload = await response.json().catch(() => ({})) as { retry_after?: number };
      const retryAfter = Math.ceil(payload.retry_after ?? 60);
      return { ok: false, retryAfterSeconds: retryAfter, error: "429" };
    }

    if (!response.ok) {
      const errText = await response.text();
      return { ok: false, error: errText.slice(0, 300) };
    }

    const payload = await response.json() as { id?: string };
    if (!payload.id) {
      return { ok: false, error: "missing message id" };
    }
    return { ok: true, messageId: payload.id };
  }

  async editReminder(input: EditReminderInput): Promise<EditReminderResult> {
    const token = requireEnv("DISCORD_BOT_TOKEN");
    const response = await fetch(
      discordApi(`/channels/${input.targetId}/messages/${input.messageId}`),
      {
        method: "PATCH",
        headers: {
          "Content-Type": "application/json",
          Authorization: `Bot ${token}`,
        },
        body: JSON.stringify({ content: input.text, components: [] }),
      },
    );

    if (!response.ok) {
      return { ok: false, error: (await response.text()).slice(0, 300) };
    }
    return { ok: true };
  }
}

export function isDiscordAppConfigured(): boolean {
  return optionalEnv("DISCORD_BOT_TOKEN") != null &&
    optionalEnv("DISCORD_PUBLIC_KEY") != null &&
    optionalEnv("DISCORD_APPLICATION_ID") != null;
}
