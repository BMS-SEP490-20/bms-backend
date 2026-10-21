using BMS.Core.Entities;

namespace BMS.Core.Interfaces;

/// <summary>Persists UC51 addenda without replacing the source medical record.</summary>
public interface IMedicalNoteRepository
{
    /// <summary>Inserts a note, or returns the stored note when its submission ID already exists.</summary>
    Task<MedicalNote> InsertOrGetAsync(MedicalNote note, CancellationToken ct = default);
    /// <summary>Returns a bounded, stable page and the matching count for one record.</summary>
    Task<(List<MedicalNote> Items, long Total)> ListAsync(string recordId, int page, int pageSize, CancellationToken ct = default);
}
