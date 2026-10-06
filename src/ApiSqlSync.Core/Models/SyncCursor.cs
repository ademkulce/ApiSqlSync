namespace ApiSqlSync.Core.Models;

/// <summary>
/// Kaydın sıralamadaki konumunu güncelleme zamanı ve ID ile temsil eder.
/// </summary>
public readonly record struct SyncCursor(long UpdatedAt, long Id) : IComparable<SyncCursor>
{
    /// <summary>
    /// Önce güncelleme zamanını, zamanlar eşitse ID'yi karşılaştırır.
    /// </summary>
    public int CompareTo(SyncCursor other)
    {
        int timestampComparison = UpdatedAt.CompareTo(other.UpdatedAt);

        if (timestampComparison != 0)
        {
            return timestampComparison;
        }

        return Id.CompareTo(other.Id);
    }
}
