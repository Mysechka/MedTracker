import express, { type Request, type Response } from "express";
import type { SupabaseClient } from "@supabase/supabase-js";
import { TelegramService } from "./services/telegram.js";
import { DiscordInteractionsService } from "./services/discord-interactions.js";
import { verifyDiscordSignature } from "./utils/crypto.js";

export interface ServerOptions {
  supabaseClient?: SupabaseClient | null;
  telegramWebhookSecret?: string;
  telegramBotToken?: string;
  discordPublicKey?: string;
}

export interface AppInstance {
  app: express.Express;
  telegramService: TelegramService;
  discordService: DiscordInteractionsService;
}

/**
 * Создает сконфигурированное Express-приложение с REST-эндпоинтами для Telegram и Discord ботов.
 */
export function createServerApp(options: ServerOptions = {}): AppInstance {
  const app = express();

  const telegramService = new TelegramService({
    supabaseClient: options.supabaseClient,
    botToken: options.telegramBotToken,
    webhookSecret: options.telegramWebhookSecret,
  });

  const discordService = new DiscordInteractionsService({
    supabaseClient: options.supabaseClient,
    publicKeyHex: options.discordPublicKey,
  });

  // Сохраняем rawBody для проверки криптографической подписи Discord
  app.use(
    express.json({
      verify: (req: any, _res, buf) => {
        req.rawBody = buf;
      },
    }),
  );

  // 1. Health check
  app.get("/health", (_req: Request, res: Response) => {
    res.json({ status: "ok", timestamp: new Date().toISOString() });
  });

  app.get("/api/health", (_req: Request, res: Response) => {
    res.json({ status: "ok", timestamp: new Date().toISOString() });
  });

  // 2. Telegram Webhook Endpoint
  app.post("/api/telegram/webhook", async (req: Request, res: Response) => {
    const secretHeader = req.headers["x-telegram-bot-api-secret-token"] as string | undefined;

    if (!telegramService.verifyWebhookSecret(secretHeader)) {
      res.status(401).json({ error: "Unauthorized: Invalid secret token" });
      return;
    }

    try {
      const update = req.body;
      const result = await telegramService.processUpdate(update);
      res.json({ ok: true, result });
    } catch (error) {
      const message = error instanceof Error ? error.message : String(error);
      console.error("Telegram webhook error:", message);
      res.status(500).json({ ok: false, error: message });
    }
  });

  // 3. Discord Interactions REST Endpoint
  app.post("/api/discord/interactions", async (req: Request, res: Response) => {
    const signature = req.headers["x-signature-ed25519"] as string | undefined;
    const timestamp = req.headers["x-signature-timestamp"] as string | undefined;

    const rawBody = (req as any).rawBody || JSON.stringify(req.body || {});
    const publicKey = discordService.publicKeyHex;

    // Если публичный ключ задан — строго проверяем подпись
    if (publicKey) {
      if (!signature || !timestamp || !verifyDiscordSignature(publicKey, signature, timestamp, rawBody)) {
        res.status(401).send("Invalid request signature");
        return;
      }
    } else if (!signature || !timestamp) {
      // Если ключа в окружении нет, но заголовков подписи нет в запросе — 401
      res.status(401).send("Missing request signature");
      return;
    }

    try {
      const interaction = req.body;
      const response = await discordService.handleInteraction(interaction);
      res.json(response);
    } catch (error) {
      const message = error instanceof Error ? error.message : String(error);
      console.error("Discord interactions error:", message);
      res.status(500).json({ error: message });
    }
  });

  return { app, telegramService, discordService };
}
