using BMS.Core.Entities;
using BMS.Core.Interfaces;
using MongoDB.Driver;

namespace BMS.Infrastructure.Repositories;

/// <summary>Stores append-only notes in MongoDB without a demo fallback.</summary>
public sealed class MedicalNoteRepository : IMedicalNoteRepository
{
    private readonly IMongoCollection<MedicalNote> _notes;

    public MedicalNoteRepository(IMongoDatabase database)
    {
        _notes = database.GetCollection<MedicalNote>("medical_notes");

        _notes.Indexes.CreateOne(new CreateIndexModel<MedicalNote>(
            Builders<MedicalNote>.IndexKeys.Ascending(x => x.RecordId)
                .Descending(x => x.CreatedAt).Descending(x => x.Id)));
    }

    public async Task<MedicalNote> InsertOrGetAsync(MedicalNote note, CancellationToken ct = default)
    {
        try
        {
            await _notes.InsertOneAsync(note, cancellationToken: ct);
            return note;
        }
        catch (MongoWriteException ex) when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey)
        {
            return await _notes.Find(x => x.Id == note.Id).FirstAsync(ct);
        }
    }

    public async Task<(List<MedicalNote> Items, long Total)> ListAsync(
        string recordId, int page, int pageSize, CancellationToken ct = default)
    {
        var filter = Builders<MedicalNote>.Filter.Eq(x => x.RecordId, recordId);
        var total = await _notes.CountDocumentsAsync(filter, cancellationToken: ct);
        var items = await _notes.Find(filter).SortByDescending(x => x.CreatedAt)
            .ThenByDescending(x => x.Id).Skip((page - 1) * pageSize).Limit(pageSize).ToListAsync(ct);
        return (items, total);
    }
}
