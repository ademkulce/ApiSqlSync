namespace ApiSqlSync.Core.Models
{
    /// <summary>
    /// Aktarılacak kaydın konumunu ve JSON içeriğini taşır.
    /// Kaynağa özel kolonlar payload içinde korunur.
    /// </summary>
    public sealed record SyncRecord(SyncCursor Cursor, string Payload);
}
