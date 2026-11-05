using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;
using MongoDB.Driver;

namespace BMS.Api.Controllers.Common;

/// <summary>
/// API endpoints serving Vietnamese administrative units (Tỉnh/Thành, Quận/Huyện, Phường/Xã)
/// for registration, patient intake, and demographic data collection.
/// Connects to MongoDB 'administrative_units' collection.
/// </summary>
[ApiController]
[Route("api/administrative")]
[AllowAnonymous]
public class AdministrativeController : ControllerBase
{
    private readonly IMongoDatabase _database;

    public AdministrativeController(IMongoDatabase database)
    {
        _database = database;
    }

    /// <summary>
    /// Retrieves list of all provinces and centrally-run cities.
    /// </summary>
    [HttpGet("provinces")]
    public async Task<IActionResult> GetProvinces(CancellationToken ct = default)
    {
        var col = _database.GetCollection<BsonDocument>("administrative_units");
        var docs = await col.Find(FilterDefinition<BsonDocument>.Empty)
            .Project(Builders<BsonDocument>.Projection.Include("code").Include("name").Exclude("_id"))
            .ToListAsync(ct);

        var list = docs.Select(d => new
        {
            code = d.GetValue("code", "").AsString,
            name = d.GetValue("name", "").AsString
        }).OrderBy(p => p.name).ToList();

        return Ok(new
        {
            success = true,
            data = list
        });
    }

    /// <summary>
    /// Retrieves districts for a given province code or name.
    /// </summary>
    [HttpGet("districts")]
    public async Task<IActionResult> GetDistricts([FromQuery] string? provinceCode, [FromQuery] string? provinceName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(provinceCode) && string.IsNullOrWhiteSpace(provinceName))
        {
            return BadRequest(new { success = false, message = "Vui lòng truyền provinceCode hoặc provinceName." });
        }

        var col = _database.GetCollection<BsonDocument>("administrative_units");
        FilterDefinition<BsonDocument> filter;

        if (!string.IsNullOrWhiteSpace(provinceCode))
        {
            filter = Builders<BsonDocument>.Filter.Eq("code", provinceCode.Trim());
        }
        else
        {
            filter = Builders<BsonDocument>.Filter.Regex("name", new BsonRegularExpression(provinceName!.Trim(), "i"));
        }

        var doc = await col.Find(filter).FirstOrDefaultAsync(ct);
        if (doc == null || !doc.Contains("districts") || !doc["districts"].IsBsonArray)
        {
            return Ok(new { success = true, data = new List<object>() });
        }

        var districts = doc["districts"].AsBsonArray.Select(d => new
        {
            code = d.AsBsonDocument.GetValue("code", "").AsString,
            name = d.AsBsonDocument.GetValue("name", "").AsString
        }).OrderBy(d => d.name).ToList();

        return Ok(new
        {
            success = true,
            data = districts
        });
    }

    /// <summary>
    /// Retrieves wards for a given district code or name.
    /// </summary>
    [HttpGet("wards")]
    public async Task<IActionResult> GetWards([FromQuery] string? districtCode, [FromQuery] string? districtName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(districtCode) && string.IsNullOrWhiteSpace(districtName))
        {
            return BadRequest(new { success = false, message = "Vui lòng truyền districtCode hoặc districtName." });
        }

        var col = _database.GetCollection<BsonDocument>("administrative_units");
        var docs = await col.Find(FilterDefinition<BsonDocument>.Empty).ToListAsync(ct);

        foreach (var doc in docs)
        {
            if (doc.Contains("districts") && doc["districts"].IsBsonArray)
            {
                foreach (var distVal in doc["districts"].AsBsonArray)
                {
                    var dist = distVal.AsBsonDocument;
                    var c = dist.GetValue("code", "").AsString;
                    var n = dist.GetValue("name", "").AsString;

                    bool match = (!string.IsNullOrWhiteSpace(districtCode) && c == districtCode.Trim()) ||
                                 (!string.IsNullOrWhiteSpace(districtName) && n.Equals(districtName.Trim(), StringComparison.OrdinalIgnoreCase));

                    if (match && dist.Contains("wards") && dist["wards"].IsBsonArray)
                    {
                        var wards = dist["wards"].AsBsonArray.Select(w => new
                        {
                            code = w.AsBsonDocument.GetValue("code", "").AsString,
                            name = w.AsBsonDocument.GetValue("name", "").AsString
                        }).OrderBy(w => w.name).ToList();

                        return Ok(new
                        {
                            success = true,
                            data = wards
                        });
                    }
                }
            }
        }

        return Ok(new { success = true, data = new List<object>() });
    }
}

