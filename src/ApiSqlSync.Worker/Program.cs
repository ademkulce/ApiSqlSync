using System.Net.Http.Headers;
using ApiSqlSync.Core.Abstractions;
using ApiSqlSync.Core.Synchronization;
using ApiSqlSync.PostgRest.Configuration;
using ApiSqlSync.PostgRest.Sources;
using ApiSqlSync.SqlServer.Configuration;
using ApiSqlSync.SqlServer.Persistence;
using ApiSqlSync.Worker;
using ApiSqlSync.Worker.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(
    new HostApplicationBuilderSettings
    {
        Args = args,

        // Ayar dosyalarını çalıştırma komutunun verildiği klasörden
        // değil, uygulamanın bulunduğu klasörden okuyoruz.
        ContentRootPath = AppContext.BaseDirectory
    });

// Host ortak appsettings dosyalarını zaten okur.
// Kişisel dosya ve uygulamaya ait ortam değişkenleri bunların üzerine gelir.
builder.Configuration
    .AddJsonFile(
        "appsettings.Local.json",
        optional: true,
        reloadOnChange: false)
    .AddEnvironmentVariables(prefix: "APISQLSYNC_");

var apiUrl = ReadRequiredSetting(
    builder.Configuration,
    "PostgRest:BaseUrl");

if (!Uri.TryCreate(apiUrl, UriKind.Absolute, out var baseUrl))
{
    throw new InvalidOperationException(
        "PostgRest:BaseUrl must be an absolute URL.");
}

var sourceOptions = new PostgRestOptions
{
    BaseUrl = baseUrl,
    Resource = ReadRequiredSetting(
        builder.Configuration,
        "PostgRest:Resource"),
    TimestampField = ReadRequiredSetting(
        builder.Configuration,
        "PostgRest:TimestampField"),
    IdField = ReadRequiredSetting(
        builder.Configuration,
        "PostgRest:IdField")
};

var storeOptions = new SqlServerOptions
{
    ConnectionString = ReadRequiredSetting(
        builder.Configuration,
        "SqlServer:ConnectionString"),
    CommandTimeoutSeconds = ReadPositiveInteger(
        builder.Configuration,
        "SqlServer:CommandTimeoutSeconds",
        defaultValue: 30)
};

var workerOptions = new SyncWorkerOptions
{
    JobId = ReadRequiredSetting(
        builder.Configuration,
        "Sync:JobId"),
    PageSize = ReadPositiveInteger(
        builder.Configuration,
        "Sync:PageSize",
        defaultValue: 500),
    IntervalSeconds = ReadPositiveInteger(
        builder.Configuration,
        "Sync:IntervalSeconds",
        defaultValue: 30)
};

// Eksik veya geçersiz ayarları ilk API isteğinden önce yakalıyoruz.
sourceOptions.Validate();
storeOptions.Validate();
workerOptions.Validate();

builder.Services.AddSingleton(sourceOptions);
builder.Services.AddSingleton(storeOptions);
builder.Services.AddSingleton(workerOptions);

var token = builder.Configuration["PostgRest:Token"];

// Her tur yeni bir istemci alır; bağlantı havuzunu factory yönetir.
builder.Services.AddHttpClient<IPageSource, PostgRestPageSource>(
    httpClient =>
    {
        if (!string.IsNullOrWhiteSpace(token))
        {
            httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", token.Trim());
        }
    });

// Store, bağlantıyı her işlemde açar; içinde ortak bir açık bağlantı tutmaz.
builder.Services.AddSingleton<ISyncStore, SqlServerSyncStore>();

// Her senkronizasyon turunda ayrı bir engine oluşturacağız.
builder.Services.AddScoped<SyncEngine>();

builder.Services.AddSingleton<ISyncRunStore, SqlServerSyncRunStore>();
builder.Services.AddScoped<SyncRunner>();

builder.Services.AddHostedService<Worker>();

using var host = builder.Build();
await host.RunAsync();

static string ReadRequiredSetting(
    IConfiguration configuration,
    string key)
{
    var value = configuration[key];

    if (string.IsNullOrWhiteSpace(value))
    {
        throw new InvalidOperationException(
            $"Required setting is missing: {key}. " +
            "Check appsettings.Local.json or configuration overrides.");
    }

    return value.Trim();
}

static int ReadPositiveInteger(
    IConfiguration configuration,
    string key,
    int defaultValue)
{
    var value = configuration[key];

    if (value is null)
    {
        return defaultValue;
    }

    if (!int.TryParse(value, out var number) || number <= 0)
    {
        throw new InvalidOperationException(
            $"Setting must be a positive integer: {key}");
    }

    return number;
}