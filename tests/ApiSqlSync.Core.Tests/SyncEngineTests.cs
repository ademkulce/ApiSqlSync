using ApiSqlSync.Core.Abstractions;
using ApiSqlSync.Core.Models;
using ApiSqlSync.Core.Synchronization;
using Xunit;

namespace ApiSqlSync.Core.Tests;

public class SyncEngineTests
{
    [Fact]
    public async Task RunAsync_ShouldSaveAllPagesAndLastCheckpoint()
    {
        // Aynı zaman değerindeki kayıtlar sayfa sınırında da korunmalı.
        var records = new[]
        {
            CreateRecord(1000, 41),
            CreateRecord(1000, 42),
            CreateRecord(1000, 43),
            CreateRecord(1001, 10)
        };

        var source = new FakePageSource(records);
        var store = new FakeSyncStore();
        var engine = new SyncEngine(source, store);

        long processedCount = await engine.RunAsync(jobId: "customers", pageSize: 2);

        Assert.Equal(4L, processedCount);
        Assert.Equal(2, store.CommitCount);
        Assert.Equal(records, store.SavedRecords.ToArray());
        Assert.Equal(new SyncCursor(1001, 10), store.Checkpoint);

        // İki dolu sayfadan sonra bitişi anlamak için boş sayfa okunur.
        Assert.Equal(3, source.ReadCount);
    }

    [Fact]
    public async Task RunAsync_ShouldResumeFromLastCheckpointAfterCommitFailure()
    {
        var records = new[]
        {
        CreateRecord(1000, 41),
        CreateRecord(1000, 42),
        CreateRecord(1000, 43),
        CreateRecord(1001, 10)
    };

        var source = new FakePageSource(records);

        // İlk sayfa kaydedilecek, ikinci kaydetme denemesi hata verecek.
        var store = new FakeSyncStore
        {
            FailOnCommitAttempt = 2
        };

        var engine = new SyncEngine(source, store);

        await Assert.ThrowsAsync<IOException>(() =>
            engine.RunAsync("customers", pageSize: 2));

        // Başarısız sayfa checkpoint'i ilerletmemeli.
        Assert.Equal(new SyncCursor(1000, 42), store.Checkpoint);
        Assert.Equal(records.Take(2).ToArray(), store.SavedRecords.ToArray());
        Assert.Equal(1, store.CommitCount);

        // Kaydetme hatasından sonra başka sayfa okunmamalı.
        Assert.Equal(2, source.ReadCount);

        // Hata giderildikten sonra yeni bir engine ile tekrar başlatıyoruz.
        store.FailOnCommitAttempt = null;

        var restartedEngine = new SyncEngine(source, store);

        long processedCount = await restartedEngine.RunAsync(
            "customers",
            pageSize: 2);

        Assert.Equal(2L, processedCount);
        Assert.Equal(records, store.SavedRecords.ToArray());
        Assert.Equal(new SyncCursor(1001, 10), store.Checkpoint);
        Assert.Equal(2, store.CommitCount);
    }

    private static SyncRecord CreateRecord(long updatedAt, long id)
    {
        return new SyncRecord(new SyncCursor(updatedAt, id), $"{{\"id\":{id},\"updated_at\":{updatedAt}}}");
    }

    /// <summary>
    /// Bellekteki kayıtları gerçek kaynağın cursor kurallarına göre sayfalar.
    /// </summary>
    private sealed class FakePageSource : IPageSource
    {
        private readonly IReadOnlyList<SyncRecord> _records;

        public int ReadCount { get; private set; }

        public FakePageSource(IReadOnlyList<SyncRecord> records)
        {
            _records = records;
        }

        public Task<IReadOnlyList<SyncRecord>> ReadPageAsync(SyncCursor? after, int pageSize, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            ReadCount++;

            IReadOnlyList<SyncRecord> page = _records
                .Where(record =>
                    !after.HasValue ||
                    record.Cursor.CompareTo(after.Value) > 0)
                .OrderBy(record => record.Cursor)
                .Take(pageSize)
                .ToArray();

            return Task.FromResult(page);
        }
    }

    /// <summary>
    /// Kaydetme çağrılarını gözlemler ve belirlenen denemede hata üretir.
    /// Gerçek SQL transaction ve upsert davranışı entegrasyon testlerinde sınanır.
    /// </summary>
    private sealed class FakeSyncStore : ISyncStore
    {
        private int _commitAttempt;

        public List<SyncRecord> SavedRecords { get; } = new();

        public SyncCursor? Checkpoint { get; private set; }

        public int CommitCount { get; private set; }

        public int? FailOnCommitAttempt { get; set; }

        public Task<SyncCursor?> LoadCheckpointAsync(
            string jobId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult(Checkpoint);
        }

        public Task CommitPageAsync(
            string jobId,
            IReadOnlyList<SyncRecord> records,
            SyncCursor checkpoint,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            _commitAttempt++;

            // Başarısız kaydetmede kalıcı durumun değişmemesini taklit ediyoruz.
            if (_commitAttempt == FailOnCommitAttempt)
            {
                throw new IOException("Simulated page commit failure.");
            }

            SavedRecords.AddRange(records);
            Checkpoint = checkpoint;
            CommitCount++;

            return Task.CompletedTask;
        }
    }


}