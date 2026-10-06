using ApiSqlSync.Core.Abstractions;
using ApiSqlSync.Core.Models;
using ApiSqlSync.Core.Synchronization;
using Xunit;

namespace ApiSqlSync.Core.Tests;

public sealed class SyncRunnerTests
{
    [Fact]
    public async Task EmptySource_CompletesSuccessfullyWithZeroRecords()
    {
        var source = new StubSource();
        var store = new StubSyncStore();
        var runStore = new StubRunStore();

        var runner = new SyncRunner(
            new SyncEngine(source, store),
            runStore);

        var count = await runner.RunAsync(
            "test-job",
            "Cli",
            pageSize: 3);

        Assert.Equal(0L, count);

        var completion = Assert.Single(runStore.Completions);

        Assert.Equal(SyncRunStatus.Succeeded, completion.Status);
        Assert.Equal((long?)0, completion.ProcessedCount);
        Assert.Null(completion.ErrorType);
    }

    [Fact]
    public async Task SourceFailure_PreservesOriginalException()
    {
        var sourceError = new InvalidOperationException(
            "Source is unavailable.");

        var source = new StubSource
        {
            ReadHandler = _ =>
                Task.FromException<IReadOnlyList<SyncRecord>>(sourceError)
        };

        var runStore = new StubRunStore();

        var runner = new SyncRunner(
            new SyncEngine(source, new StubSyncStore()),
            runStore);

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(
            () => runner.RunAsync("test-job", "Cli", 3));

        // Hata yeniden oluşturulmasın; asıl exception korunmalı.
        Assert.Same(sourceError, actual);

        var completion = Assert.Single(runStore.Completions);

        Assert.Equal(SyncRunStatus.Failed, completion.Status);
        Assert.Null(completion.ProcessedCount);
        Assert.Equal(
            nameof(InvalidOperationException),
            completion.ErrorType);
    }

    [Fact]
    public async Task Cancellation_SavesResultWithAnUncancelledToken()
    {
        using var cancellation = new CancellationTokenSource();

        var source = new StubSource
        {
            ReadHandler = token =>
            {
                // Tur başladıktan sonra kapanma isteği geldiğini temsil ediyor.
                cancellation.Cancel();

                return Task.FromException<IReadOnlyList<SyncRecord>>(
                    new OperationCanceledException(token));
            }
        };

        var runStore = new StubRunStore();

        var runner = new SyncRunner(
            new SyncEngine(source, new StubSyncStore()),
            runStore);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => runner.RunAsync(
                "test-job",
                "Worker",
                3,
                cancellation.Token));

        var completion = Assert.Single(runStore.Completions);

        Assert.Equal(SyncRunStatus.Cancelled, completion.Status);
        Assert.Null(completion.ProcessedCount);
        Assert.Null(completion.ErrorType);

        // Sonuç kaydı için zaten iptal edilmiş çalışma token'ı kullanılmamalı.
        Assert.False(completion.CancellationRequested);
    }

    [Fact]
    public async Task SuccessHistoryFailure_DoesNotMarkSynchronizationAsFailed()
    {
        var source = new StubSource();

        source.ReadHandler = _ =>
            Task.FromResult<IReadOnlyList<SyncRecord>>(
                source.ReadCalls == 1
                    ? new[]
                    {
                        new SyncRecord(
                            new SyncCursor(1000, 1),
                            """{"id":1,"updated_at":1000}""")
                    }
                    : Array.Empty<SyncRecord>());

        var store = new StubSyncStore();

        var historyError = new InvalidOperationException(
            "History could not be saved.");

        var runStore = new StubRunStore
        {
            CompleteError = historyError
        };

        var runner = new SyncRunner(
            new SyncEngine(source, store),
            runStore);

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(
            () => runner.RunAsync("test-job", "Cli", 3));

        Assert.Same(historyError, actual.InnerException);

        // Geçmiş kaydı başarısız olsa da tamamlanan veri aktarımı korunur.
        Assert.Equal(
            (SyncCursor?)new SyncCursor(1000, 1),
            store.Checkpoint);

        var completion = Assert.Single(runStore.Completions);

        // Başarı kaydı yazılamadı diye ardından Failed yazılmamalı.
        Assert.Equal(SyncRunStatus.Succeeded, completion.Status);
        Assert.Equal((long?)1, completion.ProcessedCount);
    }

    [Fact]
    public async Task SourceAndHistoryFailure_PreservesBothExceptions()
    {
        var sourceError = new InvalidOperationException(
            "Source failed.");

        var historyError = new InvalidOperationException(
            "History failed.");

        var source = new StubSource
        {
            ReadHandler = _ =>
                Task.FromException<IReadOnlyList<SyncRecord>>(sourceError)
        };

        var runStore = new StubRunStore
        {
            CompleteError = historyError
        };

        var runner = new SyncRunner(
            new SyncEngine(source, new StubSyncStore()),
            runStore);

        var actual = await Assert.ThrowsAsync<AggregateException>(
            () => runner.RunAsync("test-job", "Cli", 3));

        Assert.Equal(2, actual.InnerExceptions.Count);
        Assert.Contains(sourceError, actual.InnerExceptions);
        Assert.Contains(historyError, actual.InnerExceptions);
    }

    [Fact]
    public async Task StartHistoryFailure_DoesNotReadSource()
    {
        var startError = new InvalidOperationException(
            "Run could not be started.");

        var source = new StubSource();

        var runStore = new StubRunStore
        {
            StartError = startError
        };

        var runner = new SyncRunner(
            new SyncEngine(source, new StubSyncStore()),
            runStore);

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(
            () => runner.RunAsync("test-job", "Cli", 3));

        Assert.Same(startError, actual);
        Assert.Equal(0, source.ReadCalls);
        Assert.Empty(runStore.Completions);
    }

    private sealed class StubSource : IPageSource
    {
        public int ReadCalls { get; private set; }

        public Func<
            CancellationToken,
            Task<IReadOnlyList<SyncRecord>>> ReadHandler
        { get; set; }
                = _ => Task.FromResult<IReadOnlyList<SyncRecord>>(
                    Array.Empty<SyncRecord>());

        public Task<IReadOnlyList<SyncRecord>> ReadPageAsync(
            SyncCursor? after,
            int pageSize,
            CancellationToken cancellationToken = default)
        {
            ReadCalls++;

            return ReadHandler(cancellationToken);
        }
    }

    private sealed class StubSyncStore : ISyncStore
    {
        public SyncCursor? Checkpoint { get; private set; }

        public Task<SyncCursor?> LoadCheckpointAsync(
            string jobId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Checkpoint);
        }

        public Task CommitPageAsync(
            string jobId,
            IReadOnlyList<SyncRecord> records,
            SyncCursor checkpoint,
            CancellationToken cancellationToken = default)
        {
            Checkpoint = checkpoint;

            return Task.CompletedTask;
        }
    }

    private sealed class StubRunStore : ISyncRunStore
    {
        private readonly Guid _runId = Guid.NewGuid();

        public Exception? StartError { get; init; }
        public Exception? CompleteError { get; init; }

        public List<CompletionAttempt> Completions { get; } = [];

        public Task<Guid> StartAsync(
            string jobId,
            string runner,
            CancellationToken cancellationToken = default)
        {
            return StartError is { } error
                ? Task.FromException<Guid>(error)
                : Task.FromResult(_runId);
        }

        public Task CompleteAsync(
            Guid runId,
            SyncRunStatus status,
            long? processedCount,
            string? errorType,
            CancellationToken cancellationToken = default)
        {
            // Yazma başarısız olsa bile hangi sonucun denenmiş olduğunu tutuyoruz.
            Completions.Add(new CompletionAttempt(
                status,
                processedCount,
                errorType,
                cancellationToken.IsCancellationRequested));

            return CompleteError is { } error
                ? Task.FromException(error)
                : Task.CompletedTask;
        }
    }

    private sealed record CompletionAttempt(
        SyncRunStatus Status,
        long? ProcessedCount,
        string? ErrorType,
        bool CancellationRequested);
}