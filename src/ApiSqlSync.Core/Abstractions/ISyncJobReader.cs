using ApiSqlSync.Core.Models;

namespace ApiSqlSync.Core.Abstractions;

/// <summary>
/// Checkpoint'i bulunan işlerin özet bilgilerini okur.
/// </summary>
public interface ISyncJobReader
{
    Task<IReadOnlyList<SyncJobSummary>> ReadJobsAsync(CancellationToken cancellationToken = default);
}