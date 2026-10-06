using ApiSqlSync.Core.Models;
using ApiSqlSync.PostgRest.Configuration;
using ApiSqlSync.PostgRest.Http;
using Xunit;

namespace ApiSqlSync.PostgRest.Tests;

public class PostgRestQueryBuilderTests
{
    [Fact]
    public void Build_ShouldCreateFirstPageWithoutCursorFilter()
    {
        var builder = CreateBuilder();

        Uri result = builder.Build(after: null, pageSize: 500);

        Assert.Equal("/rest/v1/customers", result.AbsolutePath);

        // Encoding ayrıntısından bağımsız olarak sorgunun anlamını kontrol ediyoruz.
        Assert.Equal("?order=updated_at.asc,id.asc&limit=500", Uri.UnescapeDataString(result.Query));
    }

    [Fact]
    public void Build_ShouldIncludeTimestampAndIdInCursorFilter()
    {
        var builder = CreateBuilder();

        Uri result = builder.Build(new SyncCursor(1000, 42), pageSize: 500);

        string expectedQuery =
            "?order=updated_at.asc,id.asc&limit=500" +
            "&or=(updated_at.gt.1000," +
            "and(updated_at.eq.1000,id.gt.42))";

        Assert.Equal(expectedQuery, Uri.UnescapeDataString(result.Query));
    }

    [Theory]
    [InlineData("https://api.example.com/rest/v1")]
    [InlineData("https://api.example.com/rest/v1/")]
    public void Build_ShouldPreserveBasePath(string baseUrl)
    {
        var builder = CreateBuilder(baseUrl);

        Uri result = builder.Build(after: null, pageSize: 100);

        // Sondaki slash eksik olsa da v1 yolu kaybolmamalı.
        Assert.Equal("https://api.example.com/rest/v1/customers", result.GetLeftPart(UriPartial.Path));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Build_ShouldRejectNonPositivePageSize(int pageSize)
    {
        var builder = CreateBuilder();

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => builder.Build(after: null, pageSize: pageSize));

        Assert.Equal("pageSize", exception.ParamName);
    }

    private static PostgRestQueryBuilder CreateBuilder(
        string baseUrl = "https://api.example.com/rest/v1/")
    {
        var options = new PostgRestOptions
        {
            BaseUrl = new Uri(baseUrl),
            Resource = "customers"
        };

        return new PostgRestQueryBuilder(options);
    }
}