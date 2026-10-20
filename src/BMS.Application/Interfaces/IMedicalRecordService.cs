using BMS.Application.DTOs.MedicalRecords;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BMS.Application.Interfaces;

/// <summary>
/// UC33 – Medical Records (EMR) Service interface.
/// </summary>
public interface IMedicalRecordService
{
    Task<List<MedicalRecordDto>> GetByPatientIdAsync(string patientId, string? currentUserId, string? currentUserRole, CancellationToken ct = default);
    Task<MedicalRecordDto?> GetByIdAsync(string id, string? currentUserId, string? currentUserRole, CancellationToken ct = default);
    Task<MedicalRecordDto> CreateAsync(CreateMedicalRecordDto dto, string doctorUserId, CancellationToken ct = default);
    Task<MedicalRecordDto> UpdateAsync(string id, UpdateMedicalRecordDto dto, string doctorUserId, CancellationToken ct = default);
    Task<bool> DeleteAsync(string id, DeleteMedicalRecordRequest request, string doctorUserId, CancellationToken ct = default);
}
