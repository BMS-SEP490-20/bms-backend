using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
using BMS.Application.DTOs.Users;
using BMS.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;
using MongoDB.Driver;

namespace BMS.Api.Controllers.Users;

/// <summary>
/// Controller managing clinical and administrative user profile and account operations (UC08, UC14).
/// Connects directly to MongoDB 'accounts' collection for complete, non-mocked data persistence.
/// </summary>
[ApiController]
[Route("api/v1/users")]
[Route("api/users")]
[Produces("application/json")]
public class UsersController : ControllerBase
{
    private readonly IUserService _userService;
    private readonly IMongoDatabase _database;
    private readonly ILogger<UsersController> _logger;

    public UsersController(
        IUserService userService,
        IMongoDatabase database,
        ILogger<UsersController> logger)
    {
        _userService = userService;
        _database = database;
        _logger = logger;
    }

    private IMongoCollection<BsonDocument> AccountsCollection =>
        _database.GetCollection<BsonDocument>("accounts");

    /// <summary>
    /// Retrieves personal profile of currently authenticated user (UC08).
    /// </summary>
    [HttpGet("me")]
    [ProducesResponseType(typeof(ApiResponse<UserProfileResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetCurrentUserProfile(CancellationToken ct = default)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub")
            ?? User.FindFirstValue("uid");

        if (string.IsNullOrEmpty(userId))
        {
            var demoHeader = Request.Headers["X-Demo-User"].FirstOrDefault();
            if (!string.IsNullOrEmpty(demoHeader))
            {
                var demoProfile = await _userService.EnsureDefaultUserProfileAsync(ct);
                return Ok(ApiResponse<UserProfileResponseDto>.Ok(demoProfile, "Retrieved demo profile successfully."));
            }

            return Unauthorized(ApiResponse<object>.Fail(
                "Authentication required. Please provide a valid Bearer token.",
                "MSG-ERR-AUTH-INVALID"));
        }

        _logger.LogInformation("[BMS Users API] Querying profile for authenticated user {UserId}", userId);
        var profile = await _userService.GetProfileByIdAsync(userId, ct);

        if (profile == null)
        {
            return NotFound(ApiResponse<object>.Fail("User profile not found.", "MSG-ERR-USER-NOT-FOUND"));
        }

        return Ok(ApiResponse<UserProfileResponseDto>.Ok(profile, "Profile retrieved successfully."));
    }

    /// <summary>
    /// Convenience endpoint seeding and retrieving default clinical patient profile for development and UI testing.
    /// </summary>
    [HttpGet("demo")]
    [ProducesResponseType(typeof(ApiResponse<UserProfileResponseDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetDemoProfile(CancellationToken ct = default)
    {
        var profile = await _userService.EnsureDefaultUserProfileAsync(ct);
        return Ok(ApiResponse<UserProfileResponseDto>.Ok(profile, "Demo profile loaded successfully."));
    }

    /// <summary>
    /// UC14 - Master User & Employee Accounts Retrieval directly from MongoDB 'accounts'.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetAllUsers(
        [FromQuery] string? search = null,
        [FromQuery] int? roleId = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        var filterBuilder = Builders<BsonDocument>.Filter;
        var filter = filterBuilder.Ne("isDeleted", true);

        if (roleId.HasValue && roleId.Value > 0)
        {
            filter &= filterBuilder.Or(
                filterBuilder.Eq("RoleId", roleId.Value),
                filterBuilder.Eq("roleId", roleId.Value)
            );
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var regex = new BsonRegularExpression(Regex.Escape(search.Trim()), "i");
            filter &= filterBuilder.Or(
                filterBuilder.Regex("Username", regex),
                filterBuilder.Regex("username", regex),
                filterBuilder.Regex("FullName", regex),
                filterBuilder.Regex("fullName", regex),
                filterBuilder.Regex("Email", regex),
                filterBuilder.Regex("email", regex),
                filterBuilder.Regex("PhoneNumber", regex),
                filterBuilder.Regex("phoneNumber", regex)
            );
        }

        var total = await AccountsCollection.CountDocumentsAsync(filter, cancellationToken: ct);
        var docs = await AccountsCollection.Find(filter)
            .Skip((page - 1) * pageSize)
            .Limit(pageSize)
            .ToListAsync(ct);

        var users = docs.Select(doc => MapBsonToUserDto(doc)).ToList();

        return Ok(new
        {
            success = true,
            total,
            page,
            pageSize,
            data = users
        });
    }

    /// <summary>
    /// UC14 - Retrieves a single user account by identifier.
    /// </summary>
    [HttpGet("{id}")]
    public async Task<IActionResult> GetUserById(string id, CancellationToken ct = default)
    {
        FilterDefinition<BsonDocument> filter;
        if (ObjectId.TryParse(id, out var objId))
        {
            filter = Builders<BsonDocument>.Filter.Eq("_id", objId);
        }
        else
        {
            filter = Builders<BsonDocument>.Filter.Eq("_id", id);
        }

        var doc = await AccountsCollection.Find(filter).FirstOrDefaultAsync(ct);
        if (doc == null)
        {
            return NotFound(new { success = false, message = "Account not found." });
        }

        return Ok(new { success = true, data = MapBsonToUserDto(doc) });
    }

    /// <summary>
    /// UC14 - Creates a new employee or user account in MongoDB 'accounts'.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> CreateUser([FromBody] CreateUserAccountDto request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.FullName))
        {
            return BadRequest(new { success = false, message = "Username and FullName are required." });
        }

        // Check if username or email already exists
        var existing = await AccountsCollection.Find(
            Builders<BsonDocument>.Filter.Or(
                Builders<BsonDocument>.Filter.Eq("username", request.Username.Trim()),
                Builders<BsonDocument>.Filter.Eq("Username", request.Username.Trim()),
                Builders<BsonDocument>.Filter.Eq("email", request.Email?.Trim() ?? ""),
                Builders<BsonDocument>.Filter.Eq("Email", request.Email?.Trim() ?? "")
            )
        ).FirstOrDefaultAsync(ct);

        if (existing != null)
        {
            return Conflict(new { success = false, message = "Username or email already exists." });
        }

        var salt = BCrypt.Net.BCrypt.GenerateSalt();
        var rawPassword = string.IsNullOrWhiteSpace(request.Password) ? "Admin@123" : request.Password;
        var hash = BCrypt.Net.BCrypt.HashPassword(rawPassword, salt);
        var newId = ObjectId.GenerateNewId();
        var now = DateTime.UtcNow;

        var newDoc = new BsonDocument
        {
            { "_id", newId },
            { "Username", request.Username.Trim() },
            { "username", request.Username.Trim() },
            { "Email", request.Email?.Trim() ?? "" },
            { "email", request.Email?.Trim() ?? "" },
            { "FullName", request.FullName.Trim() },
            { "fullName", request.FullName.Trim() },
            { "PhoneNumber", request.PhoneNumber?.Trim() ?? "" },
            { "phoneNumber", request.PhoneNumber?.Trim() ?? "" },
            { "RoleId", request.RoleId },
            { "roleId", request.RoleId },
            { "Status", 1 },
            { "status", 1 },
            { "isActive", true },
            { "isDeleted", false },
            { "CreatedAt", now },
            { "createdAt", now },
            { "passwordHash", hash },
            { "Password", hash }
        };

        await AccountsCollection.InsertOneAsync(newDoc, cancellationToken: ct);

        return StatusCode(StatusCodes.Status201Created, new
        {
            success = true,
            message = "Account created successfully.",
            data = MapBsonToUserDto(newDoc)
        });
    }

    /// <summary>
    /// UC14 - Updates an existing user account in MongoDB.
    /// </summary>
    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateUser(string id, [FromBody] UpdateUserAccountDto request, CancellationToken ct = default)
    {
        FilterDefinition<BsonDocument> filter = ObjectId.TryParse(id, out var objId)
            ? Builders<BsonDocument>.Filter.Eq("_id", objId)
            : Builders<BsonDocument>.Filter.Eq("_id", id);

        var updateDef = Builders<BsonDocument>.Update
            .Set("FullName", request.FullName?.Trim() ?? "")
            .Set("fullName", request.FullName?.Trim() ?? "")
            .Set("Email", request.Email?.Trim() ?? "")
            .Set("email", request.Email?.Trim() ?? "")
            .Set("PhoneNumber", request.PhoneNumber?.Trim() ?? "")
            .Set("phoneNumber", request.PhoneNumber?.Trim() ?? "");

        if (request.RoleId > 0)
        {
            updateDef = updateDef
                .Set("RoleId", request.RoleId)
                .Set("roleId", request.RoleId);
        }

        if (request.IsActive.HasValue)
        {
            updateDef = updateDef
                .Set("isActive", request.IsActive.Value)
                .Set("Status", request.IsActive.Value ? 1 : 0)
                .Set("status", request.IsActive.Value ? 1 : 0);
        }

        var result = await AccountsCollection.UpdateOneAsync(filter, updateDef, cancellationToken: ct);
        if (result.MatchedCount == 0)
        {
            return NotFound(new { success = false, message = "Account not found." });
        }

        return Ok(new { success = true, message = "Account updated successfully." });
    }

    /// <summary>
    /// UC14 - Deactivates or removes a user account in MongoDB.
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteUser(string id, CancellationToken ct = default)
    {
        FilterDefinition<BsonDocument> filter = ObjectId.TryParse(id, out var objId)
            ? Builders<BsonDocument>.Filter.Eq("_id", objId)
            : Builders<BsonDocument>.Filter.Eq("_id", id);

        var update = Builders<BsonDocument>.Update
            .Set("isDeleted", true)
            .Set("isActive", false)
            .Set("Status", 0)
            .Set("status", 0);

        var result = await AccountsCollection.UpdateOneAsync(filter, update, cancellationToken: ct);
        if (result.MatchedCount == 0)
        {
            return NotFound(new { success = false, message = "Account not found." });
        }

        return Ok(new { success = true, message = "Account deleted successfully." });
    }

    /// <summary>
    /// UC14 - Toggles active status of an account.
    /// </summary>
    [HttpPatch("{id}/status")]
    public async Task<IActionResult> ToggleStatus(string id, [FromBody] ToggleStatusDto request, CancellationToken ct = default)
    {
        FilterDefinition<BsonDocument> filter = ObjectId.TryParse(id, out var objId)
            ? Builders<BsonDocument>.Filter.Eq("_id", objId)
            : Builders<BsonDocument>.Filter.Eq("_id", id);

        var update = Builders<BsonDocument>.Update
            .Set("isActive", request.IsActive)
            .Set("Status", request.IsActive ? 1 : 0)
            .Set("status", request.IsActive ? 1 : 0);

        var result = await AccountsCollection.UpdateOneAsync(filter, update, cancellationToken: ct);
        if (result.MatchedCount == 0)
        {
            return NotFound(new { success = false, message = "Account not found." });
        }

        return Ok(new { success = true, message = $"Account has been {(request.IsActive ? "activated" : "deactivated")}." });
    }

    /// <summary>
    /// UC14 - Administrative password reset for any system account.
    /// </summary>
    [HttpPost("{id}/reset-password")]
    public async Task<IActionResult> ResetPassword(string id, [FromBody] AdminResetPasswordDto request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.NewPassword))
        {
            return BadRequest(new { success = false, message = "New password is required." });
        }

        FilterDefinition<BsonDocument> filter = ObjectId.TryParse(id, out var objId)
            ? Builders<BsonDocument>.Filter.Eq("_id", objId)
            : Builders<BsonDocument>.Filter.Eq("_id", id);

        var salt = BCrypt.Net.BCrypt.GenerateSalt();
        var hash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword, salt);

        var update = Builders<BsonDocument>.Update
            .Set("passwordHash", hash)
            .Set("Password", hash);

        var result = await AccountsCollection.UpdateOneAsync(filter, update, cancellationToken: ct);
        if (result.MatchedCount == 0)
        {
            return NotFound(new { success = false, message = "Account not found." });
        }

        return Ok(new { success = true, message = "Password updated successfully." });
    }

    private static object MapBsonToUserDto(BsonDocument doc)
    {
        var id = doc["_id"].ToString() ?? "";
        var username = doc.Contains("username") ? doc["username"].AsString : (doc.Contains("Username") ? doc["Username"].AsString : "");
        var email = doc.Contains("email") ? doc["email"].AsString : (doc.Contains("Email") ? doc["Email"].AsString : "");
        var fullName = doc.Contains("fullName") ? doc["fullName"].AsString : (doc.Contains("FullName") ? doc["FullName"].AsString : "");
        var phone = doc.Contains("phoneNumber") ? doc["phoneNumber"].AsString : (doc.Contains("PhoneNumber") ? doc["PhoneNumber"].AsString : "");
        
        int roleId = 1;
        if (doc.Contains("roleId") && doc["roleId"].IsInt32) roleId = doc["roleId"].AsInt32;
        else if (doc.Contains("RoleId") && doc["RoleId"].IsInt32) roleId = doc["RoleId"].AsInt32;

        bool isActive = true;
        if (doc.Contains("isActive") && doc["isActive"].IsBoolean) isActive = doc["isActive"].AsBoolean;
        else if (doc.Contains("status") && doc["status"].IsInt32) isActive = doc["status"].AsInt32 == 1;
        else if (doc.Contains("Status") && doc["Status"].IsInt32) isActive = doc["Status"].AsInt32 == 1;

        string role = roleId switch
        {
            1 => "admin",
            2 => "doctor",
            3 => "lab_user",
            4 => "manager",
            5 => "nurse",
            6 => "receptionist",
            7 => "cashier",
            8 => "warehouse",
            9 => "patient",
            _ => "patient"
        };

        var created = doc.Contains("CreatedAt") ? doc["CreatedAt"].ToUniversalTime().ToString("o")
            : (doc.Contains("createdAt") ? doc["createdAt"].ToUniversalTime().ToString("o") : DateTime.UtcNow.ToString("o"));

        return new
        {
            id,
            username,
            email,
            fullName,
            phone,
            roleId,
            role,
            isActive,
            status = isActive ? "active" : "inactive",
            createdAt = created
        };
    }
}

public class CreateUserAccountDto
{
    public string Username { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? PhoneNumber { get; set; }
    public int RoleId { get; set; } = 9;
    public string? Password { get; set; }
}

public class UpdateUserAccountDto
{
    public string? FullName { get; set; }
    public string? Email { get; set; }
    public string? PhoneNumber { get; set; }
    public int RoleId { get; set; }
    public bool? IsActive { get; set; }
}

public class ToggleStatusDto
{
    public bool IsActive { get; set; }
}

public class AdminResetPasswordDto
{
    public string NewPassword { get; set; } = string.Empty;
}

