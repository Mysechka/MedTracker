import path from "node:path";
import { fileURLToPath } from "node:url";
import dotenv from "dotenv";

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

// Поиск .env в корне проекта или локально
const rootEnvPath = path.resolve(__dirname, "../../.env");
const localEnvPath = path.resolve(__dirname, "../.env");

dotenv.config({ path: rootEnvPath });
dotenv.config({ path: localEnvPath });

export interface BotConfig {
  discordToken: string;
  discordApplicationId: string;
  discordPublicKey: string;
  discordWebhookUrl: string;
  telegramBotToken: string;
  telegramWebhookSecret: string;
  supabaseUrl: string;
  supabaseServiceRoleKey: string;
  port: number;
}

export const config: BotConfig = {
  discordToken: process.env.DISCORD_BOT_TOKEN || "",
  discordApplicationId: process.env.DISCORD_APPLICATION_ID || "",
  discordPublicKey: process.env.DISCORD_PUBLIC_KEY || "",
  discordWebhookUrl: process.env.DISCORD_WEBHOOK_URL || "",
  telegramBotToken: process.env.TELEGRAM_BOT_TOKEN || "",
  telegramWebhookSecret: process.env.TELEGRAM_WEBHOOK_SECRET || "",
  supabaseUrl: process.env.SUPABASE_URL || "",
  supabaseServiceRoleKey: process.env.SUPABASE_SERVICE_ROLE_KEY || "",
  port: Number(process.env.PORT || 3000),
};

export interface ConfigValidationResult {
  isValid: boolean;
  missing: string[];
}

export function validateConfig(): ConfigValidationResult {
  const missing: string[] = [];
  if (!config.discordToken && !config.telegramBotToken) {
    missing.push("DISCORD_BOT_TOKEN or TELEGRAM_BOT_TOKEN");
  }
  return {
    isValid: missing.length === 0,
    missing,
  };
}
