using Med.Application.Abstractions;

namespace Med.Application.UseCases;

/// <summary>
/// Материализует dose_events на горизонт вперёд через SQL RPC.
/// Идемпотентность — dedupe_key в Postgres. C#-генератор остаётся для unit-тестов домена.
/// </summary>
public sealed class MaterializeUpcomingDosesUseCase(IDoseEventMaterializer materializer)
{
    public Task<int> ExecuteAsync(CancellationToken cancellationToken = default) =>
        materializer.MaterializeAsync(horizonDays: 14, cancellationToken);
}
