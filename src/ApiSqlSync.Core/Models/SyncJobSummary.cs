namespace ApiSqlSync.Core.Models;

/// <summary>
/// Bir senkronizasyon işinin SQL'de saklanan durumunu gösterir.
/// </summary>
public sealed record SyncJobSummary(string JobId, long RecordCount, SyncCursor Checkpoint, DateTime CheckpointSavedAtUtc);
