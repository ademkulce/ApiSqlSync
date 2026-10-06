using System.Data;
using System.Security.Cryptography;
using System.Text;
using ApiSqlSync.SqlServer.Configuration;
using Microsoft.Data.SqlClient;

namespace ApiSqlSync.SqlServer.Initialization;

/// <summary>
/// Veritabanını hazırlar ve uygulanan şema scriptlerinin geçmişini tutar.
/// </summary>
public sealed class SqlServerDatabaseInitializer
{
    private static readonly string[] ScriptNames =
    [
        "001_InitialSchema.sql",
        "002_SyncRuns.sql"
    ];

    private readonly string _connectionString;
    private readonly int _commandTimeoutSeconds;

    public SqlServerDatabaseInitializer(SqlServerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        _connectionString = options.ConnectionString;
        _commandTimeoutSeconds = options.CommandTimeoutSeconds;
    }

    /// <summary>
    /// Eksik veritabanını oluşturur ve henüz uygulanmamış scriptleri çalıştırır.
    /// Dönen liste, bu çağrıda uygulanan scriptlerin adlarını içerir.
    /// </summary>
    public async Task<IReadOnlyList<string>> InitializeAsync(
        CancellationToken cancellationToken = default)
    {
        // Paket eksik hazırlanmışsa veritabanına dokunmadan hata verelim.
        var scripts = await LoadScriptsAsync(cancellationToken);

        await EnsureDatabaseExistsAsync(cancellationToken);

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var transaction =
            (SqlTransaction)await connection.BeginTransactionAsync(
                cancellationToken);

        await PrepareHistoryAsync(
            connection,
            transaction,
            cancellationToken);

        var appliedScripts = new List<string>();

        foreach (var script in scripts)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var savedHash = await ReadSavedHashAsync(
                connection,
                transaction,
                script.Name,
                cancellationToken);

            if (savedHash is not null)
            {
                // Uygulanmış bir script değiştirilmez; değişiklik yeni dosyaya yazılır.
                if (!string.Equals(
                    savedHash,
                    script.Hash,
                    StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Previously applied script has changed: {script.Name}. " +
                        "Add a new migration script instead.");
                }

                continue;
            }

            await ApplyScriptAsync(
                connection,
                transaction,
                script,
                cancellationToken);

            appliedScripts.Add(script.Name);
        }

        // Şema değişiklikleri ve geçmiş kayıtları birlikte kalıcı olur.
        await transaction.CommitAsync(cancellationToken);

        return appliedScripts;
    }

    private async Task EnsureDatabaseExistsAsync(
        CancellationToken cancellationToken)
    {
        var targetSettings =
            new SqlConnectionStringBuilder(_connectionString);

        var databaseName = targetSettings.InitialCatalog;

        if (string.IsNullOrWhiteSpace(databaseName)
            || databaseName.Length > 128)
        {
            throw new InvalidOperationException(
                "Database name must contain between 1 and 128 characters.");
        }

        if (new[] { "master", "model", "msdb", "tempdb" }.Contains(
            databaseName,
            StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Application tables cannot be installed in a system database.");
        }

        if (!string.IsNullOrWhiteSpace(targetSettings.AttachDBFilename))
        {
            throw new InvalidOperationException(
                "Database initialization does not support AttachDBFilename. " +
                "Use Server and Database in the connection string.");
        }

        // Hedef henüz yoksa ona bağlanamayız; varlık kontrolünü master'da yapıyoruz.
        var masterSettings =
            new SqlConnectionStringBuilder(_connectionString)
            {
                InitialCatalog = "master"
            };

        await using var connection =
            new SqlConnection(masterSettings.ConnectionString);

        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandTimeout = _commandTimeoutSeconds;

        command.CommandText = "SELECT DB_ID(@databaseName);";
        command.Parameters.Add(
            "@databaseName",
            SqlDbType.NVarChar,
            128).Value = databaseName;

        var existingDatabaseId =
            await command.ExecuteScalarAsync(cancellationToken);

        if (existingDatabaseId is not null
            && existingDatabaseId is not DBNull)
        {
            return;
        }

        // SQL nesne adları parametre olamaz; köşeli parantezi kaçırarak yazıyoruz.
        var quotedDatabaseName =
            "[" + databaseName.Replace("]", "]]") + "]";

        command.Parameters.Clear();
        command.CommandText = $"CREATE DATABASE {quotedDatabaseName};";

        try
        {
            // CREATE DATABASE, şema transaction'ından önce çalıştırılır.
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (SqlException exception) when (exception.Number == 1801)
        {
            // Kontrol ile oluşturma arasında başka bir kurulum oluşturmuş olabilir.
        }
    }

    private async Task PrepareHistoryAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = _commandTimeoutSeconds;

        command.CommandText = """
            SET XACT_ABORT ON;

            DECLARE @lockResult int;

            EXEC @lockResult = sys.sp_getapplock
                @Resource = N'ApiSqlSync:schema-init',
                @LockMode = 'Exclusive',
                @LockOwner = 'Transaction',
                @LockTimeout = 10000;

            IF @lockResult < 0
                THROW 50003, 'Could not acquire schema initialization lock.', 1;

            IF OBJECT_ID(N'dbo.SchemaMigrations', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.SchemaMigrations
                (
                    ScriptName nvarchar(128) NOT NULL,
                    ScriptHash char(64) NOT NULL,

                    AppliedAtUtc datetime2(7) NOT NULL
                        CONSTRAINT DF_SchemaMigrations_AppliedAtUtc
                        DEFAULT SYSUTCDATETIME(),

                    CONSTRAINT PK_SchemaMigrations
                        PRIMARY KEY (ScriptName)
                );
            END;
            """;

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<string?> ReadSavedHashAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string scriptName,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = _commandTimeoutSeconds;

        command.CommandText = """
            SELECT ScriptHash
            FROM dbo.SchemaMigrations
            WHERE ScriptName = @scriptName;
            """;

        command.Parameters.Add(
            "@scriptName",
            SqlDbType.NVarChar,
            128).Value = scriptName;

        var result = await command.ExecuteScalarAsync(cancellationToken);

        return result is null or DBNull ? null : (string)result;
    }

    private async Task ApplyScriptAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        MigrationScript script,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = _commandTimeoutSeconds;

        command.CommandText = script.Sql;
        await command.ExecuteNonQueryAsync(cancellationToken);

        // Script başarısız olursa bu satıra ve geçmiş kaydına ulaşılmaz.
        command.CommandText = """
            INSERT INTO dbo.SchemaMigrations (ScriptName, ScriptHash)
            VALUES (@scriptName, @scriptHash);
            """;

        command.Parameters.Add(
            "@scriptName",
            SqlDbType.NVarChar,
            128).Value = script.Name;

        command.Parameters.Add(
            "@scriptHash",
            SqlDbType.Char,
            64).Value = script.Hash;

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<IReadOnlyList<MigrationScript>> LoadScriptsAsync(
        CancellationToken cancellationToken)
    {
        var assembly = typeof(SqlServerDatabaseInitializer).Assembly;
        var resourceNames = assembly.GetManifestResourceNames();
        var scripts = new List<MigrationScript>();

        foreach (var scriptName in ScriptNames)
        {
            var resourceName = resourceNames.SingleOrDefault(
                name => name.EndsWith(
                    $".Scripts.{scriptName}",
                    StringComparison.Ordinal));

            if (resourceName is null)
            {
                throw new InvalidOperationException(
                    $"Embedded SQL script was not found: {scriptName}. " +
                    "Check the EmbeddedResource configuration.");
            }

            using var stream = assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException(
                    $"Embedded SQL script could not be opened: {scriptName}.");

            using var reader = new StreamReader(stream, Encoding.UTF8);
            var sql = await reader.ReadToEndAsync(cancellationToken);

            if (string.IsNullOrWhiteSpace(sql))
            {
                throw new InvalidOperationException(
                    $"SQL script cannot be empty: {scriptName}.");
            }

            var hash = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(sql)));

            scripts.Add(new MigrationScript(scriptName, sql, hash));
        }

        return scripts;
    }

    private sealed record MigrationScript(
        string Name,
        string Sql,
        string Hash);
}