using BMS.Application.DTOs.Clinic;

namespace BMS.Application.Interfaces;

/// <summary>UC51 use cases scoped to the authenticated treating doctor.</summary>
public interface IMedicalNoteService
{
    /// <summary>Loads an existing EMR and its supplementary notes after ownership validation.</summary>
    Task<MedicalNoteContextDto> GetContextAsync(string recordId, string doctorId, int page, int pageSize, CancellationToken ct = default);
    /// <summary>Appends a note without modifying the original EMR or previous addenda.</summary>
    Task<MedicalNoteDto> AddAsync(CreateMedicalNoteDto request, string doctorId, string authorName, CancellationToken ct = default);
}
