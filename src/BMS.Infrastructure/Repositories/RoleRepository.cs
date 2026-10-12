using BMS.Application.Interfaces;
using BMS.Core.Entities;
using BMS.Infrastructure.Mongo;
using MongoDB.Driver;

namespace BMS.Infrastructure.Repositories;

public sealed class RoleRepository : IRoleRepository
{
    private readonly IMongoCollection<Role> _roles;

    public RoleRepository(IMongoDatabase db, MongoCollections names)
        => _roles = db.GetCollection<Role>(names.Roles);

    public async Task<Role?> GetByIdAsync(int roleId, CancellationToken ct = default)
        => await _roles.Find(r => r.Id == roleId).FirstOrDefaultAsync(ct);
}
