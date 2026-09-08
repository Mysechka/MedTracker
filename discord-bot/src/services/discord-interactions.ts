import type { SupabaseClient } from "@supabase/supabase-js";
import { handleLinkCommand } from "../commands/link.js";
import { handleTodayCommand } from "../commands/today.js";
import { formatSkippedText, formatSnoozedText, formatTakenText } from "../utils/formatters.js";

export const DiscordInteractionType = {
  PING: 1,
  APPLICATION_COMMAND: 2,
  MESSAGE_COMPONENT: 3,
} as const;

export const DiscordCallbackType = {
  PONG: 1,
  CHANNEL_MESSAGE_WITH_SOURCE: 4,
  DEFERRED_UPDATE_MESSAGE: 6,
  UPDATE_MESSAGE: 7,
} as const;

export interface DiscordInteractionResponse {
  type: number;
  data?: {
    content?: string;
    flags?: number;
    components?: any[];
  };
}

export interface DiscordInteraction {
  id: string;
  application_id: string;
  type: number;
  data?: {
    id?: string;
    name?: string;
    type?: number;
    options?: Array<{ name: string; value: string; type: number }>;
    custom_id?: string;
    component_type?: number;
  };
  guild_id?: string;
  channel_id?: string;
  message?: {
    id: string;
    channel_id: string;
    content: string;
  };
  token?: string;
  version?: number;
}

export interface DiscordInteractionsServiceOptions {
  supabaseClient?: SupabaseClient | null;
  publicKeyHex?: string;
}

export class DiscordInteractionsService {
  private supabase: SupabaseClient | null;
  public publicKeyHex: string;

  constructor(options: DiscordInteractionsServiceOptions = {}) {
    this.supabase = options.supabaseClient || null;
    this.publicKeyHex = options.publicKeyHex || process.env.DISCORD_PUBLIC_KEY || "";
  }

  public async handleInteraction(interaction: DiscordInteraction): Promise<DiscordInteractionResponse> {
    // 1. PING (Type 1) -> PONG (Type 1)
    if (interaction.type === DiscordInteractionType.PING) {
      return { type: DiscordCallbackType.PONG };
    }

    // 2. APPLICATION_COMMAND (Type 2)
    if (interaction.type === DiscordInteractionType.APPLICATION_COMMAND) {
      return this.handleSlashCommand(interaction);
    }

    // 3. MESSAGE_COMPONENT (Type 3) -> Buttons
    if (interaction.type === DiscordInteractionType.MESSAGE_COMPONENT) {
      return this.handleComponentInteraction(interaction);
    }

    return {
      type: DiscordCallbackType.CHANNEL_MESSAGE_WITH_SOURCE,
      data: { content: "Неподдерживаемый тип взаимодействия.", flags: 64 },
    };
  }

  private async handleSlashCommand(interaction: DiscordInteraction): Promise<DiscordInteractionResponse> {
    const commandName = interaction.data?.name?.toLowerCase();
    const channelId = interaction.channel_id;

    if (commandName === "ping") {
      return {
        type: DiscordCallbackType.CHANNEL_MESSAGE_WITH_SOURCE,
        data: { content: "Pong! 🏓 MedTracker Discord Bot активен и готов к работе.", flags: 64 },
      };
    }

    if (commandName === "link") {
      const code = interaction.data?.options?.find((o) => o.name === "code")?.value?.trim().toUpperCase();
      const result = await handleLinkCommand(code, channelId, this.supabase);
      return {
        type: DiscordCallbackType.CHANNEL_MESSAGE_WITH_SOURCE,
        data: { content: result.message, flags: 64 },
      };
    }

    if (commandName === "today") {
      const result = await handleTodayCommand(channelId, "discord", this.supabase);
      return {
        type: DiscordCallbackType.CHANNEL_MESSAGE_WITH_SOURCE,
        data: { content: result.message, flags: 64 },
      };
    }

    return {
      type: DiscordCallbackType.CHANNEL_MESSAGE_WITH_SOURCE,
      data: { content: "Неизвестная команда.", flags: 64 },
    };
  }

  private async handleComponentInteraction(interaction: DiscordInteraction): Promise<DiscordInteractionResponse> {
    const customId = interaction.data?.custom_id || "";
    const parsed = this.parseCustomId(customId);

    if (!parsed) {
      return {
        type: DiscordCallbackType.CHANNEL_MESSAGE_WITH_SOURCE,
        data: { content: "Некорректные данные кнопки.", flags: 64 },
      };
    }

    const { action, doseEventId } = parsed;

    if (action === "snooze") {
      if (this.supabase) {
        await this.supabase.rpc("snooze_notification_delivery", {
          p_dose_event_id: doseEventId,
          p_channel_type: "discord_app",
          p_minutes: 15,
        });
      }

      const updatedText = formatSnoozedText(15);
      return {
        type: DiscordCallbackType.UPDATE_MESSAGE,
        data: {
          content: updatedText,
          components: [],
        },
      };
    }

    const isTaken = action === "take" || action === "taken";
    const rpcName = isTaken ? "confirm_dose" : "skip_dose";
    const nowUtc = new Date().toISOString();

    let outcome = "Applied";
    if (this.supabase) {
      const { data: rpcData } = await this.supabase.rpc(rpcName, {
        p_dose_event_id: doseEventId,
        p_source: "Discord",
        ...(isTaken ? { p_taken_at: nowUtc } : { p_skipped_at: nowUtc }),
      });
      outcome = rpcData?.outcome || "Applied";
    }

    const timeFormatted = nowUtc.slice(11, 16);
    const updatedText =
      outcome === "Rejected"
        ? "⚠️ Действие уже было обработано ранее."
        : isTaken
          ? formatTakenText(timeFormatted)
          : formatSkippedText();

    return {
      type: DiscordCallbackType.UPDATE_MESSAGE,
      data: {
        content: updatedText,
        components: [],
      },
    };
  }

  private parseCustomId(customId: string): { action: "take" | "taken" | "skip" | "snooze"; doseEventId: string } | null {
    // 1. Формат take:<id>, skip:<id>, snooze:<id>:15
    const shortMatch = /^(take|skip|snooze):([0-9a-f-]{36})(?::\d+)?$/i.exec(customId);
    if (shortMatch) {
      return {
        action: shortMatch[1].toLowerCase() as any,
        doseEventId: shortMatch[2],
      };
    }

    // 2. Формат dose:(taken|skip|snooze):<id>
    const longMatch = /^dose:(taken|skip|snooze):([0-9a-f-]{36})$/i.exec(customId);
    if (longMatch) {
      return {
        action: longMatch[1].toLowerCase() as any,
        doseEventId: longMatch[2],
      };
    }

    return null;
  }
}
