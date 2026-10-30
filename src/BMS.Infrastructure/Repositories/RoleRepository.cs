using System.Collections.Concurrent;
using BMS.Core.Constants;
using BMS.Core.Entities;
using BMS.Core.Interfaces;
using BMS.Infrastructure.Data;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace BMS.Infrastructure.Repositories;

/// <summary>
/// Repository quản lý role trong MongoDB,
/// hỗ trợ in-memory fallback.
/// </summary>
public class RoleRepository : IRoleRepository
{
    private readonly MongoDbContext _context;
    private readonly ILogger<RoleRepository> _logger;

    private static readonly ConcurrentDictionary<string, RoleEntity>
        _memoryRoles = new();

    private static bool _seeded;

    public RoleRepository(
        MongoDbContext context,
        ILogger<RoleRepository> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <summary>
    /// Lấy danh sách role có phân trang,
    /// hỗ trợ tìm kiếm và lọc trạng thái.
    /// </summary>
    public async Task<(List<RoleEntity> Items, long TotalCount)>
        GetPagedAsync(
            int page,
            int pageSize,
            string? search = null,
            bool? isActive = null,
            CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Max(1, pageSize);

        if (_context.Roles != null)
        {
            try
            {
                var builder = Builders<RoleEntity>.Filter;

                var filter = builder.Eq(
                    r => r.IsDeleted,
                    false);

                if (!string.IsNullOrWhiteSpace(search))
                {
                    var keyword = search.Trim();

                    filter &= (
                        builder.Regex(
                            r => r.RoleCode,
                            new MongoDB.Bson.BsonRegularExpression(
                                keyword, "i"))
                        |
                        builder.Regex(
                            r => r.RoleName,
                            new MongoDB.Bson.BsonRegularExpression(
                                keyword, "i"))
                    );
                }

                if (isActive.HasValue)
                {
                    filter &= builder.Eq(
                        r => r.IsActive,
                        isActive.Value);
                }

                var total = await _context.Roles
                    .CountDocumentsAsync(
                        filter,
                        cancellationToken: ct);

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
                _logger.LogWarning(
                    ex,
                    "Failed to query roles from MongoDB. " +
                    "Falling back to memory store.");
            }
        }

        var query = _memoryRoles.Values
            .Where(r => !r.IsDeleted);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var keyword = search.Trim();

            query = query.Where(r =>
                r.RoleCode.Contains(
                    keyword,
                    StringComparison.OrdinalIgnoreCase)
                ||
                r.RoleName.Contains(
                    keyword,
                    StringComparison.OrdinalIgnoreCase));
        }

        if (isActive.HasValue)
        {
            query = query.Where(
                r => r.IsActive == isActive.Value);
        }

        var memoryList = query
            .OrderByDescending(r => r.CreatedAt)
            .ToList();

        var pagedItems = memoryList
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        return (pagedItems, memoryList.Count);
    }

    /// <summary>
    /// Tìm role theo ID, bỏ qua role đã xóa.
    /// </summary>
    public async Task<RoleEntity?> GetByIdAsync(
        string id,
        CancellationToken ct = default)
    {
        if (_context.Roles != null)
        {
            try
            {
                var filter =
                    Builders<RoleEntity>.Filter.And(
                        Builders<RoleEntity>.Filter.Eq(
                            r => r.Id, id),
                        Builders<RoleEntity>.Filter.Eq(
                            r => r.IsDeleted, false));

                return await _context.Roles
                    .Find(filter)
                    .FirstOrDefaultAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to query role by ID from MongoDB. " +
                    "Falling back to memory.");
            }
        }

        _memoryRoles.TryGetValue(id, out var role);

        return role is { IsDeleted: false }
            ? role
            : null;
    }

    /// <summary>
    /// Tìm role theo mã role.
    /// </summary>
    public async Task<RoleEntity?> GetByCodeAsync(
        string roleCode,
        CancellationToken ct = default)
    {
        var upperCode = roleCode.Trim().ToUpperInvariant();

        if (_context.Roles != null)
        {
            try
            {
                var filter =
                    Builders<RoleEntity>.Filter.And(
                        Builders<RoleEntity>.Filter.Eq(
                            r => r.RoleCode, upperCode),
                        Builders<RoleEntity>.Filter.Eq(
                            r => r.IsDeleted, false));

                return await _context.Roles
                    .Find(filter)
                    .FirstOrDefaultAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to query role by code from MongoDB. " +
                    "Falling back to memory.");
            }
        }

        return _memoryRoles.Values.FirstOrDefault(r =>
            !r.IsDeleted &&
            r.RoleCode.Equals(
                upperCode,
                StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Tìm role theo RoleId số nguyên (_id trong collection "roles" của BMS_Database).
    /// </summary>
    public async Task<Role?> GetRoleByIdAsync(
        int roleId,
        CancellationToken ct = default)
    {
        if (_context.Database != null)
        {
            try
            {
                var rolesCol = _context.Database.GetCollection<Role>("roles");
                var dbRole = await rolesCol
                    .Find(Builders<Role>.Filter.Eq(r => r.Id, roleId))
                    .FirstOrDefaultAsync(ct);

                if (dbRole != null)
                {
                    EnrichStandardPermissions(dbRole);
                    return dbRole;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to query numeric role {RoleId} from MongoDB. Falling back to default role map.",
                    roleId);
            }
        }

        var fallback = BuildDefaultNumericRole(roleId);
        if (fallback != null)
        {
            EnrichStandardPermissions(fallback);
        }

        return fallback;
    }

    /// <summary>
    /// Default role documents for the 6 seeded IDs (decision 30/10/2026 point 1), used when the
    /// <c>roles</c> collection has no document for the ID. Names come from <see cref="RoleNames"/>.
    /// </summary>
    private static Role? BuildDefaultNumericRole(int roleId) => roleId switch
    {
        1 => new Role { Id = 1, RoleName = RoleNames.Admin, Description = "System Administrator",
            Permissions = ["MANAGE_SERVICE_PACKAGE", "MANAGE_USERS", "MANAGE_ROLES", "MANAGE_SCHEDULE"] },
        2 => new Role { Id = 2, RoleName = RoleNames.Doctor, Description = "Bác sĩ",
            Permissions = ["EXAMINE_PATIENT", "ORDER_TEST", "CREATE_PRESCRIPTION", "VIEW_PATIENT_HISTORY"] },
        3 => new Role { Id = 3, RoleName = RoleNames.Lab, Description = "Lab (gồm KTV, lấy mẫu, kho)",
            Permissions = ["RECEIVE_SAMPLE", "COLLECT_SAMPLE", "RUN_ANALYZER", "ENTER_RESULT", "MANAGE_REAGENTS"] },
        4 => new Role { Id = 4, RoleName = RoleNames.Receptionist, Description = "Lễ tân (gồm Thu ngân)",
            Permissions = ["CHECK_IN", "PROCESS_PAYMENT", "MANAGE_APPOINTMENT"] },
        5 => new Role { Id = 5, RoleName = RoleNames.Patient, Description = "Bệnh nhân",
            Permissions = ["BOOK_APPOINTMENT", "VIEW_OWN_RESULT", "MANAGE_PROFILE"] },
        6 => new Role { Id = 6, RoleName = RoleNames.Manager, Description = "Quản lý (gồm Trưởng phòng Lab)",
            Permissions = ["APPROVE_RESULT", "UNLOCK_QC", "EDIT_PANIC_THRESHOLD", "VIEW_ALL_LAB", "MANAGE_SCHEDULE", "MANAGE_REAGENTS"] },
        _ => null
    };

    /// <summary>Clinical permissions Admin must never hold (decision 10: Admin does not see clinical data).</summary>
    private static readonly string[] AdminForbidden =
        ["ALL", "VIEW_PATIENT_RECORDS", "VIEW_PATIENTS", "REGISTER_PATIENTS", "COLLECT_SAMPLE", "COLLECT_SAMPLES", "RECEIVE_SAMPLE", "RECEIVE_SAMPLES", "VIEW_ALL_LAB", "ENTER_RESULT", "APPROVE_RESULT"];

    /// <summary>
    /// Normalises a role document to the canonical 6-role model: canonical name from the seeded ID,
    /// standard permissions added, and permissions the role must not hold removed.
    /// </summary>
    private static void EnrichStandardPermissions(Role role)
    {
        role.Permissions ??= new List<string>();
        var canonical = RoleNames.FromRoleId(role.Id) ?? RoleNames.Normalize(role.RoleName);
        if (canonical is null) return;
        // WHY: old documents carry LabHead/LabTech/Vietnamese display names; the claim must be canonical.
        role.RoleName = canonical;
        var existing = new HashSet<string>(role.Permissions, StringComparer.OrdinalIgnoreCase);

        void Ensure(params string[] perms)
        {
            foreach (var p in perms)
                if (existing.Add(p)) role.Permissions.Add(p);
        }
        void Remove(params string[] perms)
        {
            role.Permissions.RemoveAll(p => perms.Contains(p, StringComparer.OrdinalIgnoreCase));
            existing.ExceptWith(perms);
        }

        switch (canonical)
        {
            case RoleNames.Admin:
                Ensure("MANAGE_SERVICE_PACKAGE", "MANAGE_USERS", "MANAGE_ROLES");
                Remove(AdminForbidden);
                break;
            case RoleNames.Manager:
                Ensure("APPROVE_RESULT", "UNLOCK_QC", "EDIT_PANIC_THRESHOLD", "VIEW_ALL_LAB", "MANAGE_SCHEDULE", "MANAGE_REAGENTS", "VIEW_PATIENT_RECORDS");
                break;
            case RoleNames.Lab:
                Ensure("RECEIVE_SAMPLE", "RECEIVE_SAMPLES", "RUN_ANALYZER", "ENTER_RESULT", "ENTER_LAB_RESULTS", "MANAGE_REAGENTS", "COLLECT_SAMPLE", "COLLECT_SAMPLES", "VIEW_ALL_LAB");
                Remove("APPROVE_RESULT", "UNLOCK_QC", "EDIT_PANIC_THRESHOLD");
                break;
            case RoleNames.Doctor:
                Ensure("EXAMINE_PATIENT", "CLINIC_EXAM", "ORDER_TEST", "CREATE_PRESCRIPTION", "VIEW_PATIENT_HISTORY", "VIEW_PATIENT_RECORDS", "VIEW_PATIENTS");
                break;
            case RoleNames.Receptionist:
                Ensure("CHECK_IN", "CHECKIN_PATIENT", "REGISTER_PATIENTS", "VIEW_PATIENTS", "PROCESS_PAYMENT", "MANAGE_APPOINTMENT", "CREATE_APPOINTMENTS", "MANAGE_QUEUE");
                Remove("COLLECT_SAMPLE", "COLLECT_SAMPLES");
                break;
            case RoleNames.Patient:
                Ensure("BOOK_APPOINTMENT", "VIEW_OWN_RESULT", "MANAGE_PROFILE");
                break;
        }
    }

    /// <summary>
    /// Tạo role mới.
    /// </summary>
    public async Task CreateAsync(
        RoleEntity role,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(role.Id))
        {
            role.Id = MongoDB.Bson.ObjectId
                .GenerateNewId()
                .ToString();
        }

        if (_context.Roles != null)
        {
            try
            {
                await _context.Roles.InsertOneAsync(
                    role,
                    cancellationToken: ct);

                _memoryRoles[role.Id] = role;
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to insert role into MongoDB. " +
                    "Falling back to memory.");
            }
        }

        _memoryRoles[role.Id] = role;
    }

    /// <summary>
    /// Cập nhật role.
    /// </summary>
    public async Task UpdateAsync(
        RoleEntity role,
        CancellationToken ct = default)
    {
        role.UpdatedAt = DateTime.UtcNow;

        if (_context.Roles != null)
        {
            try
            {
                var filter =
                    Builders<RoleEntity>.Filter.Eq(
                        r => r.Id, role.Id);

                var result = await _context.Roles
                    .ReplaceOneAsync(
                        filter,
                        role,
                        cancellationToken: ct);

                if (result.MatchedCount > 0)
                {
                    _memoryRoles[role.Id] = role;
                    return;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to update role in MongoDB. " +
                    "Falling back to memory.");
            }
        }

        _memoryRoles[role.Id] = role;
    }

    /// <summary>
    /// Xóa mềm role.
    /// </summary>
    public async Task DeleteAsync(
        string id,
        CancellationToken ct = default)
    {
        var role = await GetByIdAsync(id, ct);

        if (role == null)
        {
            return;
        }

        role.IsDeleted = true;
        role.UpdatedAt = DateTime.UtcNow;

        await UpdateAsync(role, ct);
    }

    /// <summary>
    /// Khởi tạo các role mặc định của hệ thống.
    /// </summary>
    public async Task EnsureDefaultRolesSeededAsync(
        CancellationToken ct = default)
    {
        if (_seeded)
        {
            return;
        }

        var defaultRoles = new List<RoleEntity>
        {
            new()
            {
                RoleCode = "ADMIN",
                RoleName = "Quản Trị Viên Hệ Thống",
                Description =
                    "Toàn quyền quản trị phân hệ IAM, " +
                    "cấu hình tham số, danh mục và bảo mật.",
                Permissions = new List<string>
                {
                    SystemPermissions.IamView,
                    SystemPermissions.IamManage,
                    SystemPermissions.LabApprove,
                    SystemPermissions.BillingRefund
                },
                IsSystemRole = true,
                IsActive = true
            },
            new()
            {
                RoleCode = "DOCTOR",
                RoleName = "Bác Sĩ Khám Bệnh",
                Description =
                    "Bác sĩ lâm sàng: Khám bệnh, " +
                    "chỉ định xét nghiệm và kê đơn EMR.",
                Permissions = new List<string>
                {
                    SystemPermissions.ClinicExam,
                    SystemPermissions.ClinicOrder,
                    SystemPermissions.LabTest
                },
                IsSystemRole = true,
                IsActive = true
            },
            new()
            {
                RoleCode = "LAB_TECHNICIAN",
                RoleName = "Kỹ Thuật Viên Xét Nghiệm",
                Description =
                    "Tiếp nhận mẫu bệnh phẩm, vận hành " +
                    "máy xét nghiệm và nhập kết quả.",
                Permissions = new List<string>
                {
                    SystemPermissions.LabTest,
                    SystemPermissions.LabApprove,
                    SystemPermissions.InventoryView
                },
                IsSystemRole = true,
                IsActive = true
            },
            new()
            {
                RoleCode = "RECEPTIONIST",
                RoleName = "Nhân Viên Tiếp Đón",
                Description =
                    "Điều phối hàng đợi, tiếp nhận " +
                    "bệnh nhân và đặt lịch khám.",
                Permissions = new List<string>
                {
                    SystemPermissions.ReceptionQueue,
                    SystemPermissions.ReceptionCheckin
                },
                IsSystemRole = true,
                IsActive = true
            },
            new()
            {
                RoleCode = "PATIENT",
                RoleName = "Bệnh Nhân",
                Description =
                    "Tra cứu kết quả và đặt lịch khám.",
                Permissions = new List<string>(),
                IsSystemRole = true,
                IsActive = true
            }
        };

        foreach (var role in defaultRoles)
        {
            ct.ThrowIfCancellationRequested();

            var existing = await GetByCodeAsync(
                role.RoleCode,
                ct);

            if (existing == null)
            {
                await CreateAsync(role, ct);
            }
        }

        _seeded = true;
    }
}

