using ApiSqlSync.Core.Models;

namespace ApiSqlSync.Core.Abstractions;

/// <summary>
/// Kaynak veriyi cursor üzerinden sayfalar halinde okumak için kullanılan sözleşme.
/// </summary>
public interface IPageSource
{
    /// <summary>
    /// Verilen konumdan sonraki kayıtları cursor'a göre kesin artan sırada getirir.
    /// Başlangıç konumu null ise ilk sayfa okunur.
    /// </summary>
    /// <remarks>
    /// pageSize pozitif olmalı ve sonuç bu sınırı aşmamalıdır.
    /// Boş liste, sorgu anında sonraki kayıt bulunmadığını belirtir.
    /// Okuma hataları boş listeye çevrilmeden çağırana iletilmelidir.
    /// </remarks>
    Task<IReadOnlyList<SyncRecord>> ReadPageAsync(SyncCursor? after, int pageSize, CancellationToken cancellationToken = default);
}