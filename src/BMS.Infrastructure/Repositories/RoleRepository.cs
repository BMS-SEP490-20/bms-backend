using System.Collections.Concurrent;
using BMS.Core.Constants;
using BMS.Core.Entities;
using BMS.Core.Interfaces;
using BMS.Infrastructure.Data;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace BMS.Infrastructure.Repositories;

/// <summary>
/// Implements Role repository interacting with MongoDB collection "roles" and in-memory fallback.
/// Conforms to: Repository Pattern & Clean Architecture.
/// </summary>
public class RoleRepository : IRoleRepository
{
    private readonly MongoDbContext _context;
    private readonly ILogger<RoleRepository> _logger;

    private static readonly ConcurrentDictionary<string, RoleEntity> _memoryRoles = new();
    private static bool _seeded = false;

    public RoleRepository(MongoDbContext context, ILogger<RoleRepository> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <summary>
    /// Retrieves paginated list of non-deleted roles.
    /// </summary>
    public async Task<(List<RoleEntity> Items, long TotalCount)> GetPagedAsync(
        int page,
        int pageSize,
        string? search = null,
        bool? isActive = null,
        CancellationToken ct = default)
    {
        if (_context.Roles != null)
        {
            try
            {
                var builder = Builders<RoleEntity>.Filter;
                var filter = builder.Eq(r => r.IsDeleted, false);

                if (!string.IsNullOrWhiteSpace(search))
                {
                    var searchLower = search.Trim().ToLowerInvariant();
                    filter &= (builder.Regex(r => r.RoleCode, new MongoDB.Bson.BsonRegularExpression(searchLower, "i")) |
                               builder.Regex(r => r.RoleName, new MongoDB.Bson.BsonRegularExpression(searchLower, "i")));
                }

                if (isActive.HasValue)
                {
                    filter &= builder.Eq(r => r.IsActive, isActive.Value);
                }

                var total = await _context.Roles.CountDocumentsAsync(filter, cancellationToken: ct);
                var items = await _context.Roles
                    .Find(filter)
                    .SortByDescending(r => r.CreatedAt)
                    .Skip((page - 1) * pageSize)
                    .Limit(pageSize)
                    .ToListAsync(ct);

                return (items, total);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to query roles from MongoDB. Falling back to memory store.");
            }
        }

        var query = _memoryRoles.Values.Where(r => !r.IsDeleted);
        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(r =>
                r.RoleCode.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                r.RoleName.Contains(search, StringComparison.OrdinalIgnoreCase));
        }

        if (isActive.HasValue)
        {
            query = query.Where(r => r.IsActive == isActive.Value);
        }

        var memoryList = query.OrderByDescending(r => r.CreatedAt).ToList();
        var pagedItems = memoryList.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        return (pagedItems, memoryList.Count);
    }

    public async Task<RoleEntity?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        if (_context.Roles != null)
        {
            try
            {
                var filter = Builders<RoleEntity>.Filter.And(
                    Builders<RoleEntity>.Filter.Eq(r => r.Id, id),
                    Builders<RoleEntity>.Filter.Eq(r => r.IsDeleted, false)
                );
                return await _context.Roles.Find(filter).FirstOrDefaultAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to query role by Id from MongoDB. Falling back to memory.");
            }
        }

        _memoryRoles.TryGetValue(id, out var role);
        return role is { IsDeleted: false } ? role : null;
    }

    public async Task<RoleEntity?> GetByCodeAsync(string roleCode, CancellationToken ct = default)
    {
        var upperCode = roleCode.Trim().ToUpperInvariant();

        if (_context.Roles != null)
        {
            try
            {
                var filter = Builders<RoleEntity>.Filter.And(
                    Builders<RoleEntity>.Filter.Eq(r => r.RoleCode, upperCode),
                    Builders<RoleEntity>.Filter.Eq(r => r.IsDeleted, false)
                );
                return await _context.Roles.Find(filter).FirstOrDefaultAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to query role by code from MongoDB. Falling back to memory.");
            }
        }

        return _memoryRoles.Values.FirstOrDefault(r =>
            !r.IsDeleted && r.RoleCode.Equals(upperCode, StringComparison.OrdinalIgnoreCase));
    }

    public async Task CreateAsync(RoleEntity role, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(role.Id))
        {
            role.Id = MongoDB.Bson.ObjectId.GenerateNewId().ToString();
        }

        if (_context.Roles != null)
        {
            try
            {
                await _context.Roles.InsertOneAsync(role, cancellationToken: ct);
                _memoryRoles[role.Id] = role;
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to insert role into MongoDB. Falling back to memory.");
            }
        }

        _memoryRoles[role.Id] = role;
    }

    public async Task UpdateAsync(RoleEntity role, CancellationToken ct = default)
    {
        role.UpdatedAt = DateTime.UtcNow;

        if (_context.Roles != null)
        {
            try
            {
                var filter = Builders<RoleEntity>.Filter.Eq(r => r.Id, role.Id);
                await _context.Roles.ReplaceOneAsync(filter, role, cancellationToken: ct);
                _memoryRoles[role.Id] = role;
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to update role in MongoDB. Falling back to memory.");
            }
        }

        _memoryRoles[role.Id] = role;
    }

    public async Task DeleteAsync(string id, CancellationToken ct = default)
    {
        var role = await GetByIdAsync(id, ct);
        if (role == null) return;

        role.IsDeleted = true;
        role.UpdatedAt = DateTime.UtcNow;

        await UpdateAsync(role, ct);
    }

    /// <summary>
    /// Seeds default built-in clinical roles with IsSystemRole = true per rule BR-ROLE-01.
    /// </summary>
    public async Task EnsureDefaultRolesSeededAsync(CancellationToken ct = default)
    {
        if (_seeded) return;
        _seeded = true;

        var defaultRoles = new List<RoleEntity>
        {
            new()
            {
                RoleCode = "ADMIN",
                RoleName = "Quản Trị Viên Hệ Thống",
                Description = "Toàn quyền quản trị phân hệ IAM, cấu hình tham số, danh mục và bảo mật.",
                Permissions = new List<string> {
                    SystemPermissions.IamView, SystemPermissions.IamManage,
                    SystemPermissions.LabApprove, SystemPermissions.BillingRefund
                },
                IsSystemRole = true,
                IsActive = true
            },
            new()
            {
                RoleCode = "DOCTOR",
                RoleName = "Bác Sĩ Khám Bệnh",
                Description = "Bác sĩ lâm sàng: Khám bệnh, chỉ định xét nghiệm và kê đơn EMR.",
                Permissions = new List<string> {
                    SystemPermissions.ClinicExam, SystemPermissions.ClinicOrder, SystemPermissions.LabTest
                },
                IsSystemRole = true,
                IsActive = true
            },
            new()
            {
                RoleCode = "LAB_TECHNICIAN",
                RoleName = "Kỹ Thuật Viên Xét Nghiệm",
                Description = "KTV phòng Lab: Tiếp nhận mẫu bệnh phẩm, vận hành máy sinh hóa, nhập kết quả.",
                Permissions = new List<string> {
                    SystemPermissions.LabTest, SystemPermissions.LabApprove, SystemPermissions.InventoryView
                },
                IsSystemRole = true,
                IsActive = true
            },
            new()
            {
                RoleCode = "RECEPTIONIST",
                RoleName = "Nhân Viên Tiếp Đón",
                Description = "Điều phối hàng đợi, tiếp nhận bệnh nhân, đặt lịch khám.",
                Permissions = new List<string> {
                    SystemPermissions.ReceptionQueue, SystemPermissions.ReceptionCheckin
                },
                IsSystemRole = true,
                IsActive = true
            },
            new()
            {
                RoleCode = "PATIENT",
                RoleName = "Bệnh Nhân",
                Description = "Tài khoản cổng thông tin bệnh nhân: Tra cứu kết quả và đặt lịch khám.",
                Permissions = new List<string>(),
                IsSystemRole = true,
                IsActive = true
            }
        };

        foreach (var r in defaultRoles)
        {
            var existing = await GetByCodeAsync(r.RoleCode, ct);
            if (existing == null)
            {
                await CreateAsync(r, ct);
            }
        }
    }
}
