using BMS.Core.Entities;
using BMS.Core.Interfaces;
using MongoDB.Driver;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BMS.Infrastructure.Repositories;

/// <summary>
/// MongoDB implementation of IMedicalRecordRepository.
/// </summary>
public class MedicalRecordRepository : IMedicalRecordRepository
{
    private readonly IMongoCollection<MedicalRecord> _collection;

    public MedicalRecordRepository(IMongoDatabase database)
    {
        _collection = database.GetCollection<MedicalRecord>("medical_records");

        // Unique index on recordCode
        var indexKeys = Builders<MedicalRecord>.IndexKeys.Ascending(x => x.RecordCode);
        _collection.Indexes.CreateOne(new CreateIndexModel<MedicalRecord>(indexKeys, new CreateIndexOptions { Unique = true }));

        // PatientId lookup index
        var patientIndex = Builders<MedicalRecord>.IndexKeys
            .Ascending(x => x.PatientId)
            .Ascending(x => x.IsDeleted);
        _collection.Indexes.CreateOne(new CreateIndexModel<MedicalRecord>(patientIndex));
    }

    public async Task<List<MedicalRecord>> GetByPatientIdAsync(string patientId, CancellationToken ct = default)
    {
        return await _collection
            .Find(x => x.PatientId == patientId && !x.IsDeleted)
            .SortByDescending(x => x.CreatedAt)
            .ToListAsync(ct);
    }

    public async Task<MedicalRecord?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        return await _collection
            .Find(x => (x.Id == id || x.RecordCode == id) && !x.IsDeleted)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<MedicalRecord?> GetByRecordCodeAsync(string recordCode, CancellationToken ct = default)
    {
        return await _collection
            .Find(x => x.RecordCode == recordCode && !x.IsDeleted)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<string> GenerateNextRecordCodeAsync(DateTime date, CancellationToken ct = default)
    {
        var prefix = $"EMR-{date:yyMMdd}-";
        var last = await _collection
            .Find(x => x.RecordCode.StartsWith(prefix))
            .SortByDescending(x => x.RecordCode)
            .Limit(1)
            .FirstOrDefaultAsync(ct);

        var nextSeq = 1;
        if (last != null && last.RecordCode.Length >= prefix.Length + 4)
        {
            var tail = last.RecordCode[prefix.Length..];
            if (int.TryParse(tail, out var seq))
            {
                nextSeq = seq + 1;
            }
        }

        return $"{prefix}{nextSeq:D4}";
    }

    public async Task InsertAsync(MedicalRecord record, CancellationToken ct = default)
    {
        await _collection.InsertOneAsync(record, cancellationToken: ct);
    }

    public async Task UpdateAsync(MedicalRecord record, CancellationToken ct = default)
    {
        record.UpdatedAt = DateTime.UtcNow;
        await _collection.ReplaceOneAsync(x => x.Id == record.Id, record, cancellationToken: ct);
    }

    public async Task<bool> SoftDeleteAsync(string id, string deletedBy, string deleteReason, CancellationToken ct = default)
    {
        var update = Builders<MedicalRecord>.Update
            .Set(x => x.IsDeleted, true)
            .Set(x => x.DeletedAt, DateTime.UtcNow)
            .Set(x => x.DeletedBy, deletedBy)
            .Set(x => x.DeleteReason, deleteReason)
            .Set(x => x.UpdatedAt, DateTime.UtcNow);

        var result = await _collection.UpdateOneAsync(
            x => (x.Id == id || x.RecordCode == id) && !x.IsDeleted,
            update,
            cancellationToken: ct);

        return result.ModifiedCount > 0;
    }
}
