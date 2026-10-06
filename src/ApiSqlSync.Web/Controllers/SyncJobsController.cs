using ApiSqlSync.Core.Abstractions;
using Microsoft.AspNetCore.Mvc;

namespace ApiSqlSync.Web.Controllers;

/// <summary>
/// SQL'de checkpoint'i bulunan senkronizasyon işlerini listeler.
/// </summary>
public sealed class SyncJobsController : Controller
{
    private readonly ISyncJobReader _jobReader;

    public SyncJobsController(ISyncJobReader jobReader)
    {
        _jobReader = jobReader;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        // Tarayıcı isteği iptal ederse okuma işlemine de bildirilir.
        var jobs = await _jobReader.ReadJobsAsync(cancellationToken);

        return View(jobs);
    }
}