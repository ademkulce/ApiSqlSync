using System.Diagnostics;
using ApiSqlSync.Core.Synchronization;
using ApiSqlSync.Worker.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ApiSqlSync.Worker;

/// <summary>
/// Senkronizasyonu periyodik çalıştırır.
/// Bir tur bitmeden sonraki turu başlatmaz.
/// </summary>
public sealed class Worker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly SyncWorkerOptions _options;
    private readonly ILogger<Worker> _logger;

    public Worker(
        IServiceScopeFactory scopeFactory,
        SyncWorkerOptions options,
        ILogger<Worker> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(        CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(_options.IntervalSeconds);

        _logger.LogInformation(
            "Worker başladı. İş: {JobId}, tur sonrası bekleme: {IntervalSeconds} saniye.",
            _options.JobId,
            _options.IntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            var stopwatch = Stopwatch.StartNew();

            try
            {
                // Worker uzun ömürlüdür; engine ve API istemcisini
                // her tur için açılan ayrı bir scope içinde kullanıyoruz.
                await using var scope = _scopeFactory.CreateAsyncScope();

                var runner =
    scope.ServiceProvider.GetRequiredService<SyncRunner>();

                _logger.LogInformation(
                    "Senkronizasyon turu başladı. İş: {JobId}",
                    _options.JobId);

                var processedCount = await runner.RunAsync(
    _options.JobId,
    "Worker",
    _options.PageSize,
    stoppingToken);

                _logger.LogInformation(
                    "Tur tamamlandı. İş: {JobId}, işlenen kayıt: {ProcessedCount}, süre: {ElapsedMilliseconds} ms.",
                    _options.JobId,
                    processedCount,
                    stopwatch.ElapsedMilliseconds);
            }
            catch (OperationCanceledException)
                when (stoppingToken.IsCancellationRequested)
            {
                // Kapanma isteği geldiğinde yeni tur başlatmıyoruz.
                break;
            }
            catch (Exception exception)
            {
                // Başarılı sayfaların checkpoint'i SQL'de duruyor.
                // Sonraki tur ilerlemeyi oradan yeniden okuyacak.
                _logger.LogError(
                    exception,
                    "Senkronizasyon turu başarısız. İş: {JobId}. {IntervalSeconds} saniye sonra yeniden denenecek.",
                    _options.JobId,
                    _options.IntervalSeconds);
            }

            try
            {
                _logger.LogInformation("Sonraki tur için {IntervalSeconds} saniye bekleniyor.", _options.IntervalSeconds);
                // Bekleme tur tamamlandıktan sonra başlar.
                // İşlem uzun sürse de ikinci bir tur paralel başlamaz.
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException)
                when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        _logger.LogInformation(
            "Worker durduruldu. İş: {JobId}",
            _options.JobId);
    }
}