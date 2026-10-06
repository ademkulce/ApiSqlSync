using System.Text.RegularExpressions;

namespace ApiSqlSync.PostgRest.Configuration;

/// <summary>
/// PostgREST kaynağının adresini ve cursor için kullanılacak alanları tanımlar.
/// </summary>
public sealed class PostgRestOptions
{
    public required Uri BaseUrl { get; init; }

    public required string Resource { get; init; }

    public string TimestampField { get; init; } = "updated_at";

    public string IdField { get; init; } = "id";

    /// <summary>
    /// HTTP isteği oluşturulmadan önce kaynak ayarlarını doğrular.
    /// </summary>
    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(BaseUrl);

        if (!BaseUrl.IsAbsoluteUri)
        {
            throw new ArgumentException("BaseUrl must be an absolute URL.");
        }

        // Token gönderileceği için uzak bağlantılarda HTTPS kullanıyoruz.
        // Yerel geliştirmede localhost üzerinden HTTP kullanılabilir.
        bool isHttps = BaseUrl.Scheme == Uri.UriSchemeHttps;
        bool isLocalHttp = BaseUrl.Scheme == Uri.UriSchemeHttp && BaseUrl.IsLoopback;

        if (!isHttps && !isLocalHttp)
        {
            throw new ArgumentException("BaseUrl must use HTTPS, or HTTP on loopback.");
        }

        // Adres yalnızca API'nin temel yolunu içermeli.
        if (!string.IsNullOrEmpty(BaseUrl.Query) ||
            !string.IsNullOrEmpty(BaseUrl.Fragment) ||
            !string.IsNullOrEmpty(BaseUrl.UserInfo))
        {
            throw new ArgumentException("BaseUrl cannot contain query parameters, fragments or credentials.");
        }

        ValidateIdentifier(Resource, nameof(Resource));
        ValidateIdentifier(TimestampField, nameof(TimestampField));
        ValidateIdentifier(IdField, nameof(IdField));
    }

    private static void ValidateIdentifier(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);

        // İlk sürümde basit tablo/view ve kolon adlarını destekliyoruz.
        // Nokta, virgül ve parantez gibi sorgu karakterlerini kabul etmiyoruz.
        if (!Regex.IsMatch(value, @"^[A-Za-z_][A-Za-z0-9_]*$"))
        {
            throw new ArgumentException("Use letters, digits and underscores; the first character cannot be a digit.", parameterName);
        }
    }
}