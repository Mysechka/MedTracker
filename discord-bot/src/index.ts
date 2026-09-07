import { createClient, type SupabaseClient } from "@supabase/supabase-js";
import { config, validateConfig } from "./config.js";
import { createDiscordBot } from "./bot.js";

async function main(): Promise<void> {
  console.log("==========================================");
  console.log("   MedTracker Discord Bot Runner (TS)");
  console.log("==========================================");

  const validation = validateConfig();
  if (!validation.isValid) {
    console.warn(`⚠️ Внимание: отсутствуют переменные окружения: ${validation.missing.join(", ")}`);
    console.warn("Укажите DISCORD_BOT_TOKEN в файле .env в корне проекта для подключения бота.");
  }

  let supabaseClient: SupabaseClient | null = null;
  if (config.supabaseUrl && config.supabaseServiceRoleKey) {
    supabaseClient = createClient(config.supabaseUrl, config.supabaseServiceRoleKey);
    console.log("🔌 Подключен Supabase клиент к:", config.supabaseUrl);
  }

  if (!config.discordToken) {
    console.log("ℹ️ Бот готов к запуску после указания токена DISCORD_BOT_TOKEN в .env.");
    return;
  }

  const bot = createDiscordBot({
    token: config.discordToken,
    applicationId: config.discordApplicationId,
    supabaseClient,
  });

  process.on("SIGINT", async () => {
    console.log("\nПолучен сигнал SIGINT. Завершение работы...");
    await bot.stop();
    process.exit(0);
  });

  process.on("SIGTERM", async () => {
    console.log("\nПолучен сигнал SIGTERM. Завершение работы...");
    await bot.stop();
    process.exit(0);
  });

  try {
    console.log("🚀 Запуск Discord-бота...");
    await bot.start();
  } catch (error) {
    console.error("❌ Ошибка при запуске Discord-бота:", error);
    process.exit(1);
  }
}

main().catch((err) => {
  console.error("Критическая ошибка:", err);
  process.exit(1);
});
