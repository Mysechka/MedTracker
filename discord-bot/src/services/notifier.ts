import type { Client, MessageCreateOptions } from "discord.js";

export interface SendMessageResult {
  success: boolean;
  messageId?: string;
  error?: string;
}

/**
 * Отправка тестового или целевого сообщения в канал Discord
 * @param client - Экземпляр Discord Client
 * @param channelId - ID целевого канала
 * @param messageOptions - Текст или опции сообщения
 */
export async function sendChannelMessage(
  client: Client,
  channelId: string,
  messageOptions: string | MessageCreateOptions,
): Promise<SendMessageResult> {
  if (!client || !channelId) {
    return { success: false, error: "Missing client or channelId" };
  }

  try {
    const channel = await client.channels.fetch(channelId);
    if (!channel || !channel.isTextBased()) {
      return { success: false, error: `Channel ${channelId} not found or not text-based` };
    }

    const payload = typeof messageOptions === "string" ? { content: messageOptions } : messageOptions;
    // @ts-expect-error text-based channels support send
    const sent = await channel.send(payload);
    return { success: true, messageId: sent.id };
  } catch (err) {
    const errorMsg = err instanceof Error ? err.message : String(err);
    console.error(`Failed to send message to channel ${channelId}:`, errorMsg);
    return { success: false, error: errorMsg };
  }
}
