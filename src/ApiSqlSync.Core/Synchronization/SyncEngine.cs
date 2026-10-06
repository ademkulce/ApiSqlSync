using ApiSqlSync.Core.Abstractions;
using ApiSqlSync.Core.Models;

namespace ApiSqlSync.Core.Synchronization;

/// <summary>
/// Kaynaktan okunan sayfaları hedefe aktarır ve kaydedilen konumu takip eder.
/// Kaynak ve hedefe ait teknik işlemler ilgili implementasyonlara bırakılır.
/// </summary>
public sealed class SyncEngine
{
    private readonly IPageSource _source;
    private readonly ISyncStore _store;

    public SyncEngine(IPageSource source, ISyncStore store)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(store);

        _source = source;
        _store = store;
    }

    /// <summary>
    /// Son kaydedilen konumdan başlayarak boş sayfa gelene kadar aktarımı sürdürür.
    /// Bu çalışmada başarıyla kaydedilen kayıt sayısını döndürür.
    /// </summary>
    public async Task<long> RunAsync(string jobId, int pageSize, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobId);

        if (pageSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pageSize));
        }

        cancellationToken.ThrowIfCancellationRequested();

        // İlk çalışmada checkpoint bulunmaz; null cursor ile baştan okunur.
        SyncCursor? cursor = await _store.LoadCheckpointAsync(jobId, cancellationToken);

        long processedCount = 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var records = await _source.ReadPageAsync(cursor, pageSize, cancellationToken);

            // Az kayıt dönmesi yeterli değil; kaynak kendi sayfa limitini uygulayabilir.
            if (records.Count == 0)
            {
                return processedCount;
            }

            if (records.Count > pageSize)
            {
                throw new InvalidDataException("Source returned more records than requested.");
            }

            // İlk kayıt da önceki sayfanın checkpoint'inden sonra gelmeli.
            SyncCursor? previous = cursor;

            foreach (var record in records)
            {
                // Geri giden veya tekrarlanan cursor, ilerleme bilgisini geçersiz kılar.
                // Sayfanın tamamını kaydetmeden önce sıralamayı doğruluyoruz.
                if (previous.HasValue && record.Cursor.CompareTo(previous.Value) <= 0)
                {
                    throw new InvalidDataException("Records must strictly increase by cursor.");
                }

                previous = record.Cursor;
            }

            SyncCursor nextCursor = records[^1].Cursor;

            // Kayıtlar ve checkpoint hedefte aynı transaction ile kaydedilmeli.
            await _store.CommitPageAsync(jobId, records, nextCursor, cancellationToken);

            // Kaydetme başarısız olursa bu konuma ilerlememeliyiz.
            cursor = nextCursor;
            processedCount += records.Count;
        }
    }
}
