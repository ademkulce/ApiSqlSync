using System.Net.Http.Headers;
using System.Text.Json;
using ApiSqlSync.Core.Abstractions;
using ApiSqlSync.Core.Models;
using ApiSqlSync.PostgRest.Configuration;
using ApiSqlSync.PostgRest.Http;

namespace ApiSqlSync.PostgRest.Sources;

/// <summary>
/// PostgREST yanıtını okuyarak senkronizasyon kayıtlarına dönüştürür.
/// </summary>
public sealed class PostgRestPageSource : IPageSource
{
    private readonly HttpClient _httpClient;
    private readonly PostgRestQueryBuilder _queryBuilder;
    private readonly string _timestampField;
    private readonly string _idField;

    public PostgRestPageSource(HttpClient httpClient, PostgRestOptions options)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);

        _httpClient = httpClient;
        _queryBuilder = new PostgRestQueryBuilder(options);
        _timestampField = options.TimestampField;
        _idField = options.IdField;
    }

    /// <summary>
    /// Verilen cursor'dan sonraki sayfayı okur.
    /// HTTP ve JSON hatalarını aktarımın tamamlandığı şeklinde yorumlamaz.
    /// </summary>
    public async Task<IReadOnlyList<SyncRecord>> ReadPageAsync(SyncCursor? after, int pageSize, CancellationToken cancellationToken = default)
    {
        Uri requestUri = _queryBuilder.Build(after, pageSize);

        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);

        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await _httpClient.SendAsync(request, cancellationToken);

        // Başarısız yanıtı boş sayfaya çevirmiyoruz; aktarım hata ile durmalı.
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);

        using var document = await JsonDocument.ParseAsync(stream,
            cancellationToken: cancellationToken);

        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("PostgREST response must be a JSON array.");
        }

        var records = new List<SyncRecord>();

        foreach (var item in document.RootElement.EnumerateArray())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (item.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException("Each source record must be a JSON object.");
            }

            long updatedAt = ReadInt64(item, _timestampField);
            long id = ReadInt64(item, _idField);

            // JSON'u string olarak alıyoruz; document kapandıktan sonra da kullanılabilir.
            records.Add(new SyncRecord(new SyncCursor(updatedAt, id), item.GetRawText()));
        }

        return records;
    }

    /// <summary>
    /// Cursor alanının mevcut ve 64 bit tam sayı olarak okunabilir olduğunu doğrular.
    /// </summary>
    private static long ReadInt64(JsonElement item, string fieldName)
    {
        if (!item.TryGetProperty(fieldName, out var value) ||
            value.ValueKind != JsonValueKind.Number ||
            !value.TryGetInt64(out long result))
        {
            throw new InvalidDataException($"Field '{fieldName}' must contain a 64-bit integer.");
        }

        return result;
    }
}