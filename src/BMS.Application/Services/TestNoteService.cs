using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BMS.Application.DTOs.Laboratory;
using BMS.Application.Interfaces;
using BMS.Core.Entities;
using BMS.Core.Interfaces;
using BMS.Infrastructure.Data;
using MongoDB.Driver;

namespace BMS.Application.Services;

/// <summary>
/// Service implementing clinical laboratory test notes business logic (UC52 / Add Test Notes).
/// Complies with ISO 15189 audit trail requirements and zero-mock-data rules.
/// </summary>
public class TestNoteService : ITestNoteService
{
    private readonly ITestNoteRepository _noteRepository;
    private readonly BmsDbContext _dbContext;
    private readonly IMongoCollection<Sample>? _samplesCol;

    public TestNoteService(
        ITestNoteRepository noteRepository,
        BmsDbContext dbContext,
        IMongoDatabase? database = null)
    {
        _noteRepository = noteRepository;
        _dbContext = dbContext;
        if (database != null)
        {
            try
            {
                _samplesCol = database.GetCollection<Sample>("blood_samples");
            }
            catch
            {
                _samplesCol = null;
            }
        }
    }

    public async Task<TestNoteDto> CreateNoteAsync(
        CreateTestNoteRequestDto request,
        string? userId,
        string? userName,
        string? userRole)
    {
        if (string.IsNullOrWhiteSpace(request.SpecimenId))
        {
            throw new ArgumentException("Specimen identifier is required.", nameof(request.SpecimenId));
        }

        if (string.IsNullOrWhiteSpace(request.Content))
        {
            throw new ArgumentException("Note content cannot be empty.", nameof(request.Content));
        }

        // Resolve specimen metadata dynamically from database
        var sample = await FindSampleAsync(request.SpecimenId);

        var note = new TestNote
        {
            Id = Guid.NewGuid().ToString(),
            SpecimenId = request.SpecimenId.Trim(),
            SampleId = sample?.Id,
            SampleBarcode = sample?.Barcode ?? request.SpecimenId.Trim(),
            PatientId = sample?.PatientId,
            PatientName = sample?.PatientName,
            TestPackageName = sample?.TestPackageName,
            NoteType = string.IsNullOrWhiteSpace(request.NoteType) ? "Technical observation" : request.NoteType.Trim(),
            Visibility = string.IsNullOrWhiteSpace(request.Visibility) ? "Internal" : request.Visibility.Trim(),
            IsInternalOnly = request.IsInternalOnly,
            PrintOnReport = request.PrintOnReport,
            Tags = request.Tags ?? new List<string>(),
            Content = request.Content.Trim(),
            CreatedByUserId = userId,
            CreatedByUserName = userName ?? "Lab Staff",
            CreatedByUserRole = userRole ?? "Lab Technician",
            CreatedAt = DateTime.UtcNow
        };

        var created = await _noteRepository.CreateAsync(note);
        return MapToDto(created);
    }

    public async Task<SpecimenNoteContextDto?> GetSpecimenContextAsync(string specimenBarcodeOrId)
    {
        if (string.IsNullOrWhiteSpace(specimenBarcodeOrId))
        {
            return null;
        }

        var sample = await FindSampleAsync(specimenBarcodeOrId);
        if (sample == null)
        {
            return null;
        }

        var notes = await _noteRepository.GetBySpecimenAsync(specimenBarcodeOrId);

        return new SpecimenNoteContextDto
        {
            SpecimenId = sample.Barcode,
            SampleBarcode = sample.Barcode,
            PatientId = sample.PatientId,
            PatientName = sample.PatientName,
            TestPackageName = sample.TestPackageName,
            Status = sample.StatusString,
            Priority = sample.PriorityString,
            LabRoom = sample.LabRoom,
            CollectorName = sample.CollectorName,
            ExistingNotes = notes.Select(MapToDto).ToList()
        };
    }

    public async Task<List<TestNoteDto>> GetNotesBySpecimenAsync(string specimenBarcodeOrId)
    {
        var notes = await _noteRepository.GetBySpecimenAsync(specimenBarcodeOrId);
        return notes.Select(MapToDto).ToList();
    }

    private async Task<Sample?> FindSampleAsync(string specimenBarcodeOrId)
    {
        var key = specimenBarcodeOrId.Trim();

        // 1. Check MongoDB blood_samples collection
        if (_samplesCol != null)
        {
            try
            {
                var filter = Builders<Sample>.Filter.Or(
                    Builders<Sample>.Filter.Eq(s => s.Barcode, key),
                    Builders<Sample>.Filter.Eq(s => s.Id, key)
                );
                var doc = await _samplesCol.Find(filter).FirstOrDefaultAsync();
                if (doc != null) return doc;
            }
            catch
            {
                // Fall through to in-memory check
            }
        }

        // 2. Check BmsDbContext in-memory store
        lock (_dbContext.Samples)
        {
            return _dbContext.Samples.FirstOrDefault(s =>
                string.Equals(s.Barcode, key, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(s.Id, key, StringComparison.OrdinalIgnoreCase));
        }
    }

    private static TestNoteDto MapToDto(TestNote n)
    {
        return new TestNoteDto
        {
            Id = n.Id,
            SpecimenId = n.SpecimenId,
            SampleId = n.SampleId,
            SampleBarcode = n.SampleBarcode,
            PatientId = n.PatientId,
            PatientName = n.PatientName,
            TestPackageName = n.TestPackageName,
            NoteType = n.NoteType,
            Visibility = n.Visibility,
            IsInternalOnly = n.IsInternalOnly,
            PrintOnReport = n.PrintOnReport,
            Tags = n.Tags ?? new List<string>(),
            Content = n.Content,
            CreatedByUserId = n.CreatedByUserId,
            CreatedByUserName = n.CreatedByUserName,
            CreatedByUserRole = n.CreatedByUserRole,
            CreatedAt = n.CreatedAt,
            UpdatedAt = n.UpdatedAt
        };
    }
}
