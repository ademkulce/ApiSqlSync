using System.Data;
using System.Text.Json;
using ApiSqlSync.Core.Abstractions;
using ApiSqlSync.Core.Models;
using ApiSqlSync.SqlServer.Configuration;
using Microsoft.Data.SqlClient;

namespace ApiSqlSync.SqlServer.Persistence;

/// <summary>
/// Senkronizasyon kayıtlarını ve checkpoint'leri SQL Server'da saklar.
/// </summary>
public sealed class SqlServerSyncStore : ISyncStore
{
    private readonly string _connectionString;
    private readonly int _commandTimeoutSeconds;

    public SqlServerSyncStore(SqlServerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        _connectionString = options.ConnectionString;
        _commandTimeoutSeconds = options.CommandTimeoutSeconds;
    }

    /// <summary>
    /// İşin son kaydedilen konumunu okur.
    /// Henüz çalışmamış bir iş için null döndürür.
    /// </summary>
    public async Task<SyncCursor?> LoadCheckpointAsync(
        string jobId,
        CancellationToken cancellationToken = default)
    {
        ValidateJobId(jobId);

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();

        command.CommandTimeout = _commandTimeoutSeconds;

        command.CommandText = """
            SELECT UpdatedAt, LastId
            FROM dbo.SyncCheckpoints
            WHERE JobId = @jobId;
            """;

        // Parametrenin tipi ve uzunluğu tablodaki tanımla aynı.
        command.Parameters.Add(
            "@jobId",
            SqlDbType.NVarChar,
            128).Value = jobId;

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new SyncCursor(
            UpdatedAt: reader.GetInt64(0),
            Id: reader.GetInt64(1));
    }

    /// <summary>
    /// Sayfayı JSON olarak SQL Server'a aktarır.
    /// Kayıtları ve checkpoint'i aynı transaction içinde kaydeder.
    /// </summary>
    public async Task CommitPageAsync(
        string jobId,
        IReadOnlyList<SyncRecord> records,
        SyncCursor checkpoint,
        CancellationToken cancellationToken = default)
    {
        ValidateJobId(jobId);
        ArgumentNullException.ThrowIfNull(records);

        if (records.Count == 0)
        {
            throw new ArgumentException(
                "Page cannot be empty.",
                nameof(records));
        }

        // Önce sayfayı doğruluyoruz; hatalı veri için bağlantı açmayalım.
        SyncCursor? previous = null;
        var ids = new HashSet<long>();
        var batch = new List<StagingRecord>(records.Count);

        foreach (var record in records)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (record is null)
            {
                throw new ArgumentException(
                    "Page cannot contain null records.",
                    nameof(records));
            }

            if (previous.HasValue &&
                record.Cursor.CompareTo(previous.Value) <= 0)
            {
                throw new ArgumentException(
                    "Records must strictly increase by cursor.",
                    nameof(records));
            }

            // Aynı sayfada aynı kaydın iki sürümünü kabul etmiyoruz.
            if (!ids.Add(record.Cursor.Id))
            {
                throw new ArgumentException(
                    "Record IDs must be unique within a page.",
                    nameof(records));
            }

            batch.Add(new StagingRecord(
                Id: record.Cursor.Id,
                UpdatedAt: record.Cursor.UpdatedAt,
                Payload: record.Payload));

            previous = record.Cursor;
        }

        if (records[^1].Cursor != checkpoint)
        {
            throw new ArgumentException(
                "Checkpoint must match the last record.",
                nameof(checkpoint));
        }

        // Payload bir metin olarak taşınır.
        // Serialize işlemi içindeki tırnakları ve özel karakterleri kaçırır.
        var batchJson = JsonSerializer.Serialize(batch);

        cancellationToken.ThrowIfCancellationRequested();

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var transaction =
            (SqlTransaction)await connection.BeginTransactionAsync(
                cancellationToken);

        await using var command = connection.CreateCommand();

        command.Transaction = transaction;
        command.CommandTimeout = _commandTimeoutSeconds;

        command.Parameters.Add(
            "@jobId",
            SqlDbType.NVarChar,
            128).Value = jobId;

        command.Parameters.Add(
            "@updatedAt",
            SqlDbType.BigInt).Value = checkpoint.UpdatedAt;

        command.Parameters.Add(
            "@lastId",
            SqlDbType.BigInt).Value = checkpoint.Id;

        // -1, nvarchar(max) anlamına gelir; uzun payload'lar kesilmesin.
        command.Parameters.Add(
            "@batchJson",
            SqlDbType.NVarChar,
            -1).Value = batchJson;

        command.CommandText = """
            SET XACT_ABORT ON;

            DECLARE @lockResult int;

            -- Mevcut tasarımda sayfa yazımlarını sırayla yürütüyoruz.
            -- Kilit, transaction tamamlandığında serbest bırakılır.
            EXEC @lockResult = sys.sp_getapplock
                @Resource = N'ApiSqlSync:page-write',
                @LockMode = 'Exclusive',
                @LockOwner = 'Transaction',
                @LockTimeout = 5000;

            IF @lockResult < 0
                THROW 50001, 'Could not acquire sync write lock.', 1;

            -- Daha eski bir sayfa checkpoint'i geriye taşıyamaz.
            IF EXISTS
            (
                SELECT 1
                FROM dbo.SyncCheckpoints WITH (UPDLOCK, HOLDLOCK)
                WHERE JobId = @jobId
                  AND
                  (
                      UpdatedAt > @updatedAt
                      OR
                      (
                          UpdatedAt = @updatedAt
                          AND LastId > @lastId
                      )
                  )
            )
                THROW 50002, 'Checkpoint cannot move backwards.', 1;

            -- Oluşturma ve kullanım aynı SQL komutunda.
            -- Geçici tabloyu farklı komutlar arasında taşımıyoruz.
            CREATE TABLE #SyncBatch
            (
                Id bigint NOT NULL PRIMARY KEY,
                UpdatedAt bigint NOT NULL,
                Payload nvarchar(max) NOT NULL
            );

            INSERT INTO #SyncBatch
                (Id, UpdatedAt, Payload)
            SELECT
                Id,
                UpdatedAt,
                Payload
            FROM OPENJSON(@batchJson)
            WITH
            (
                Id bigint 'strict $.Id',
                UpdatedAt bigint 'strict $.UpdatedAt',
                Payload nvarchar(max) 'strict $.Payload'
            );

            DECLARE @now datetime2(7) = SYSUTCDATETIME();

            -- Hedefte daha yeni bir sürüm varsa eski veriyle ezmeyelim.
            UPDATE target
            SET
                UpdatedAt = batch.UpdatedAt,
                Payload = batch.Payload,
                SyncedAtUtc = @now
            FROM dbo.SyncRecords AS target
            INNER JOIN #SyncBatch AS batch
                ON batch.Id = target.Id
            WHERE target.JobId = @jobId
              AND batch.UpdatedAt >= target.UpdatedAt;

            -- Hedefte bulunmayan kayıtları ekliyoruz.
            INSERT INTO dbo.SyncRecords
                (JobId, Id, UpdatedAt, Payload, SyncedAtUtc)
            SELECT
                @jobId,
                batch.Id,
                batch.UpdatedAt,
                batch.Payload,
                @now
            FROM #SyncBatch AS batch
            WHERE NOT EXISTS
            (
                SELECT 1
                FROM dbo.SyncRecords AS target
                WHERE target.JobId = @jobId
                  AND target.Id = batch.Id
            );

            UPDATE dbo.SyncCheckpoints
            SET
                UpdatedAt = @updatedAt,
                LastId = @lastId,
                SavedAtUtc = @now
            WHERE JobId = @jobId;

            IF @@ROWCOUNT = 0
            BEGIN
                INSERT INTO dbo.SyncCheckpoints
                    (JobId, UpdatedAt, LastId, SavedAtUtc)
                VALUES
                    (@jobId, @updatedAt, @lastId, @now);
            END;

            DROP TABLE #SyncBatch;
            """;

        await command.ExecuteNonQueryAsync(cancellationToken);

        // Kayıtlar ve ilerleme bilgisi birlikte kalıcı hâle gelir.
        // Commit öncesindeki bir hata, transaction'ın geri alınmasını sağlar.
        await transaction.CommitAsync(cancellationToken);
    }

    private static void ValidateJobId(string jobId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobId);

        if (jobId.Length > 128)
        {
            throw new ArgumentException(
                "JobId cannot exceed 128 characters.",
                nameof(jobId));
        }
    }

    /// <summary>
    /// SQL'e gönderilen sayfanın satır yapısı.
    /// Alan adları OPENJSON içindeki yollarla eşleşir.
    /// </summary>
    private sealed record StagingRecord(
        long Id,
        long UpdatedAt,
        string Payload);
}