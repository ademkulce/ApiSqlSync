using System.Data;
using ApiSqlSync.Core.Models;
using ApiSqlSync.SqlServer.Configuration;
using ApiSqlSync.SqlServer.Initialization;
using ApiSqlSync.SqlServer.Persistence;
using Microsoft.Data.SqlClient;
using Xunit;

namespace ApiSqlSync.IntegrationTests;

public class SqlServerDatabaseInitializerTests
{
    [Fact]
    public async Task InitializeAsync_ShouldCreateDatabaseAndPreserveDataOnRepeat()
    {
        var connectionString =
            Environment.GetEnvironmentVariable("APISQLSYNC_TEST_CONNECTION");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Set APISQLSYNC_TEST_CONNECTION and restart Visual Studio.");
        }

        // Sunucu ve kimlik doğrulama aynı kalır; veritabanı bu teste özeldir.
        var databaseName =
            "ApiSqlSync_InitTest_" + Guid.NewGuid().ToString("N");

        var targetSettings = new SqlConnectionStringBuilder(connectionString)
        {
            InitialCatalog = databaseName,
            Pooling = false
        };

        var masterSettings = new SqlConnectionStringBuilder(connectionString)
        {
            InitialCatalog = "master",
            Pooling = false
        };

        var options = new SqlServerOptions
        {
            ConnectionString = targetSettings.ConnectionString,
            CommandTimeoutSeconds = 60
        };

        var initializer = new SqlServerDatabaseInitializer(options);

        try
        {
            var firstResult = await initializer.InitializeAsync();

            Assert.Equal(
                new[]
                {
                    "001_InitialSchema.sql",
                    "002_SyncRuns.sql"
                },
                firstResult.ToArray());

            // Buraya bağlanabilmemiz, yeni veritabanının oluşturulduğunu gösterir.
            await using var connection =
                new SqlConnection(targetSettings.ConnectionString);

            await connection.OpenAsync();

            Assert.Equal(4, await ReadTableCountAsync(connection));

            var firstHistory = await ReadHistoryAsync(connection);

            Assert.Equal(2, firstHistory.Count);

            Assert.Equal(
                firstResult.ToArray(),
                firstHistory.Select(row => row.Name).ToArray());

            Assert.All(
                firstHistory,
                row => Assert.Equal(64, row.Hash.Length));

            var store = new SqlServerSyncStore(options);
            const string jobId = "initializer-test";

            var record = new SyncRecord(
                new SyncCursor(1000, 41),
                """{"id":41,"name":"Kurulumdan sonra korunacak kayıt"}""");

            await store.CommitPageAsync(
                jobId,
                new[] { record },
                record.Cursor);

            // İkinci kurulumda uygulanmış scriptler yeniden çalıştırılmamalı.
            var secondResult = await initializer.InitializeAsync();

            Assert.Empty(secondResult);

            var secondHistory = await ReadHistoryAsync(connection);

            // Ad, hash ve uygulanma zamanı aynı kalmalı.
            Assert.Equal(
                firstHistory.ToArray(),
                secondHistory.ToArray());

            Assert.Equal(4, await ReadTableCountAsync(connection));

            // Kurulum tekrarının hem veriyi hem ilerleme bilgisini koruduğunu doğrula.
            Assert.Equal(
                record.Cursor,
                await store.LoadCheckpointAsync(jobId));

            await using var command = connection.CreateCommand();

            command.CommandText = """
                SELECT Id, UpdatedAt, Payload
                FROM dbo.SyncRecords
                WHERE JobId = @jobId;
                """;

            command.Parameters.Add(
                "@jobId",
                SqlDbType.NVarChar,
                128).Value = jobId;

            await using var reader = await command.ExecuteReaderAsync();

            Assert.True(await reader.ReadAsync());
            Assert.Equal(record.Cursor.Id, reader.GetInt64(0));
            Assert.Equal(record.Cursor.UpdatedAt, reader.GetInt64(1));
            Assert.Equal(record.Payload, reader.GetString(2));

            // Tekrar kurulum fazladan veri üretmemeli.
            Assert.False(await reader.ReadAsync());
        }
        finally
        {
            // Yalnızca bu test için üretilen veritabanını kaldırıyoruz.
            await DropTestDatabaseAsync(
                masterSettings.ConnectionString,
                databaseName);
        }
    }

    private static async Task<int> ReadTableCountAsync(
        SqlConnection connection)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = """
            SELECT COUNT(*)
            FROM sys.tables
            WHERE schema_id = SCHEMA_ID(N'dbo')
              AND name IN
              (
                  N'SyncRecords',
                  N'SyncCheckpoints',
                  N'SyncRuns',
                  N'SchemaMigrations'
              );
            """;

        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static async Task<IReadOnlyList<MigrationRow>> ReadHistoryAsync(
        SqlConnection connection)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = """
            SELECT ScriptName, ScriptHash, AppliedAtUtc
            FROM dbo.SchemaMigrations
            ORDER BY ScriptName;
            """;

        await using var reader = await command.ExecuteReaderAsync();

        var rows = new List<MigrationRow>();

        while (await reader.ReadAsync())
        {
            rows.Add(new MigrationRow(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetDateTime(2)));
        }

        return rows;
    }

    private static async Task DropTestDatabaseAsync(
        string masterConnectionString,
        string databaseName)
    {
        await using var connection =
            new SqlConnection(masterConnectionString);

        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandTimeout = 60;

        var quotedName = "[" + databaseName.Replace("]", "]]") + "]";

        command.CommandText = $"""
            IF DB_ID(@databaseName) IS NOT NULL
            BEGIN
                ALTER DATABASE {quotedName}
                    SET SINGLE_USER WITH ROLLBACK IMMEDIATE;

                DROP DATABASE {quotedName};
            END;
            """;

        command.Parameters.Add(
            "@databaseName",
            SqlDbType.NVarChar,
            128).Value = databaseName;

        await command.ExecuteNonQueryAsync();
    }

    private sealed record MigrationRow(
        string Name,
        string Hash,
        DateTime AppliedAtUtc);
}