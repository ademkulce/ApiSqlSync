using ApiSqlSync.Core.Models;

namespace ApiSqlSync.Web.Models;

/// <summary>
/// Geçmiş ekranının filtresini ve sonuçlarını birlikte taşır.
/// </summary>
public sealed class SyncRunHistoryViewModel
{
    public string? JobId { get; init; }

    public IReadOnlyList<SyncRunSummary> Runs { get; init; } = [];
}