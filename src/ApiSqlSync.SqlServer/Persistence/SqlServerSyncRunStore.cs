using System.Data;
using ApiSqlSync.Core.Abstractions;
using ApiSqlSync.Core.Models;
using ApiSqlSync.SqlServer.Configuration;
using Microsoft.Data.SqlClient;

namespace ApiSqlSync.SqlServer.Persistence;

/// <summary>
/// Senkronizasyon turlarının çalışma geçmişini SQL Server'da saklar.
/// </summary>
public sealed class SqlServerSyncRunStore : ISyncRunStore
{
    private readonly string _connectionString;
    private readonly int _commandTimeoutSeconds;

    public SqlServerSyncRunStore(SqlServerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        _connectionString = options.ConnectionString;
        _commandTimeoutSeconds = options.CommandTimeoutSeconds;
    }

    public async Task<Guid> StartAsync(string jobId, string runner, CancellationToken cancellationToken = default)
    {
        ValidateText(jobId, 128, nameof(jobId));
        ValidateText(runner, 32, nameof(runner));

        var runId = Guid.NewGuid();

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();

        command.CommandTimeout = _commandTimeoutSeconds;

        command.CommandText = """
            INSERT INTO dbo.SyncRuns
                (RunId, JobId, Runner, StartedAtUtc, Status)
            VALUES
                (
                    @runId,
                    @jobId,
                    @runner,
                    SYSUTCDATETIME(),
                    N'Running'
                );
            """;

        command.Parameters.Add("@runId", SqlDbType.UniqueIdentifier).Value = runId;

        command.Parameters.Add("@jobId", SqlDbType.NVarChar, 128).Value = jobId;

        command.Parameters.Add("@runner", SqlDbType.NVarChar, 32).Value = runner;

        await command.ExecuteNonQueryAsync(cancellationToken);

        return runId;
    }

    public async Task CompleteAsync(Guid runId, SyncRunStatus status, long? processedCount, string? errorType, CancellationToken cancellationToken = default)
    {
        if (runId == Guid.Empty)
        {
            throw new ArgumentException("Run ID cannot be empty.", nameof(runId));
        }

        // Tamamlama işleminde yalnızca bitiş durumlarını kabul ediyoruz.
        if (status is not (
            SyncRunStatus.Succeeded or
            SyncRunStatus.Failed or
            SyncRunStatus.Cancelled))
        {
            throw new ArgumentOutOfRangeException(
                nameof(status),
                "A terminal run status is required.");
        }

        if (status == SyncRunStatus.Succeeded)
        {
            if (!processedCount.HasValue || processedCount.Value < 0)
            {
                throw new ArgumentException("A successful run requires a non-negative record count.", nameof(processedCount));
            }
        }
        else if (processedCount.HasValue)
        {
            // Engine, hata veya iptal durumunda toplam sayıyı döndürmüyor.
            // Bilmediğimiz bir toplamı sıfır olarak kaydetmeyelim.
            throw new ArgumentException("Record count must be null for failed or cancelled runs.", nameof(processedCount));
        }

        if (status == SyncRunStatus.Failed)
        {
            ValidateText(errorType, 256, nameof(errorType));
        }
        else if (errorType is not null)
        {
            throw new ArgumentException("Error type is only allowed for failed runs.", nameof(errorType));
        }

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();

        command.CommandTimeout = _commandTimeoutSeconds;

        command.CommandText = """
            UPDATE dbo.SyncRuns
            SET
                FinishedAtUtc = SYSUTCDATETIME(),
                Status = @status,
                ProcessedCount = @processedCount,
                ErrorType = @errorType
            WHERE RunId = @runId
              AND Status = N'Running';
            """;

        command.Parameters.Add(
            "@runId",
            SqlDbType.UniqueIdentifier).Value = runId;

        command.Parameters.Add(
            "@status",
            SqlDbType.NVarChar,
            16).Value = status.ToString();

        command.Parameters.Add(
            "@processedCount",
            SqlDbType.BigInt).Value =
                processedCount.HasValue
                    ? (object)processedCount.Value
                    : DBNull.Value;

        command.Parameters.Add(
            "@errorType",
            SqlDbType.NVarChar,
            256).Value = (object?)errorType ?? DBNull.Value;

        var affectedRows =
            await command.ExecuteNonQueryAsync(cancellationToken);

        // Bitmiş bir turun sonucunu ikinci kez değiştirmiyoruz.
        if (affectedRows != 1)
        {
            throw new InvalidOperationException("Run was not found or has already been completed.");
        }
    }

    private static void ValidateText(string? value, int maxLength, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);

        if (value.Length > maxLength)
        {
            throw new ArgumentException($"Value cannot exceed {maxLength} characters.", parameterName);
        }
    }
}