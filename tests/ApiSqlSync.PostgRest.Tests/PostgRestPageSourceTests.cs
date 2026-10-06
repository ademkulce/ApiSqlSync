using System.Net;
using System.Text;
using System.Text.Json;
using ApiSqlSync.Core.Models;
using ApiSqlSync.PostgRest.Configuration;
using ApiSqlSync.PostgRest.Sources;
using Xunit;

namespace ApiSqlSync.PostgRest.Tests;

public class PostgRestPageSourceTests
{
    [Fact]
    public async Task ReadPageAsync_ShouldConvertJsonToSyncRecords()
    {
        const string json = """
            [
              {"id":41,"updated_at":1000,"name":"Örnek müşteri"},
              {"id":42,"updated_at":1000,"is_active":true}
            ]
            """;

        using var client = CreateClient(HttpStatusCode.OK, json);
        var source = CreateSource(client);

        var records = await source.ReadPageAsync(null, 100);

        Assert.Equal(2, records.Count);
        Assert.Equal(new SyncCursor(1000, 41), records[0].Cursor);
        Assert.Equal(new SyncCursor(1000, 42), records[1].Cursor);

        // Cursor dışındaki alanlar da payload içinde korunmalı.
        using var payload = JsonDocument.Parse(records[0].Payload);

        Assert.Equal("Örnek müşteri", payload.RootElement.GetProperty("name").GetString());
    }

    [Fact]
    public async Task ReadPageAsync_ShouldReturnEmptyListForEmptyArray()
    {
        using var client = CreateClient(HttpStatusCode.OK, "[]");
        var source = CreateSource(client);

        var records = await source.ReadPageAsync(null, 100);

        Assert.Empty(records);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task ReadPageAsync_ShouldThrowForFailedHttpResponse(HttpStatusCode statusCode)
    {
        using var client = CreateClient(statusCode, "{}");
        var source = CreateSource(client);

        // Başarısız HTTP yanıtı, boş sayfa olarak yorumlanmamalı.
        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => source.ReadPageAsync(null, 100));

        Assert.Equal(statusCode, exception.StatusCode);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("[null]")]
    [InlineData("""[{"id":41}]""")]
    [InlineData("""[{"id":41,"updated_at":null}]""")]
    [InlineData("""[{"id":"41","updated_at":1000}]""")]
    [InlineData("""[{"id":41,"updated_at":9223372036854775808}]""")]
    public async Task ReadPageAsync_ShouldRejectInvalidRecordShape(
        string json)
    {
        using var client = CreateClient(HttpStatusCode.OK, json);
        var source = CreateSource(client);

        await Assert.ThrowsAsync<InvalidDataException>(() => source.ReadPageAsync(null, 100));
    }

    [Fact]
    public async Task ReadPageAsync_ShouldThrowForMalformedJson()
    {
        using var client = CreateClient(HttpStatusCode.OK, "[invalid");
        var source = CreateSource(client);

        await Assert.ThrowsAnyAsync<JsonException>(() => source.ReadPageAsync(null, 100));
    }

    private static PostgRestPageSource CreateSource(HttpClient client)
    {
        var options = new PostgRestOptions
        {
            BaseUrl = new Uri("https://api.example.com/"),
            Resource = "customers"
        };

        return new PostgRestPageSource(client, options);
    }

    private static HttpClient CreateClient(HttpStatusCode statusCode, string body)
    {
        return new HttpClient(new StubHttpMessageHandler(statusCode, body));
    }

    /// <summary>
    /// Ağ bağlantısı kurmadan, testin belirlediği HTTP yanıtını döndürür.
    /// </summary>
    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly string _body;

        public StubHttpMessageHandler(HttpStatusCode statusCode, string body)
        {
            _statusCode = statusCode;
            _body = body;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Her istekte yeni yanıt oluşturulur; çağıran yanıtı dispose eder.
            var response = new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/json")
            };

            return Task.FromResult(response);
        }
    }
}