import crypto from "node:crypto";

/**
 * ASN.1 SPKI DER header prefix for 32-byte Ed25519 public keys.
 */
const ED25519_SPKI_PREFIX = Buffer.from("302a300506032b6570032100", "hex");

/**
 * Валидация подписи Discord Interaction через Ed25519.
 * @param publicKeyHex - 64-символьный hex публичного ключа приложения Discord
 * @param signatureHex - 128-символьный hex подписи из заголовка X-Signature-Ed25519
 * @param timestamp - Значение из заголовка X-Signature-Timestamp
 * @param rawBody - Сырое тело HTTP-запроса (строка или Buffer)
 */
export function verifyDiscordSignature(
  publicKeyHex: string,
  signatureHex: string,
  timestamp: string,
  rawBody: string | Buffer,
): boolean {
  if (!publicKeyHex || !signatureHex || !timestamp) {
    return false;
  }

  try {
    const rawKey = Buffer.from(publicKeyHex.trim(), "hex");
    if (rawKey.length !== 32) {
      return false;
    }

    const rawSig = Buffer.from(signatureHex.trim(), "hex");
    if (rawSig.length !== 64) {
      return false;
    }

    const bodyString = typeof rawBody === "string" ? rawBody : rawBody.toString("utf8");
    const msg = Buffer.from(timestamp + bodyString, "utf8");

    const spkiKey = Buffer.concat([ED25519_SPKI_PREFIX, rawKey]);
    const pubKey = crypto.createPublicKey({
      key: spkiKey,
      format: "der",
      type: "spki",
    });

    return crypto.verify(null, msg, pubKey, rawSig);
  } catch (error) {
    return false;
  }
}

/**
 * Вспомогательная функция для генерации тестовой пары ключей Ed25519 и подписи сообщений (для тестов).
 */
export function createTestEd25519KeyPair() {
  const { publicKey, privateKey } = crypto.generateKeyPairSync("ed25519");

  const exportedPubDer = publicKey.export({ format: "der", type: "spki" });
  // Извлекаем последние 32 байта (raw public key)
  const rawPubHex = exportedPubDer.subarray(exportedPubDer.length - 32).toString("hex");

  return {
    publicKeyHex: rawPubHex,
    sign: (timestamp: string, body: string): string => {
      const msg = Buffer.from(timestamp + body, "utf8");
      const sig = crypto.sign(null, msg, privateKey);
      return sig.toString("hex");
    },
  };
}
