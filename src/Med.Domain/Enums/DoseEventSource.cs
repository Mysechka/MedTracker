namespace Med.Domain.Enums;

/// <summary>Кто инициировал переход состояния dose_event.</summary>
public enum DoseEventSource
{
    App = 0,
    Telegram = 1,
    Discord = 2,
    System = 3,
}
