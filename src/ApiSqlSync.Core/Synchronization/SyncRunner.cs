using ApiSqlSync.Core.Abstractions;
using ApiSqlSync.Core.Models;

namespace ApiSqlSync.Core.Synchronization;

/// <summary>
/// Senkronizasyonu çalıştırır ve turun başlangıç/sonuç bilgilerini kaydeder.
/// </summary>
public sealed class SyncRunner
{
    private readonly SyncEngine _engine;
    private readonly ISyncRunStore _runStore;

    public SyncRunner(SyncEngine engine, ISyncRunStore runStore)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(runStore);

        _engine = engine;
        _runStore = runStore;
    }

    public async Task<long> RunAsync(string jobId, string runner, int pageSize, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobId);
        ArgumentException.ThrowIfNullOrWhiteSpace(runner);

        if (pageSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pageSize));
        }

        // Geçmiş kaydı açılamazsa senkronizasyonu başlatmıyoruz.
        var runId = await _runStore.StartAsync(jobId, runner, cancellationToken);

        long processedCount;

        try
        {
            processedCount = await _engine.RunAsync(jobId, pageSize, cancellationToken);
        }
        catch (Exception syncError)
        {
            var cancelled = syncError is OperationCanceledException && cancellationToken.IsCancellationRequested;

            var status = cancelled ? SyncRunStatus.Cancelled : SyncRunStatus.Failed;

            var errorType = cancelled ? null : syncError.GetType().Name;

            try
            {
                await SaveCompletionAsync(runId, status, processedCount: null, errorType: errorType);
            }
            catch (Exception historyError)
            {
                // Sonucu yazarken oluşan hata, asıl senkronizasyon
                // hatasını gizlemesin. İkisini birlikte taşıyoruz.
                throw new AggregateException("Synchronization failed and its result could not be saved.", syncError, historyError);
            }

            // Asıl hatayı, çağrı bilgisini koruyarak yeniden iletiyoruz.
            throw;
        }

        try
        {
            // Engine başarıyla tamamlandı. Bundan sonraki bir hata,
            // aktarımın değil çalışma geçmişinin kaydedilmesiyle ilgilidir.
            await SaveCompletionAsync(runId, SyncRunStatus.Succeeded, processedCount, errorType: null);
        }
        catch (Exception historyError)
        {
            throw new InvalidOperationException(
                $"Synchronization completed with {processedCount} records, " +
                $"but the result could not be saved. Run ID: {runId}.",
                historyError);
        }

        return processedCount;
    }

    private async Task SaveCompletionAsync(Guid runId, SyncRunStatus status, long? processedCount, string? errorType)
    {
        // Ctrl+C ile iptal edilen token'ı burada kullanmıyoruz.
        // İptal sonucunu kaydetmek için kısa, ayrı bir süre tanıyoruz.
        using var completionCancellation =
            new CancellationTokenSource(TimeSpan.FromSeconds(10));

        await _runStore.CompleteAsync(runId, status, processedCount, errorType, completionCancellation.Token);
    }
}