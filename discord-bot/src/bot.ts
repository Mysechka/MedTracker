import {
  Client,
  GatewayIntentBits,
  REST,
  Routes,
  SlashCommandBuilder,
  Events,
  type Message,
  type Interaction,
} from "discord.js";
import type { SupabaseClient } from "@supabase/supabase-js";
import { handleLinkCommand } from "./commands/link.js";
import { handlePingCommand } from "./commands/ping.js";
import { sendChannelMessage, type SendMessageResult } from "./services/notifier.js";

/**
 * Определение слэш-команд для регистрации в Discord API
 */
export const slashCommands = [
  new SlashCommandBuilder()
    .setName("ping")
    .setDescription("Проверить статус и пинг бота MedTracker"),
  new SlashCommandBuilder()
    .setName("link")
    .setDescription("Привязать данный канал к аккаунту MedTracker")
    .addStringOption((option) =>
      option
        .setName("code")
        .setDescription("6-значный код привязки из приложения MedTracker (например, A8F3K2)")
        .setRequired(true)
        .setMinLength(6)
        .setMaxLength(6),
    ),
];

/**
 * Регистрация слэш-команд в Discord REST API
 */
export async function registerSlashCommands(token: string, applicationId: string): Promise<void> {
  if (!token || !applicationId) {
    console.warn("⚠️ Пропуск регистрации слэш-команд: отсутствует DISCORD_BOT_TOKEN или DISCORD_APPLICATION_ID");
    return;
  }

  const rest = new REST({ version: "10" }).setToken(token);

  try {
    console.log("⏳ Регистрация слэш-команд в Discord API...");
    await rest.put(Routes.applicationCommands(applicationId), {
      body: slashCommands.map((c) => c.toJSON()),
    });
    console.log("✅ Слэш-команды (/ping, /link) успешно зарегистрированы в Discord.");
  } catch (error) {
    console.error("❌ Ошибка при регистрации слэш-команд:", error);
  }
}

export interface DiscordBotOptions {
  token: string;
  applicationId?: string;
  supabaseClient?: SupabaseClient | null;
}

export interface DiscordBotInstance {
  client: Client;
  readonly isReady: boolean;
  start(): Promise<Client>;
  stop(): Promise<void>;
  sendTestMessage(channelId: string, text?: string): Promise<SendMessageResult>;
}

/**
 * Создание и настройка экземпляра Discord-бота
 */
export function createDiscordBot(options: DiscordBotOptions): DiscordBotInstance {
  const { token, applicationId, supabaseClient } = options;

  const client = new Client({
    intents: [
      GatewayIntentBits.Guilds,
      GatewayIntentBits.GuildMessages,
      GatewayIntentBits.MessageContent,
      GatewayIntentBits.DirectMessages,
    ],
  });

  let isReady = false;

  client.once(Events.ClientReady, (readyClient) => {
    isReady = true;
    console.log(`🤖 MedTracker Discord Bot готов! Авторизован как: ${readyClient.user.tag}`);
  });

  // Обработка текстовых команд (!ping, !link <код>)
  client.on(Events.MessageCreate, async (message: Message) => {
    if (message.author.bot) return;

    const content = message.content.trim();

    if (content === "!ping" || content === "/ping") {
      const response = handlePingCommand(client.ws.ping);
      await message.reply(response.content);
      return;
    }

    if (content.startsWith("!link") || content.startsWith("/link")) {
      const parts = content.split(/\s+/);
      const code = parts[1];
      const result = await handleLinkCommand(code, message.channelId, supabaseClient);
      await message.reply(result.message);
    }
  });

  // Обработка слэш-команд Discord Interaction
  client.on(Events.InteractionCreate, async (interaction: Interaction) => {
    if (!interaction.isChatInputCommand()) return;

    const { commandName } = interaction;

    if (commandName === "ping") {
      const response = handlePingCommand(client.ws.ping);
      await interaction.reply({ content: response.content, ephemeral: true });
      return;
    }

    if (commandName === "link") {
      const code = interaction.options.getString("code", true);
      const result = await handleLinkCommand(code, interaction.channelId, supabaseClient);
      await interaction.reply({ content: result.message, ephemeral: true });
    }
  });

  return {
    client,
    get isReady() {
      return isReady;
    },
    async start() {
      if (!token) {
        throw new Error("DISCORD_BOT_TOKEN не задан в .env файле.");
      }
      if (applicationId) {
        await registerSlashCommands(token, applicationId);
      }
      await client.login(token);
      return client;
    },
    async stop() {
      isReady = false;
      await client.destroy();
      console.log("🛑 MedTracker Discord Bot остановлен.");
    },
    async sendTestMessage(channelId: string, text = "💊 Тестовое напоминание от MedTracker Discord Bot") {
      return sendChannelMessage(client, channelId, text);
    },
  };
}
