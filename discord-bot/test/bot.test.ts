import { test, describe } from "node:test";
import assert from "node:assert/strict";
import type { SupabaseClient } from "@supabase/supabase-js";
import { isValidLinkCode, handleLinkCommand } from "../src/commands/link.js";
import { handlePingCommand } from "../src/commands/ping.js";
import { createDiscordBot, slashCommands } from "../src/bot.js";
import { validateConfig, config } from "../src/config.js";

describe("Discord Bot Unit Tests (TypeScript)", () => {
  describe("6-Character Link Code Validation", () => {
    test("валидные 6-значные буквенно-цифровые коды проходят проверку", () => {
      assert.equal(isValidLinkCode("A8F3K2"), true);
      assert.equal(isValidLinkCode("123456"), true);
      assert.equal(isValidLinkCode("ABCDEF"), true);
      assert.equal(isValidLinkCode("9Z8Y7X"), true);
      assert.equal(isValidLinkCode("a8f3k2"), true);
      assert.equal(isValidLinkCode("  A8F3K2  "), true);
    });

    test("невалидные коды отклоняются", () => {
      assert.equal(isValidLinkCode(""), false);
      assert.equal(isValidLinkCode(null), false);
      assert.equal(isValidLinkCode(undefined), false);
      assert.equal(isValidLinkCode("A8F3K"), false); // 5 символов
      assert.equal(isValidLinkCode("A8F3K29"), false); // 7 символов
      assert.equal(isValidLinkCode("A8-3K2"), false); // спецсимвол
      assert.equal(isValidLinkCode("A8 3K2"), false); // пробел внутри
    });
  });

  describe("Command Handlers", () => {
    test("handlePingCommand возвращает корректный Pong ответ", () => {
      const result = handlePingCommand(42);
      assert.match(result.content, /Pong! 🏓/);
      assert.match(result.content, /42ms/);
    });

    test("handleLinkCommand отклоняет некорректный код", async () => {
      const result = await handleLinkCommand("invalid_code", "channel_123");
      assert.equal(result.success, false);
      assert.match(result.message, /6 символов/);
    });

    test("handleLinkCommand принимает валидный 6-значный код", async () => {
      const result = await handleLinkCommand("A8F3K2", "channel_123");
      assert.equal(result.success, true);
      assert.match(result.message, /A8F3K2/);
    });

    test("handleLinkCommand с моком Supabase успешно обрабатывает Applied", async () => {
      const mockSupabase = {
        rpc: async (fn: string, params: { p_link_code: string; p_channel_id: string }) => {
          assert.equal(fn, "confirm_messenger_link");
          assert.equal(params.p_link_code, "A8F3K2");
          assert.equal(params.p_channel_id, "channel_123");
          return { data: { outcome: "Applied" }, error: null };
        },
      } as unknown as SupabaseClient;

      const result = await handleLinkCommand("A8F3K2", "channel_123", mockSupabase);
      assert.equal(result.success, true);
      assert.match(result.message, /успешно привязан к MedTracker/);
    });

    test("handleLinkCommand с моком Supabase обрабатывает ошибку ненайденного кода", async () => {
      const mockSupabase = {
        rpc: async () => ({ data: { outcome: "Rejected" }, error: null }),
      } as unknown as SupabaseClient;

      const result = await handleLinkCommand("A8F3K2", "channel_123", mockSupabase);
      assert.equal(result.success, false);
      assert.match(result.message, /не найден или его срок действия истёк/);
    });
  });

  describe("Bot Architecture & Config", () => {
    test("слэш-команды содержат ping и link с опцией 6 символов", () => {
      const pingCmd = slashCommands.find((c) => c.name === "ping");
      const linkCmd = slashCommands.find((c) => c.name === "link");

      assert.ok(pingCmd);
      assert.ok(linkCmd);

      const json = linkCmd.toJSON();
      const codeOption = json.options?.find((o: any) => o.name === "code") as any;
      assert.ok(codeOption);
      assert.equal(codeOption.min_length, 6);
      assert.equal(codeOption.max_length, 6);
    });

    test("createDiscordBot инициализирует клиент и методы", () => {
      const bot = createDiscordBot({
        token: "test_token",
        applicationId: "test_app_id",
      });

      assert.ok(bot.client);
      assert.equal(typeof bot.start, "function");
      assert.equal(typeof bot.stop, "function");
      assert.equal(typeof bot.sendTestMessage, "function");
    });

    test("validateConfig проверяет переменные окружения", () => {
      const validation = validateConfig();
      assert.equal(typeof validation.isValid, "boolean");
      assert.ok(Array.isArray(validation.missing));
    });
  });
});
