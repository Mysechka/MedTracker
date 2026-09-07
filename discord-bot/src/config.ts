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
  supabaseUrl: string;
  supabaseServiceRoleKey: string;
}

export const config: BotConfig = {
  discordToken: process.env.DISCORD_BOT_TOKEN || "",
  discordApplicationId: process.env.DISCORD_APPLICATION_ID || "",
  discordPublicKey: process.env.DISCORD_PUBLIC_KEY || "",
  discordWebhookUrl: process.env.DISCORD_WEBHOOK_URL || "",
  supabaseUrl: process.env.SUPABASE_URL || "",
  supabaseServiceRoleKey: process.env.SUPABASE_SERVICE_ROLE_KEY || "",
};

export interface ConfigValidationResult {
  isValid: boolean;
  missing: string[];
}

export function validateConfig(): ConfigValidationResult {
  const missing: string[] = [];
  if (!config.discordToken) missing.push("DISCORD_BOT_TOKEN");
  return {
    isValid: missing.length === 0,
    missing,
  };
}
