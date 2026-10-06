using ApiSqlSync.Core.Models;
using Xunit;

namespace ApiSqlSync.Core.Tests;

public class SyncCursorTests
{
    [Theory]
    [InlineData(1000L, 41L, 1000L, 42L, -1)]
    [InlineData(1000L, 43L, 1000L, 42L, 1)]
    [InlineData(1000L, 42L, 1000L, 42L, 0)]
    [InlineData(1001L, 10L, 1000L, 43L, 1)]
    [InlineData(999L, 999L, 1000L, 1L, -1)]
    public void CompareTo_ShouldOrderByTimestampThenId(
        long updatedAt,
        long id,
        long otherUpdatedAt,
        long otherId,
        int expectedSign)
    {
        // Arrange: karşılaştırılacak konumları hazırla.
        var cursor = new SyncCursor(updatedAt, id);
        var other = new SyncCursor(otherUpdatedAt, otherId);

        // Act: karşılaştırmayı çalıştır.
        int result = cursor.CompareTo(other);

        // Assert: sonucun yönünü kontrol et.
        Assert.Equal(expectedSign, Math.Sign(result));
    }
}