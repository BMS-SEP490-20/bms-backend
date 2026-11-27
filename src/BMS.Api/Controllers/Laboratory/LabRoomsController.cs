using Microsoft.AspNetCore.Authorization;
using BMS.Core.Constants;
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
// WHY: UC34: Manager, Admin. (ROLE-PERMISSION-MATRIX §1, TIEU-CHI §1.2)
[Route("api/facilities/rooms")]
[Authorize(Roles = RoleSets.AllStaff)]
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
        var notDeleted = Builders<BsonDocument>.Filter.Ne("is_deleted", true)
            & Builders<BsonDocument>.Filter.Ne("IsDeleted", true)
            & Builders<BsonDocument>.Filter.Ne("isDeleted", true);
        var docs = await collection.Find(notDeleted).ToListAsync(ct);
        var rooms = docs.Select(d => new
        {
            id = d.Contains("_id") && !d["_id"].IsBsonNull ? d["_id"].ToString()! : "",
            roomCode = d.Contains("room_code") && !d["room_code"].IsBsonNull ? d["room_code"].ToString()! : (d.Contains("_id") ? d["_id"].ToString()! : ""),
            name = d.Contains("room_name") && !d["room_name"].IsBsonNull ? d["room_name"].ToString()! : "",
            floor = d.Contains("floor") && !d["floor"].IsBsonNull ? d["floor"].ToString()! : "",
            accessLevel = d.Contains("access_level") && !d["access_level"].IsBsonNull ? d["access_level"].ToString()! : ""
        }).ToList();

        return Ok(new
        {
            success = true,
            data = rooms
        });
    }
}
