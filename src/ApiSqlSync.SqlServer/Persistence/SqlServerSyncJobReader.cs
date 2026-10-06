using ApiSqlSync.Core.Abstractions;
using ApiSqlSync.Core.Models;
using ApiSqlSync.SqlServer.Configuration;
using Microsoft.Data.SqlClient;

namespace ApiSqlSync.SqlServer.Persistence;

/// <summary>
/// İşlerin kayıt sayılarını ve checkpoint bilgilerini SQL Server'dan okur.
/// </summary>
public sealed class SqlServerSyncJobReader : ISyncJobReader
{
    private readonly string _connectionString;
    private readonly int _commandTimeoutSeconds;

    public SqlServerSyncJobReader(SqlServerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        _connectionString = options.ConnectionString;
        _commandTimeoutSeconds = options.CommandTimeoutSeconds;
    }

    public async Task<IReadOnlyList<SyncJobSummary>> ReadJobsAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();

        command.CommandTimeout = _commandTimeoutSeconds;

        command.CommandText = """
    SELECT
        cp.JobId,
        cp.UpdatedAt,
        cp.LastId,
        cp.SavedAtUtc,
        (
            SELECT COUNT_BIG(*)
            FROM dbo.SyncRecords AS sr
            WHERE sr.JobId = cp.JobId
        ) AS RecordCount
    FROM dbo.SyncCheckpoints AS cp
    ORDER BY cp.JobId;
    """;

        var jobs = new List<SyncJobSummary>();

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            var checkpoint = new SyncCursor(
                UpdatedAt: reader.GetInt64(1),
                Id: reader.GetInt64(2));

            // SQL'deki datetime2 saat dilimi bilgisi taşımaz.
            // Bu alanı SYSUTCDATETIME ile yazdığımız için UTC olarak işaretliyoruz.
            var savedAtUtc = DateTime.SpecifyKind(
                reader.GetDateTime(3),
                DateTimeKind.Utc);

            jobs.Add(new SyncJobSummary(
                JobId: reader.GetString(0),
                RecordCount: reader.GetInt64(4),
                Checkpoint: checkpoint,
                CheckpointSavedAtUtc: savedAtUtc));
        }

        return jobs;
    }
}