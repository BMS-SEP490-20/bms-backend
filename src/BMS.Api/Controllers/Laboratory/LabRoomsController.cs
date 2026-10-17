using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;
using MongoDB.Driver;

namespace BMS.Api.Controllers.Laboratory;

/// <summary>
/// Controller managing laboratory rooms and testing workstation allocations.
/// Connects directly to MongoDB 'lab_rooms' collection.
/// </summary>
[ApiController]
[Route("api/labrooms")]
[Route("api/laboratory/rooms")]
[Route("api/v1/laboratory/rooms")]
public class LabRoomsController : ControllerBase
{
    private readonly IMongoDatabase _database;

    public LabRoomsController(IMongoDatabase database)
    {
        _database = database;
    }

    /// <summary>
    /// Retrieves active laboratory rooms from MongoDB.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetLabRooms(CancellationToken ct = default)
    {
        var collection = _database.GetCollection<BsonDocument>("lab_rooms");
        var docs = await collection.Find(FilterDefinition<BsonDocument>.Empty).ToListAsync(ct);
        var rooms = docs.Select(d => new
        {
            id = d.GetValue("_id", "").AsString,
            roomCode = d.Contains("room_code") ? d["room_code"].AsString : d.GetValue("_id", "").AsString,
            name = d.Contains("room_name") ? d["room_name"].AsString : "",
            floor = d.Contains("floor") ? d["floor"].AsString : "",
            accessLevel = d.Contains("access_level") ? d["access_level"].AsString : ""
        }).ToList();

        return Ok(new
        {
            success = true,
            data = rooms
        });
    }
}
