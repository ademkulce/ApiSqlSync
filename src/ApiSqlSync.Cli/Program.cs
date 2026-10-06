using System.Net.Http.Headers;
using ApiSqlSync.Core.Synchronization;
using ApiSqlSync.PostgRest.Configuration;
using ApiSqlSync.PostgRest.Sources;
using ApiSqlSync.SqlServer.Configuration;
using ApiSqlSync.SqlServer.Initialization;
using ApiSqlSync.SqlServer.Persistence;
using Microsoft.Extensions.Configuration;

using var cancellation = new CancellationTokenSource();

Console.CancelKeyPress += (_, eventArgs) =>
{
    // Ctrl+C, devam eden işlemlere iptal isteği gönderir.
    eventArgs.Cancel = true;
    cancellation.Cancel();
};

try
{
    if (args.Length == 1 && args[0] == "--help")
    {
        Console.WriteLine("ApiSqlSync CLI");
        Console.WriteLine();
        Console.WriteLine("Parametresiz : Bir senkronizasyon turu çalıştırır.");
        Console.WriteLine("--init-db    : Veritabanını ve şemayı hazırlar.");
        Console.WriteLine("--help       : Kullanım bilgisini gösterir.");

        return 0;
    }

    if (args.Length > 1
        || (args.Length == 1 && args[0] != "--init-db"))
    {
        Console.Error.WriteLine(
            "Geçersiz parametre. Kullanım için --help seçeneğini kullanın.");

        return 2;
    }

    var configuration = new ConfigurationBuilder()
        .SetBasePath(AppContext.BaseDirectory)
        .AddJsonFile(
            "appsettings.json",
            optional: false,
            reloadOnChange: false)
        .AddJsonFile(
            "appsettings.Local.json",
            optional: true,
            reloadOnChange: false)
        .AddEnvironmentVariables(prefix: "APISQLSYNC_")
        .Build();

    var sqlServerOptions = new SqlServerOptions
    {
        ConnectionString = ReadRequiredSetting(
            configuration,
            "SqlServer:ConnectionString"),

        CommandTimeoutSeconds = ReadPositiveInteger(
            configuration,
            "SqlServer:CommandTimeoutSeconds",
            defaultValue: 30)
    };

    sqlServerOptions.Validate();

    // Kurulum için yalnızca SQL ayarları gerekir.
    if (args.Length == 1 && args[0] == "--init-db")
    {
        Console.WriteLine("Veritabanı kurulumu başladı.");

        var initializer =
            new SqlServerDatabaseInitializer(sqlServerOptions);

        var appliedScripts = await initializer.InitializeAsync(
            cancellation.Token);

        foreach (var scriptName in appliedScripts)
        {
            Console.WriteLine($"Uygulandı: {scriptName}");
        }

        if (appliedScripts.Count == 0)
        {
            Console.WriteLine(
                "Şema güncel. Uygulanacak yeni script bulunamadı.");
        }

        Console.WriteLine("Veritabanı hazır.");

        return 0;
    }

    var baseUrlText = ReadRequiredSetting(
        configuration,
        "PostgRest:BaseUrl");

    if (!Uri.TryCreate(baseUrlText, UriKind.Absolute, out var baseUrl))
    {
        throw new InvalidOperationException(
            "PostgRest:BaseUrl geçerli bir mutlak adres olmalıdır.");
    }

    var postgRestOptions = new PostgRestOptions
    {
        BaseUrl = baseUrl,

        Resource = ReadRequiredSetting(
            configuration,
            "PostgRest:Resource"),

        TimestampField = ReadRequiredSetting(
            configuration,
            "PostgRest:TimestampField"),

        IdField = ReadRequiredSetting(
            configuration,
            "PostgRest:IdField")
    };

    postgRestOptions.Validate();

    var jobId = ReadRequiredSetting(
        configuration,
        "Sync:JobId");

    var pageSize = ReadPositiveInteger(
        configuration,
        "Sync:PageSize",
        defaultValue: 500);

    using var httpClient = new HttpClient();

    var token = configuration["PostgRest:Token"];

    if (!string.IsNullOrWhiteSpace(token))
    {
        httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token.Trim());
    }

    var source = new PostgRestPageSource(
        httpClient,
        postgRestOptions);

    var store = new SqlServerSyncStore(sqlServerOptions);
    var engine = new SyncEngine(source, store);

    var runStore = new SqlServerSyncRunStore(sqlServerOptions);
    var runner = new SyncRunner(engine, runStore);

    Console.WriteLine($"Senkronizasyon başladı. İş: {jobId}");
    Console.WriteLine($"Kaynak: {postgRestOptions.Resource}");
    Console.WriteLine($"Sayfa boyutu: {pageSize}");

    var processedCount = await runner.RunAsync(
        jobId,
        "Cli",
        pageSize,
        cancellation.Token);

    Console.WriteLine(
        $"Senkronizasyon tamamlandı. İşlenen kayıt: {processedCount}");

    return 0;
}
catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
{
    Console.WriteLine("İşlem iptal edildi.");

    return 130;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"İşlem başarısız: {exception.Message}");

    return 1;
}

static string ReadRequiredSetting(
    IConfiguration configuration,
    string key)
{
    var value = configuration[key];

    if (string.IsNullOrWhiteSpace(value))
    {
        throw new InvalidOperationException(
            $"'{key}' ayarı eksik. " +
            "appsettings.Local.json dosyasındaki ayarları kontrol edin.");
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

    if (!int.TryParse(value, out var result) || result <= 0)
    {
        throw new InvalidOperationException(
            $"'{key}' pozitif bir tam sayı olmalıdır.");
    }

    return result;
}