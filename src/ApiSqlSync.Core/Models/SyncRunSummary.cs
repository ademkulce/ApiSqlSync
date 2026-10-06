namespace ApiSqlSync.Core.Models;

/// <summary>
/// Bir senkronizasyon turunun görüntülenebilir özetidir.
/// </summary>
public sealed record SyncRunSummary(
    Guid RunId,
    string JobId,
    string Runner,
    DateTime StartedAtUtc,
    DateTime? FinishedAtUtc,
    SyncRunStatus Status,
    long? ProcessedCount,
    string? ErrorType);