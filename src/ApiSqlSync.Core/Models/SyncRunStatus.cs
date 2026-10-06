namespace ApiSqlSync.Core.Models;

/// <summary>
/// Tek bir senkronizasyon turunun durumunu belirtir.
/// </summary>
public enum SyncRunStatus
{
    Running,
    Succeeded,
    Failed,
    Cancelled
}