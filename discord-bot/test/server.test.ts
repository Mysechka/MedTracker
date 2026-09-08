import { test, describe, before, after } from "node:test";
import assert from "node:assert/strict";
import type { Server } from "node:http";
import type { AddressInfo } from "node:net";
import { createServerApp } from "../src/server.js";
import { createTestEd25519KeyPair } from "../src/utils/crypto.js";

describe("Express REST Server Endpoints", () => {
  let server: Server;
  let baseUrl: string;
  const keyPair = createTestEd25519KeyPair();
  const telegramSecret = "test_telegram_secret_999";

  before(async () => {
    const { app } = createServerApp({
      telegramWebhookSecret: telegramSecret,
      discordPublicKey: keyPair.publicKeyHex,
    });

    await new Promise<void>((resolve) => {
      server = app.listen(0, () => {
        const addr = server.address() as AddressInfo;
        baseUrl = `http://localhost:${addr.port}`;
        resolve();
      });
    });
  });

  after(async () => {
    await new Promise<void>((resolve, reject) => {
      server.close((err) => (err ? reject(err) : resolve()));
    });
  });

  test("GET /health возвращает 200 OK", async () => {
    const res = await fetch(`${baseUrl}/health`);
    assert.equal(res.status, 200);
    const body = (await res.json()) as any;
    assert.equal(body.status, "ok");
  });

  test("POST /api/telegram/webhook с неверным секретным заголовком возвращает 401", async () => {
    const res = await fetch(`${baseUrl}/api/telegram/webhook`, {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        "X-Telegram-Bot-Api-Secret-Token": "wrong_secret",
      },
      body: JSON.stringify({ update_id: 1 }),
    });

    assert.equal(res.status, 401);
  });

  test("POST /api/telegram/webhook с правильным секретным заголовком возвращает 200", async () => {
    const res = await fetch(`${baseUrl}/api/telegram/webhook`, {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        "X-Telegram-Bot-Api-Secret-Token": telegramSecret,
      },
      body: JSON.stringify({
        update_id: 1,
        message: {
          message_id: 1,
          chat: { id: 12345, type: "private" },
          text: "/start",
        },
      }),
    });

    assert.equal(res.status, 200);
    const json = (await res.json()) as any;
    assert.equal(json.ok, true);
  });

  test("POST /api/discord/interactions без заголовков подписи возвращает 401", async () => {
    const res = await fetch(`${baseUrl}/api/discord/interactions`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ type: 1 }),
    });

    assert.equal(res.status, 401);
  });

  test("POST /api/discord/interactions с валидной подписью Ed25519 возвращает 200 PONG", async () => {
    const timestamp = Math.floor(Date.now() / 1000).toString();
    const payload = JSON.stringify({ type: 1 });
    const signature = keyPair.sign(timestamp, payload);

    const res = await fetch(`${baseUrl}/api/discord/interactions`, {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        "X-Signature-Ed25519": signature,
        "X-Signature-Timestamp": timestamp,
      },
      body: payload,
    });

    assert.equal(res.status, 200);
    const json = (await res.json()) as any;
    assert.equal(json.type, 1); // PONG
  });
});
