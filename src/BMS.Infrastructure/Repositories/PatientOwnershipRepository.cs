using BMS.Application.Interfaces;
using MongoDB.Bson;
using MongoDB.Driver;

namespace BMS.Infrastructure.Repositories;

/// <summary>
/// Mongo-backed <see cref="IPatientOwnership"/>: a patient document is owned by an account when its
/// <c>AccountId</c>/<c>accountId</c> matches. Reads only; one indexed lookup per check.
/// </summary>
public sealed class PatientOwnershipRepository(IMongoDatabase database) : IPatientOwnership
{
    private IMongoCollection<BsonDocument> Patients => database.GetCollection<BsonDocument>("patients");

    private static BsonArray Ids(string id) => ObjectId.TryParse(id, out var oid) ? new BsonArray { id, oid } : new BsonArray { id };

    /// <inheritdoc />
    public async Task<bool> IsOwnPatientAsync(string accountId, string patientId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(accountId) || string.IsNullOrWhiteSpace(patientId)) return false;
        // WHY: several modules (notifications, payments) key records by the account ID directly.
        if (string.Equals(accountId, patientId, StringComparison.Ordinal)) return true;

        var f = Builders<BsonDocument>.Filter;
        var owner = f.Or(f.In("AccountId", Ids(accountId)), f.In("accountId", Ids(accountId)));
        var record = f.Or(f.In("_id", Ids(patientId)), f.Eq("PatientCode", patientId), f.Eq("patientCode", patientId), f.Eq("patient_code", patientId));
        return await Patients.Find(owner & record).Limit(1).CountDocumentsAsync(ct) > 0;
    }
}
