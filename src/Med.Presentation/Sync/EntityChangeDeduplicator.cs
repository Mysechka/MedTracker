using System.Collections.Concurrent;

namespace Med.Presentation.Sync;

/// <summary>
/// Предотвращает дублирующие перерисовки и race conditions между
/// локальными действиями пользователя и асинхронно возвращающимися Realtime-событиями Supabase.
/// </summary>
public sealed class EntityChangeDeduplicator
{
    private readonly ConcurrentDictionary<(Type EntityType, Guid EntityId), DateTimeOffset> _recentOperations = new();
    private readonly TimeSpan _window;

    public EntityChangeDeduplicator(TimeSpan? window = null)
    {
        _window = window ?? TimeSpan.FromSeconds(5);
    }

    /// <summary>
    /// Фиксирует факт локального изменения сущности пользователем.
    /// </summary>
    public void RecordLocalChange<T>(Guid id) => RecordLocalChange(typeof(T), id);

    public void RecordLocalChange(Type entityType, Guid id)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        _recentOperations[(entityType, id)] = now;
        TrimExpired(now);
    }

    /// <summary>
    /// Проверяет, было ли событие недавно инициировано локально,
    /// чтобы исключить повторную обработку эха от Realtime.
    /// </summary>
    public bool IsDuplicateOrRecentLocal<T>(Guid id) => IsDuplicateOrRecentLocal(typeof(T), id);

    public bool IsDuplicateOrRecentLocal(Type entityType, Guid id)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        if (_recentOperations.TryGetValue((entityType, id), out DateTimeOffset timestamp))
        {
            if (now - timestamp <= _window)
            {
                return true;
            }

            _recentOperations.TryRemove((entityType, id), out _);
        }

        return false;
    }

    private void TrimExpired(DateTimeOffset now)
    {
        // Не блокируем поток очисткой, если записей немного
        if (_recentOperations.Count > 100)
        {
            foreach (var kvp in _recentOperations)
            {
                if (now - kvp.Value > _window)
                {
                    _recentOperations.TryRemove(kvp.Key, out _);
                }
            }
        }
    }
}
