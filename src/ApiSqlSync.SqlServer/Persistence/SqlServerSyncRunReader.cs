using System.Data;
using ApiSqlSync.Core.Abstractions;
using ApiSqlSync.Core.Models;
using ApiSqlSync.SqlServer.Configuration;
using Microsoft.Data.SqlClient;

namespace ApiSqlSync.SqlServer.Persistence;

/// <summary>
/// Çalışma geçmişini en yeni turdan başlayarak okur.
/// </summary>
public sealed class SqlServerSyncRunReader : ISyncRunReader
{
    private readonly string _connectionString;
    private readonly int _commandTimeoutSeconds;

    public SqlServerSyncRunReader(SqlServerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        _connectionString = options.ConnectionString;
        _commandTimeoutSeconds = options.CommandTimeoutSeconds;
    }

    public async Task<IReadOnlyList<SyncRunSummary>> ReadRecentAsync(int limit = 50, string? jobId = null, CancellationToken cancellationToken = default)
    {
        // Boş filtre bütün işleri gösterir.
        jobId = string.IsNullOrWhiteSpace(jobId) ? null : jobId.Trim();

        if (jobId is { Length: > 128 })
        {
            throw new ArgumentException("Job ID cannot exceed 128 characters.", nameof(jobId));
        }

        // Geçmiş büyüse de tek istekte bütün tabloyu yüklemiyoruz.
        if (limit is < 1 or > 200)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), "Limit must be between 1 and 200.");
        }

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();

        command.CommandTimeout = _commandTimeoutSeconds;

        command.CommandText = """
    SELECT TOP (@limit)
        r.RunId,
        r.JobId,
        r.Runner,
        r.StartedAtUtc,
        r.FinishedAtUtc,
        r.Status,
        r.ProcessedCount,
        r.ErrorType
    FROM dbo.SyncRuns AS r
    WHERE (@jobId IS NULL OR r.JobId = @jobId)
    ORDER BY r.StartedAtUtc DESC, r.RunId DESC;
    """;

        command.Parameters.Add("@limit", SqlDbType.Int).Value = limit;
        command.Parameters.Add("@jobId", SqlDbType.NVarChar, 128).Value = (object?)jobId ?? DBNull.Value;

        var runs = new List<SyncRunSummary>();

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            var startedAtUtc = ReadUtcDateTime(reader, 3);

            DateTime? finishedAtUtc = reader.IsDBNull(4) ? null : ReadUtcDateTime(reader, 4);

            var status = Enum.Parse<SyncRunStatus>(reader.GetString(5), ignoreCase: true);

            long? processedCount = reader.IsDBNull(6) ? null : reader.GetInt64(6);

            string? errorType = reader.IsDBNull(7) ? null : reader.GetString(7);

            runs.Add(new SyncRunSummary(
                RunId: reader.GetGuid(0),
                JobId: reader.GetString(1),
                Runner: reader.GetString(2),
                StartedAtUtc: startedAtUtc,
                FinishedAtUtc: finishedAtUtc,
                Status: status,
                ProcessedCount: processedCount,
                ErrorType: errorType));
        }

        return runs;
    }

    private static DateTime ReadUtcDateTime(SqlDataReader reader, int ordinal)
    {
        // datetime2 saat dilimi taşımaz; bu alanları UTC olarak kaydediyoruz.
        return DateTime.SpecifyKind(reader.GetDateTime(ordinal), DateTimeKind.Utc);
    }
}