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

            // Удаляем только если значение не обновилось другим потоком
            ((ICollection<KeyValuePair<(Type, Guid), DateTimeOffset>>)_recentOperations)
                .Remove(new KeyValuePair<(Type, Guid), DateTimeOffset>((entityType, id), timestamp));
        }

        return false;
    }

    private void TrimExpired(DateTimeOffset now)
    {
        foreach (var kvp in _recentOperations)
        {
            if (now - kvp.Value > _window)
            {
                // Удаляем ТОЛЬКО если значение не изменилось (атомарная проверка)
                ((ICollection<KeyValuePair<(Type, Guid), DateTimeOffset>>)_recentOperations)
                    .Remove(kvp);
            }
        }
    }
}
