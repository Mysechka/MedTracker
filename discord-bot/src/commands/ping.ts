export interface PingCommandResult {
  content: string;
}

/**
 * Обработка команды ping
 * @param wsPing - задержка WebSocket в миллисекундах
 */
export function handlePingCommand(wsPing: number = -1): PingCommandResult {
  const latency = wsPing >= 0 ? ` (Пинг: ${wsPing}ms)` : "";
  return {
    content: `Pong! 🏓 MedTracker Bot активен и готов к работе.${latency}`,
  };
}
