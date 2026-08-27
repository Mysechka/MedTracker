namespace Med.Application.Abstractions;

/// <summary>Обёртка над RPC materialize_upcoming_doses (источник истины — Postgres).</summary>
public interface IDoseEventMaterializer
{
    Task<int> MaterializeAsync(int horizonDays = 14, CancellationToken cancellationToken = default);
}
