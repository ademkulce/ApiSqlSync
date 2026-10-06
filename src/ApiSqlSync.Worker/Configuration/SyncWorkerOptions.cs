namespace ApiSqlSync.Worker.Configuration;

/// <summary>
/// Worker'ın hangi işi, kaç kayıtlık sayfalarla ve hangi aralıkla
/// çalıştıracağını belirler.
/// </summary>
public sealed class SyncWorkerOptions
{
    public required string JobId { get; init; }

    public int PageSize { get; init; } = 500;

    // Bir tur tamamlandıktan sonra beklenecek süre.
    public int IntervalSeconds { get; init; } = 30;

    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(JobId);

        if (JobId.Length > 128)
        {
            throw new ArgumentException("JobId cannot exceed 128 characters.", nameof(JobId));
        }

        if (PageSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(PageSize), "Page size must be positive.");
        }

        // Bu prototipte bekleme aralığını bir saniye ile bir gün arasında tutuyoruz.
        if (IntervalSeconds is < 1 or > 86400)
        {
            throw new ArgumentOutOfRangeException(nameof(IntervalSeconds), "Interval must be between 1 and 86400 seconds.");
        }
    }
}