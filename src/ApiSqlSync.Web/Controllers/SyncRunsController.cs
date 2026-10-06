using ApiSqlSync.Core.Abstractions;
using ApiSqlSync.Web.Models;
using Microsoft.AspNetCore.Mvc;

namespace ApiSqlSync.Web.Controllers;

/// <summary>
/// Senkronizasyon geçmişini iş kimliğine göre filtreleyerek listeler.
/// </summary>
public sealed class SyncRunsController : Controller
{
    private readonly ISyncRunReader _runReader;

    public SyncRunsController(ISyncRunReader runReader)
    {
        _runReader = runReader;
    }

    [HttpGet]
    public async Task<IActionResult> Index(string? jobId, CancellationToken cancellationToken)
    {
        jobId = string.IsNullOrWhiteSpace(jobId) ? null : jobId.Trim();

        if (jobId is { Length: > 128 })
        {
            ModelState.AddModelError(nameof(jobId), "İş kimliği en fazla 128 karakter olabilir.");

            return View(new SyncRunHistoryViewModel
            {
                JobId = jobId
            });
        }

        var runs = await _runReader.ReadRecentAsync(limit: 50, jobId: jobId, cancellationToken: cancellationToken);

        return View(new SyncRunHistoryViewModel
        {
            JobId = jobId,
            Runs = runs
        });
    }
}