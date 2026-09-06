import { DiscordAppChannel } from "../_shared/channels/discord_app.ts";
import { createServiceClient, rpcJson } from "../_shared/supabase.ts";
import {
  formatSkippedText,
  formatTakenText,
  optionalEnv,
} from "../_shared/types.ts";
import type { SupabaseClient } from "https://esm.sh/@supabase/supabase-js@2.49.1";

/**
 * Discord Interaction Types:
 * 1: PING
 * 2: APPLICATION_COMMAND
 * 3: MESSAGE_COMPONENT
 */
const InteractionType = {
  PING: 1,
  APPLICATION_COMMAND: 2,
  MESSAGE_COMPONENT: 3,
} as const;

/**
 * Discord Interaction Callback Types:
 * 1: PONG
 * 4: CHANNEL_MESSAGE_WITH_SOURCE
 * 6: DEFERRED_UPDATE_MESSAGE
 * 7: UPDATE_MESSAGE
 */
const CallbackType = {
  PONG: 1,
  CHANNEL_MESSAGE_WITH_SOURCE: 4,
  DEFERRED_UPDATE_MESSAGE: 6,
  UPDATE_MESSAGE: 7,
} as const;

interface DiscordUser {
  id: string;
  username: string;
  discriminator: string;
}

interface DiscordMember {
  user?: DiscordUser;
}

interface DiscordCommandOption {
  name: string;
  value: string;
  type: number;
}

interface DiscordInteractionData {
  id?: string;
  name?: string;
  type?: number;
  options?: DiscordCommandOption[];
  custom_id?: string;
  component_type?: number;
}

interface DiscordMessage {
  id: string;
  channel_id: string;
  content: string;
}

interface DiscordInteraction {
  id: string;
  application_id: string;
  type: number;
  data?: DiscordInteractionData;
  guild_id?: string;
  channel_id?: string;
  member?: DiscordMember;
  user?: DiscordUser;
  token: string;
  version: number;
  message?: DiscordMessage;
}

interface ConfirmLinkRpcResult {
  outcome: "Applied" | "Rejected";
  reason?: string;
  user_id?: string;
  channel_type?: string;
}

interface DoseRpcResult {
  outcome: "Applied" | "NoOp" | "Rejected";
  state?: string;
  reason?: string;
  dose_event_id?: string;
}

Deno.serve(async (req: Request): Promise<Response> => {
  if (req.method !== "POST") {
    return new Response("Method not allowed", { status: 405 });
  }

  // 1. Проверка публичного ключа Discord
  const publicKeyHex = optionalEnv("DISCORD_PUBLIC_KEY");
  if (!publicKeyHex) {
    return new Response("Discord public key not configured", { status: 503 });
  }

  // 2. Проверка сигнатур в заголовках
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

  // 3. Парсинг взаимодействия
  let interaction: DiscordInteraction;
  try {
    interaction = JSON.parse(bodyText) as DiscordInteraction;
  } catch {
    return new Response("Bad request", { status: 400 });
  }

  // 4. Обработка PING -> PONG (Type 1)
  if (interaction.type === InteractionType.PING) {
    return Response.json({ type: CallbackType.PONG });
  }

  try {
    const client = createServiceClient();

    // 5. Обработка Slash Command (/link <code>) (Type 2)
    if (interaction.type === InteractionType.APPLICATION_COMMAND) {
      return await handleSlashCommand(client, interaction);
    }

    // 6. Обработка Message Component (кнопки приёма) (Type 3) -> Ответ Type 7 (UPDATE_MESSAGE)
    if (interaction.type === InteractionType.MESSAGE_COMPONENT) {
      return await handleComponent(client, interaction);
    }

    return Response.json({
      type: CallbackType.CHANNEL_MESSAGE_WITH_SOURCE,
      data: { content: "Неподдерживаемый тип взаимодействия", flags: 64 },
    });
  } catch (error) {
    const message = error instanceof Error ? error.message : String(error);
    console.error("discord-interactions error:", message);
    return Response.json({
      type: CallbackType.CHANNEL_MESSAGE_WITH_SOURCE,
      data: { content: "Произошла временная ошибка. Попробуйте позже.", flags: 64 },
    });
  }
});

/**
 * Обработка слэш-команды /link <code>.
 */
async function handleSlashCommand(
  client: SupabaseClient,
  interaction: DiscordInteraction,
): Promise<Response> {
  const name = interaction.data?.name?.toLowerCase();
  if (name !== "link") {
    return Response.json({
      type: CallbackType.CHANNEL_MESSAGE_WITH_SOURCE,
      data: { content: "Неизвестная команда", flags: 64 },
    });
  }

  const code = interaction.data?.options?.find((o) => o.name === "code")?.value?.trim();
  const channelId = interaction.channel_id;

  if (!code || !channelId) {
    return Response.json({
      type: CallbackType.CHANNEL_MESSAGE_WITH_SOURCE,
      data: { content: "Необходимо указать код привязки: `/link <code>`", flags: 64 },
    });
  }

  const result = await rpcJson<ConfirmLinkRpcResult>(client, "confirm_messenger_link", {
    p_link_code: code,
    p_channel_type: "discord",
    p_chat_id: null,
    p_channel_id: channelId,
  });

  const content = result.outcome === "Applied"
    ? "✅ Discord успешно привязан! Уведомления о приёме лекарств будут приходить в этот канал."
    : "❌ Код не найден или уже использован. Сгенерируйте новый код в приложении MedTracker (Настройки → Мессенджеры).";

  return Response.json({
    type: CallbackType.CHANNEL_MESSAGE_WITH_SOURCE,
    data: { content, flags: 64 },
  });
}

/**
 * Обработка нажатий интерактивных кнопок (Type 3: MESSAGE_COMPONENT).
 * Возвращает Type 7 (UPDATE_MESSAGE), атомарно обновляя текст и удаляя кнопки components: [].
 */
async function handleComponent(
  client: SupabaseClient,
  interaction: DiscordInteraction,
): Promise<Response> {
  const customId = interaction.data?.custom_id ?? "";
  const match = /^dose:(taken|skip|snooze):([0-9a-f-]{36})$/i.exec(customId);

  if (!match) {
    return Response.json({
      type: CallbackType.CHANNEL_MESSAGE_WITH_SOURCE,
      data: { content: "Некорректные данные кнопки", flags: 64 },
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

    const updatedText = "⏳ Отложено на 15 минут";

    // Возвращаем Type 7 (UPDATE_MESSAGE) с components: [] для мгновенного обновления
    const response = Response.json({
      type: CallbackType.UPDATE_MESSAGE,
      data: {
        content: updatedText,
        components: [],
      },
    });

    // Фоном обновляем статус доставки в БД
    if (channelId && messageId) {
      await client
        .from("notification_deliveries")
        .update({ status: "Edited", message_id: messageId })
        .eq("dose_event_id", doseEventId)
        .eq("channel_type", "discord_app");
    }

    return response;
  }

  const rpcName = action === "taken" ? "confirm_dose" : "skip_dose";
  const nowUtc = new Date().toISOString();

  const result = await rpcJson<DoseRpcResult>(client, rpcName, {
    p_dose_event_id: doseEventId,
    p_source: "Discord",
    ...(action === "taken"
      ? { p_taken_at: nowUtc }
      : { p_skipped_at: nowUtc }),
  });

  const updatedText = result.outcome === "Rejected"
    ? "⚠️ Действие уже было обработано ранее."
    : (action === "taken"
      ? formatTakenText(nowUtc.slice(11, 16))
      : formatSkippedText());

  if (channelId && messageId) {
    await client
      .from("notification_deliveries")
      .update({ status: "Edited", message_id: messageId })
      .eq("dose_event_id", doseEventId)
      .eq("channel_type", "discord_app");
  }

  // Атомарный ответ Type 7 (UPDATE_MESSAGE): обновляет текст и удаляет кнопки
  return Response.json({
    type: CallbackType.UPDATE_MESSAGE,
    data: {
      content: updatedText,
      components: [],
    },
  });
}

/**
 * Криптографическая валидация Ed25519 подписи Discord через Web Crypto API.
 */
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
      { name: "Ed25519" },
      false,
      ["verify"],
    );

    const message = new TextEncoder().encode(timestamp + body);
    return await crypto.subtle.verify("Ed25519", key, hexToBytes(signatureHex), message);
  } catch (error) {
    console.error("discord signature verification failed:", error);
    return false;
  }
}

function hexToBytes(hex: string): Uint8Array {
  const clean = hex.trim().toLowerCase();
  if (clean.length % 2 !== 0) {
    throw new Error("Invalid hex string length");
  }
  const bytes = new Uint8Array(clean.length / 2);
  for (let i = 0; i < bytes.length; i++) {
    bytes[i] = Number.parseInt(clean.slice(i * 2, i * 2 + 2), 16);
  }
  return bytes;
}
