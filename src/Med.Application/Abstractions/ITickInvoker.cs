namespace Med.Application.Abstractions;

public sealed record TickInvokeResult(bool Ok, string RawBody);

/// <summary>Ручной вызов Edge Function tick (диагностика). Без секретов в клиенте — JWT сессии.</summary>
public interface ITickInvoker
{
    Task<TickInvokeResult> InvokeAsync(CancellationToken cancellationToken = default);
}
