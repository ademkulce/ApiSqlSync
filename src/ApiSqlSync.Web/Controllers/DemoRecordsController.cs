using System.Globalization;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using ApiSqlSync.Core.Models;
using Microsoft.AspNetCore.Mvc;

namespace ApiSqlSync.Web.Controllers;

/// <summary>
/// Yerel denemelerde kullanılacak örnek kaynak.
/// Aynı zaman değerine sahip kayıtlar, cursor'ın Id kısmını da sınar.
/// </summary>
[ApiController]
[Route("demo-api/records")]
public sealed class DemoRecordsController : ControllerBase
{
    private readonly IWebHostEnvironment _environment;

    private static readonly DemoRecord[] Records =
 [
     new(1, 1000, "Birinci kayıt"),
    new(2, 4000, "İkinci kayıt güncellendi"),
    new(3, 1000, "Üçüncü kayıt"),
    new(4, 2000, "Dördüncü kayıt"),
    new(5, 2000, "Beşinci kayıt"),
    new(6, 3000, "Altıncı kayıt"),
    new(7, 3000, "Yedinci kayıt"),
    new(8, 4000, "Sekizinci kayıt"),
    new(9, 5000, "Worker tarafından alınacak kayıt")
 ];

    public DemoRecordsController(IWebHostEnvironment environment)
    {
        _environment = environment;
    }

    /// <summary>
    /// Cursor'dan sonraki kayıtları sıralayıp istenen sayfa boyutunda döndürür.
    /// </summary>
    [HttpGet]
    public IActionResult Get([FromQuery] int limit = 500, [FromQuery] string? order = null, [FromQuery(Name = "or")] string? cursorFilter = null)
    {
        // Demo verilerini yalnızca geliştirme ortamında sunuyoruz.
        if (!_environment.IsDevelopment())
        {
            return NotFound();
        }

        if (limit is < 1 or > 1000)
        {
            return BadRequest("Limit must be between 1 and 1000.");
        }

        if (order is not null && order != "updated_at.asc,id.asc")
        {
            return BadRequest("Supported order: updated_at.asc,id.asc");
        }

        SyncCursor? after = null;

        if (cursorFilter is not null)
        {
            if (!TryReadCursor(cursorFilter, out var cursor))
            {
                return BadRequest("Invalid cursor filter.");
            }

            after = cursor;
        }

        var page = Records
            .Where(record =>
                !after.HasValue ||
                new SyncCursor(record.UpdatedAt, record.Id)
                    .CompareTo(after.Value) > 0)
            .OrderBy(record => record.UpdatedAt)
            .ThenBy(record => record.Id)
            .Take(limit)
            .ToArray();

        return Ok(page);
    }

    private static bool TryReadCursor(string filter, out SyncCursor cursor)
    {
        cursor = default;

        if (filter.Length > 200)
        {
            return false;
        }

        // İstemcinin ürettiği filtre:
        // (updated_at.gt.1000,and(updated_at.eq.1000,id.gt.3))
        const string pattern =
            @"^\(updated_at\.gt\.(-?[0-9]+)," +
            @"and\(updated_at\.eq\.(-?[0-9]+),id\.gt\.(-?[0-9]+)\)\)$";

        var match = Regex.Match(filter, pattern, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

        if (!match.Success)
        {
            return false;
        }

        if (!long.TryParse(
                match.Groups[1].Value,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var updatedAt) ||
            !long.TryParse(
                match.Groups[2].Value,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var equalUpdatedAt) ||
            !long.TryParse(
                match.Groups[3].Value,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var id))
        {
            return false;
        }

        // Filtrenin iki kolu aynı zaman sınırını kullanmalı.
        if (updatedAt != equalUpdatedAt)
        {
            return false;
        }

        cursor = new SyncCursor(updatedAt, id);
        return true;
    }

    public sealed record DemoRecord(
        [property: JsonPropertyName("id")] long Id,
        [property: JsonPropertyName("updated_at")] long UpdatedAt,
        [property: JsonPropertyName("name")] string Name);
}