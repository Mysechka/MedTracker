namespace Med.Presentation.Messaging;

/// <summary>
/// Источник происхождения изменений сущности.
/// </summary>
public enum ChangeSource
{
    /// <summary>Локальное действие пользователя в приложении.</summary>
    Local = 0,

    /// <summary>Внешнее изменение через Supabase Realtime (бот, другое устройство, pg_cron).</summary>
    Realtime = 1,
}
