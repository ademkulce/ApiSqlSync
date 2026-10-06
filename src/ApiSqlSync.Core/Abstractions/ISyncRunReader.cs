using ApiSqlSync.Core.Models;

namespace ApiSqlSync.Core.Abstractions;

/// <summary>
/// Son senkronizasyon turlarını, isteğe bağlı iş filtresiyle okur.
/// </summary>
public interface ISyncRunReader
{
    Task<IReadOnlyList<SyncRunSummary>> ReadRecentAsync(
        int limit = 50,
        string? jobId = null,
        CancellationToken cancellationToken = default);
}