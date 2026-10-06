using ApiSqlSync.Core.Models;

namespace ApiSqlSync.Core.Abstractions;

/// <summary>
/// Senkronizasyon turlarının başlangıç ve sonuç bilgilerini saklar.
/// </summary>
public interface ISyncRunStore
{
    Task<Guid> StartAsync(string jobId, string runner, CancellationToken cancellationToken = default);

    Task CompleteAsync(Guid runId, SyncRunStatus status, long? processedCount, string? errorType, CancellationToken cancellationToken = default);
}