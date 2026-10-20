using System.Collections.Generic;
using System.Threading.Tasks;
using BMS.Application.DTOs.Laboratory;

namespace BMS.Application.Interfaces;

/// <summary>
/// Service interface for laboratory test notes operations (UC52 / Add Test Notes).
/// </summary>
public interface ITestNoteService
{
    Task<TestNoteDto> CreateNoteAsync(CreateTestNoteRequestDto request, string? userId, string? userName, string? userRole);
    Task<SpecimenNoteContextDto?> GetSpecimenContextAsync(string specimenBarcodeOrId);
    Task<List<TestNoteDto>> GetNotesBySpecimenAsync(string specimenBarcodeOrId);
}
