using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BMS.Core.Entities;
using BMS.Core.Interfaces;
using BMS.Infrastructure.Data;
using MongoDB.Driver;

namespace BMS.Infrastructure.Repositories;

/// <summary>
/// Repository implementation for laboratory test notes (UC52 / Add Test Notes).
/// Interacts with MongoDB collection <c>test_notes</c> with thread-safe in-memory synchronization.
/// </summary>
public class TestNoteRepository : ITestNoteRepository
{
    private readonly IMongoCollection<TestNote>? _collection;
    private readonly BmsDbContext _dbContext;

    public TestNoteRepository(IMongoDatabase? database, BmsDbContext dbContext)
    {
        _dbContext = dbContext;
        if (database != null)
        {
            try
            {
                _collection = database.GetCollection<TestNote>("test_notes");
            }
            catch
            {
                _collection = null;
            }
        }
    }

    public async Task<TestNote> CreateAsync(TestNote note)
    {
        if (string.IsNullOrWhiteSpace(note.Id))
        {
            note.Id = Guid.NewGuid().ToString();
        }
        if (note.CreatedAt == default)
        {
            note.CreatedAt = DateTime.UtcNow;
        }

        if (_collection != null)
        {
            try
            {
                await _collection.InsertOneAsync(note);
            }
            catch
            {
                // Fall back to in-memory store
            }
        }

        lock (_dbContext.TestNotes)
        {
            _dbContext.TestNotes.Add(note);
        }

        return note;
    }

    public async Task<TestNote?> GetByIdAsync(string id)
    {
        if (_collection != null)
        {
            try
            {
                var note = await _collection.Find(n => n.Id == id).FirstOrDefaultAsync();
                if (note != null) return note;
            }
            catch
            {
                // Fall back to in-memory store
            }
        }

        lock (_dbContext.TestNotes)
        {
            return _dbContext.TestNotes.FirstOrDefault(n => n.Id == id);
        }
    }

    public async Task<List<TestNote>> GetBySpecimenAsync(string specimenIdOrBarcode)
    {
        if (string.IsNullOrWhiteSpace(specimenIdOrBarcode))
        {
            return new List<TestNote>();
        }

        if (_collection != null)
        {
            try
            {
                var filter = Builders<TestNote>.Filter.Or(
                    Builders<TestNote>.Filter.Eq(n => n.SpecimenId, specimenIdOrBarcode),
                    Builders<TestNote>.Filter.Eq(n => n.SampleBarcode, specimenIdOrBarcode),
                    Builders<TestNote>.Filter.Eq(n => n.SampleId, specimenIdOrBarcode)
                );
                var notes = await _collection.Find(filter)
                    .SortByDescending(n => n.CreatedAt)
                    .ToListAsync();
                if (notes.Count > 0) return notes;
            }
            catch
            {
                // Fall back to in-memory store
            }
        }

        lock (_dbContext.TestNotes)
        {
            return _dbContext.TestNotes
                .Where(n => string.Equals(n.SpecimenId, specimenIdOrBarcode, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(n.SampleBarcode, specimenIdOrBarcode, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(n.SampleId, specimenIdOrBarcode, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(n => n.CreatedAt)
                .ToList();
        }
    }

    public async Task<List<TestNote>> GetAllAsync()
    {
        if (_collection != null)
        {
            try
            {
                var notes = await _collection.Find(FilterDefinition<TestNote>.Empty)
                    .SortByDescending(n => n.CreatedAt)
                    .ToListAsync();
                if (notes.Count > 0) return notes;
            }
            catch
            {
                // Fall back to in-memory store
            }
        }

        lock (_dbContext.TestNotes)
        {
            return _dbContext.TestNotes.OrderByDescending(n => n.CreatedAt).ToList();
        }
    }
}
