using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BMS.Application.DTOs.Clinic;

namespace BMS.Application.Interfaces;

/// <summary>
/// Service interface for Medical Assessment operations (UC50 / Iteration 3).
/// </summary>
public interface IMedicalAssessmentService
{
    Task<MedicalAssessmentDto> CreateAsync(CreateMedicalAssessmentDto dto, string currentUserId, string? currentUserName, CancellationToken ct = default);
    Task<MedicalAssessmentDto?> GetByIdAsync(string id, string? currentUserId, string? currentUserRole, CancellationToken ct = default);
    Task<List<MedicalAssessmentDto>> GetByPatientIdAsync(string patientId, string? currentUserId, string? currentUserRole, CancellationToken ct = default);
    Task<List<PatientEncounterDto>> GetPatientEncountersAsync(string patientId, CancellationToken ct = default);
    Task<(List<MedicalAssessmentDto> Items, int TotalCount)> QueryAsync(MedicalAssessmentFilterDto filter, string? currentUserId, string? currentUserRole, CancellationToken ct = default);
    Task<MedicalAssessmentDto> UpdateAsync(string id, UpdateMedicalAssessmentDto dto, string currentUserId, string? currentUserName, CancellationToken ct = default);
    Task<MedicalAssessmentDto> FinalizeAsync(string id, FinalizeMedicalAssessmentDto dto, string currentUserId, string? currentUserName, CancellationToken ct = default);
    Task<bool> DeleteAsync(string id, string deleteReason, string currentUserId, CancellationToken ct = default);
}
