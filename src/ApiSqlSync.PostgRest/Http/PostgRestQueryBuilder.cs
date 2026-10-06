using System.Globalization;
using ApiSqlSync.Core.Models;
using ApiSqlSync.PostgRest.Configuration;

namespace ApiSqlSync.PostgRest.Http;

/// <summary>
/// Sayfa okuma isteğinin adresini kaynak ayarları ve cursor üzerinden oluşturur.
/// </summary>
public sealed class PostgRestQueryBuilder
{
    private readonly Uri _endpoint;
    private readonly string _timestampField;
    private readonly string _idField;

    public PostgRestQueryBuilder(PostgRestOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        // Sondaki slash eksikse Uri, son yol parçasını resource ile değiştirebilir.
        var baseUrl = new Uri(options.BaseUrl.AbsoluteUri.TrimEnd('/') + "/");

        _endpoint = new Uri(baseUrl, options.Resource);
        _timestampField = options.TimestampField;
        _idField = options.IdField;
    }

    /// <summary>
    /// İlk sayfa için sıralama ve limit, sonraki sayfalar için cursor filtresi ekler.
    /// </summary>
    public Uri Build(SyncCursor? after, int pageSize)
    {
        if (pageSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pageSize));
        }

        var parameters = new List<string>
        {
            CreateParameter("order",$"{_timestampField}.asc,{_idField}.asc"),

            CreateParameter("limit",pageSize.ToString(CultureInfo.InvariantCulture))
        };

        if (after.HasValue)
        {
            // Sayıların biçimi uygulamanın çalıştığı ülke ayarından etkilenmemeli.
            string timestamp = after.Value.UpdatedAt.ToString(CultureInfo.InvariantCulture);

            string id = after.Value.Id.ToString(CultureInfo.InvariantCulture);

            // Zamanı büyük olanlar veya aynı zamanda daha büyük ID taşıyanlar.
            string filter =
                $"({_timestampField}.gt.{timestamp}," +
                $"and({_timestampField}.eq.{timestamp}," +
                $"{_idField}.gt.{id}))";

            parameters.Add(CreateParameter("or", filter));
        }

        var builder = new UriBuilder(_endpoint)
        {
            Query = string.Join("&", parameters)
        };

        return builder.Uri;
    }

    private static string CreateParameter(string name, string value)
    {
        // Query değerlerini ayrı ayrı kodluyoruz; URL'nin tamamını kodlamıyoruz.
        return $"{name}={Uri.EscapeDataString(value)}";
    }
}