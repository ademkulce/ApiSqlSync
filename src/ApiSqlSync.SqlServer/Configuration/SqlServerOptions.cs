using Microsoft.Data.SqlClient;

namespace ApiSqlSync.SqlServer.Configuration;

/// <summary>
/// Hedef veritabanının bağlantı ve sorgu ayarlarını tanımlar.
/// </summary>
public sealed class SqlServerOptions
{
    public required string ConnectionString { get; init; }

    public int CommandTimeoutSeconds { get; init; } = 30;

    /// <summary>
    /// Bağlantı açılmadan önce ayarların biçimini doğrular.
    /// Sunucuya erişilebildiğini veya giriş bilgilerinin doğru olduğunu kontrol etmez.
    /// </summary>
    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ConnectionString);

        var builder = new SqlConnectionStringBuilder(ConnectionString);

        // Hedef veritabanını açıkça belirtmek istiyoruz.
        if (string.IsNullOrWhiteSpace(builder.InitialCatalog))
        {
            throw new ArgumentException("ConnectionString must specify a database.");
        }

        if (CommandTimeoutSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(CommandTimeoutSeconds));
        }
    }
}