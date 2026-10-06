using System.Data;
using ApiSqlSync.Core.Models;
using ApiSqlSync.SqlServer.Configuration;
using ApiSqlSync.SqlServer.Persistence;
using Microsoft.Data.SqlClient;
using Xunit;

namespace ApiSqlSync.IntegrationTests;

public class SqlServerSyncStoreTests
{
    [Fact]
    public async Task CommitPageAsync_ShouldReplaySafelyAndRollbackInvalidPage()
    {
        string connectionString = GetConnectionString();
        string jobId = "test-" + Guid.NewGuid().ToString("N");

        var store = new SqlServerSyncStore(new SqlServerOptions
        {
            ConnectionString = connectionString
        });

        try
        {
            // Yeni bir işin checkpoint'i bulunmamalı.
            Assert.Null(await store.LoadCheckpointAsync(jobId));

            var firstRecord = new SyncRecord(
                new SyncCursor(1000, 41),
                """{"id":41,"name":"İlk değer"}""");

            await store.CommitPageAsync(
                jobId,
                new[] { firstRecord },
                firstRecord.Cursor);

            // Aynı sayfa tekrar işlendiğinde ikinci bir kayıt oluşmamalı.
            await store.CommitPageAsync(
                jobId,
                new[] { firstRecord },
                firstRecord.Cursor);

            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();

            Assert.Equal(1, await ReadCountAsync(connection, jobId));

            Assert.Equal(
                firstRecord.Cursor,
                await store.LoadCheckpointAsync(jobId));

            var updatedRecord = new SyncRecord(
                new SyncCursor(1001, 41),
                """{"id":41,"name":"Yeni değer"}""");

            await store.CommitPageAsync(
                jobId,
                new[] { updatedRecord },
                updatedRecord.Cursor);

            Assert.Equal(1, await ReadCountAsync(connection, jobId));

            Assert.Equal(
                updatedRecord.Payload,
                await ReadPayloadAsync(connection, jobId, 41));

            // İlk kayıt güncellenebilir, fakat ikinci kaydın JSON'u geçersiz.
            // Sayfanın tamamı rollback olmalı.
            var invalidPage = new[]
            {
                new SyncRecord(
                    new SyncCursor(1002, 41),
                    """{"id":41,"name":"Kaydedilmemesi gereken değer"}"""),

                new SyncRecord(
                    new SyncCursor(1003, 42),
                    "invalid-json")
            };

            await Assert.ThrowsAsync<SqlException>(() =>
                store.CommitPageAsync(
                    jobId,
                    invalidPage,
                    invalidPage[^1].Cursor));

            // Başarısız sayfa ne önceki veriyi ne de checkpoint'i değiştirmeli.
            Assert.Equal(1, await ReadCountAsync(connection, jobId));

            Assert.Equal(
                updatedRecord.Payload,
                await ReadPayloadAsync(connection, jobId, 41));

            Assert.Equal(
                updatedRecord.Cursor,
                await store.LoadCheckpointAsync(jobId));
        }
        finally
        {
            // Test başarısız olsa da yalnızca bu teste ait kayıtları temizliyoruz.
            await CleanupAsync(connectionString, jobId);
        }
    }

    private static string GetConnectionString()
    {
        return Environment.GetEnvironmentVariable(
            "APISQLSYNC_TEST_CONNECTION")
            ?? throw new InvalidOperationException(
                "Set APISQLSYNC_TEST_CONNECTION and restart Visual Studio.");
    }

    private static async Task<int> ReadCountAsync(
        SqlConnection connection,
        string jobId)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = """
            SELECT COUNT(*)
            FROM dbo.SyncRecords
            WHERE JobId = @jobId;
            """;

        command.Parameters.Add(
            "@jobId", SqlDbType.NVarChar, 128).Value = jobId;

        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static async Task<string?> ReadPayloadAsync(
        SqlConnection connection,
        string jobId,
        long id)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = """
            SELECT Payload
            FROM dbo.SyncRecords
            WHERE JobId = @jobId AND Id = @id;
            """;

        command.Parameters.Add(
            "@jobId", SqlDbType.NVarChar, 128).Value = jobId;

        command.Parameters.Add(
            "@id", SqlDbType.BigInt).Value = id;

        return (string?)await command.ExecuteScalarAsync();
    }

    private static async Task CleanupAsync(
        string connectionString,
        string jobId)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();

        command.CommandText = """
            DELETE FROM dbo.SyncRecords WHERE JobId = @jobId;
            DELETE FROM dbo.SyncCheckpoints WHERE JobId = @jobId;
            """;

        command.Parameters.Add(
            "@jobId", SqlDbType.NVarChar, 128).Value = jobId;

        await command.ExecuteNonQueryAsync();
    }
}