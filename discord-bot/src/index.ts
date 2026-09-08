import { createClient, type SupabaseClient } from "@supabase/supabase-js";
import { config, validateConfig } from "./config.js";
import { createDiscordBot } from "./bot.js";
import { createServerApp } from "./server.js";

async function main(): Promise<void> {
  console.log("==========================================");
  console.log("   MedTracker Bots REST API Server (TS)   ");
  console.log("==========================================");

  const validation = validateConfig();
  if (!validation.isValid) {
    console.warn(`⚠️ Внимание: отсутствуют переменные: ${validation.missing.join(", ")}`);
  }

  let supabaseClient: SupabaseClient | null = null;
  if (config.supabaseUrl && config.supabaseServiceRoleKey) {
    supabaseClient = createClient(config.supabaseUrl, config.supabaseServiceRoleKey);
    console.log("🔌 Подключен Supabase клиент к:", config.supabaseUrl);
  }

  // Запуск REST API сервера для вебхуков Telegram и Discord Interactions
  const { app } = createServerApp({
    supabaseClient,
    telegramWebhookSecret: config.telegramWebhookSecret,
    telegramBotToken: config.telegramBotToken,
    discordPublicKey: config.discordPublicKey,
  });

  const server = app.listen(config.port, () => {
    console.log(`🌐 REST API сервер запущен на http://localhost:${config.port}`);
    console.log(`   - Telegram Webhook: POST /api/telegram/webhook`);
    console.log(`   - Discord Interactions: POST /api/discord/interactions`);
  });

  let bot: ReturnType<typeof createDiscordBot> | null = null;
  if (config.discordToken) {
    bot = createDiscordBot({
      token: config.discordToken,
      applicationId: config.discordApplicationId,
      supabaseClient,
    });

    try {
      console.log("🚀 Запуск Discord Gateway бота...");
      await bot.start();
    } catch (error) {
      console.error("❌ Ошибка при запуске Discord-бота:", error);
    }
  }

  const shutdown = async () => {
    console.log("\n🛑 Остановка сервисов...");
    server.close();
    if (bot) {
      await bot.stop();
    }
    process.exit(0);
  };

  process.on("SIGINT", shutdown);
  process.on("SIGTERM", shutdown);
}

main().catch((err) => {
  console.error("Критическая ошибка:", err);
  process.exit(1);
});
