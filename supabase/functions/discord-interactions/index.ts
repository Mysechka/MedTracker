import { DiscordAppChannel } from "../_shared/channels/discord_app.ts";
import { createServiceClient, rpcJson } from "../_shared/supabase.ts";
import {
  formatSkippedText,
  formatTakenText,
  optionalEnv,
} from "../_shared/types.ts";

/**
 * Discord Interactions Endpoint.
 * Проверка Ed25519 заголовков X-Signature-Ed25519 / X-Signature-Timestamp обязательна.
 */

Deno.serve(async (req) => {
  if (req.method !== "POST") {
    return new Response("Method not allowed", { status: 405 });
  }

  const publicKeyHex = optionalEnv("DISCORD_PUBLIC_KEY");
  if (!publicKeyHex) {
    return new Response("Discord public key not configured", { status: 503 });
  }

  const signature = req.headers.get("X-Signature-Ed25519");
  const timestamp = req.headers.get("X-Signature-Timestamp");
  if (!signature || !timestamp) {
    return new Response("Unauthorized", { status: 401 });
  }

  const bodyText = await req.text();
  const valid = await verifyDiscordSignature(publicKeyHex, signature, timestamp, bodyText);
  if (!valid) {
    return new Response("Unauthorized", { status: 401 });
  }

  let interaction: DiscordInteraction;
  try {
    interaction = JSON.parse(bodyText) as DiscordInteraction;
  } catch {
    return new Response("Bad request", { status: 400 });
  }

  // PING
  if (interaction.type === 1) {
    return Response.json({ type: 1 });
  }

  try {
    const client = createServiceClient();

    // APPLICATION_COMMAND — /link CODE
    if (interaction.type === 2) {
      return await handleSlashCommand(client, interaction);
    }

    // MESSAGE_COMPONENT — кнопки
    if (interaction.type === 3) {
      return await handleComponent(client, interaction);
    }

    return Response.json({
      type: 4,
      data: { content: "Неподдерживаемый тип взаимодействия", flags: 64 },
    });
  } catch (error) {
    const message = error instanceof Error ? error.message : String(error);
    console.error("discord-interactions failed", message);
    return Response.json({
      type: 4,
      data: { content: "Временная ошибка. Попробуйте позже.", flags: 64 },
    });
  }
});

interface DiscordInteraction {
  type: number;
  id?: string;
  token?: string;
  channel_id?: string;
  guild_id?: string;
  data?: {
    name?: string;
    options?: Array<{ name: string; value: string }>;
    custom_id?: string;
  };
  member?: { user?: { id: string } };
  user?: { id: string };
  message?: { id: string; channel_id?: string };
}

async function handleSlashCommand(
  // deno-lint-ignore no-explicit-any
  client: any,
  interaction: DiscordInteraction,
): Promise<Response> {
  const name = interaction.data?.name?.toLowerCase();
  if (name !== "link") {
    return Response.json({
      type: 4,
      data: { content: "Неизвестная команда", flags: 64 },
    });
  }

  const code = interaction.data?.options?.find((o) => o.name === "code")?.value?.trim();
  const channelId = interaction.channel_id;
  if (!code || !channelId) {
    return Response.json({
      type: 4,
      data: { content: "Нужен код привязки", flags: 64 },
    });
  }

  const result = await rpcJson<{ outcome: string }>(client, "confirm_messenger_link", {
    p_link_code: code,
    p_channel_type: "discord",
    p_chat_id: null,
    p_channel_id: channelId,
  });

  const content = result.outcome === "Applied"
    ? "Discord привязан. Уведомления о приёмах будут приходить в этот канал."
    : "Код не найден или уже использован.";

  return Response.json({
    type: 4,
    data: { content, flags: 64 },
  });
}

async function handleComponent(
  // deno-lint-ignore no-explicit-any
  client: any,
  interaction: DiscordInteraction,
): Promise<Response> {
  const customId = interaction.data?.custom_id ?? "";
  const match = /^dose:(taken|skip|snooze):([0-9a-f-]{36})$/i.exec(customId);
  if (!match) {
    return Response.json({
      type: 4,
      data: { content: "Некорректные данные", flags: 64 },
    });
  }

  const action = match[1].toLowerCase();
  const doseEventId = match[2];
  const channelId = interaction.channel_id ?? interaction.message?.channel_id;
  const messageId = interaction.message?.id;

  if (action === "snooze") {
    await rpcJson(client, "snooze_notification_delivery", {
      p_dose_event_id: doseEventId,
      p_channel_type: "discord_app",
      p_minutes: 15,
    });

    if (channelId && messageId && optionalEnv("DISCORD_BOT_TOKEN")) {
      const channel = new DiscordAppChannel();
      await channel.editReminder({
        messageId,
        targetId: channelId,
        text: "Отложено на 15 минут",
      });
    }

    return Response.json({
      type: 4,
      data: { content: "Напомню через 15 минут", flags: 64 },
    });
  }

  const rpcName = action === "taken" ? "confirm_dose" : "skip_dose";
  const result = await rpcJson<{ outcome: string }>(client, rpcName, {
    p_dose_event_id: doseEventId,
    p_source: "Discord",
    ...(action === "taken"
      ? { p_taken_at: new Date().toISOString() }
      : { p_skipped_at: new Date().toISOString() }),
  });

  const text = action === "taken"
    ? formatTakenText(new Date().toISOString().slice(11, 16))
    : formatSkippedText();

  if (channelId && messageId && optionalEnv("DISCORD_BOT_TOKEN")) {
    const channel = new DiscordAppChannel();
    await channel.editReminder({ messageId, targetId: channelId, text });
    await client
      .from("notification_deliveries")
      .update({ status: "Edited", message_id: messageId })
      .eq("dose_event_id", doseEventId)
      .eq("channel_type", "discord_app");
  }

  const ack = result.outcome === "Rejected"
    ? "Уже обработано"
    : (action === "taken" ? "Принято" : "Пропущено");

  return Response.json({
    type: 4,
    data: { content: ack, flags: 64 },
  });
}

async function verifyDiscordSignature(
  publicKeyHex: string,
  signatureHex: string,
  timestamp: string,
  body: string,
): Promise<boolean> {
  try {
    const key = await crypto.subtle.importKey(
      "raw",
      hexToBytes(publicKeyHex),
      { name: "Ed25519", namedCurve: "Ed25519" },
      false,
      ["verify"],
    );

    const message = new TextEncoder().encode(timestamp + body);
    return await crypto.subtle.verify("Ed25519", key, hexToBytes(signatureHex), message);
  } catch (error) {
    console.error("signature verify failed", error);
    return false;
  }
}

function hexToBytes(hex: string): Uint8Array {
  const clean = hex.trim().toLowerCase();
  if (clean.length % 2 !== 0) {
    throw new Error("invalid hex");
  }
  const bytes = new Uint8Array(clean.length / 2);
  for (let i = 0; i < bytes.length; i++) {
    bytes[i] = Number.parseInt(clean.slice(i * 2, i * 2 + 2), 16);
  }
  return bytes;
}
