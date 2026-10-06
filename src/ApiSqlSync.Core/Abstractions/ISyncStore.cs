using ApiSqlSync.Core.Models;

namespace ApiSqlSync.Core.Abstractions;

/// <summary>
/// İşe ait kayıtları ve aktarımın kaldığı konumu saklamak için kullanılan sözleşme.
/// </summary>
public interface ISyncStore
{
    /// <summary>
    /// İşin son başarıyla kaydedilen konumunu getirir.
    /// Henüz checkpoint oluşmamışsa null döndürür.
    /// </summary>
    Task<SyncCursor?> LoadCheckpointAsync(string jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sayfadaki kayıtları upsert eder ve checkpoint'i aynı transaction içinde kaydeder.
    /// </summary>
    /// <remarks>
    /// Sayfa boş olmamalı ve kayıtlar cursor'a göre kesin artan sırada olmalıdır.
    /// Checkpoint, son kaydın cursor'ı olmalıdır.
    /// Aynı sayfanın tekrar işlenmesi mükerrer kayıt oluşturmamalıdır.
    /// Başarısızlık halinde kayıt değişiklikleri ve checkpoint birlikte geri alınmalıdır.
    /// </remarks>
    Task CommitPageAsync(string jobId, IReadOnlyList<SyncRecord> records, SyncCursor checkpoint, CancellationToken cancellationToken = default);
}