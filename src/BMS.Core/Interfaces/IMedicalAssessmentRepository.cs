using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BMS.Core.Entities;

namespace BMS.Core.Interfaces;

/// <summary>
/// Interface for Medical Assessment data repository (UC50 / Iteration 3).
/// </summary>
public interface IMedicalAssessmentRepository
{
    Task<MedicalAssessment> CreateAsync(MedicalAssessment assessment, CancellationToken ct = default);
    Task<MedicalAssessment?> GetByIdAsync(string id, CancellationToken ct = default);
    Task<MedicalAssessment?> GetByCodeAsync(string code, CancellationToken ct = default);
    Task<List<MedicalAssessment>> GetByPatientIdAsync(string patientId, CancellationToken ct = default);
    Task<List<MedicalAssessment>> GetByEncounterIdAsync(string encounterId, CancellationToken ct = default);
    Task<(List<MedicalAssessment> Items, int TotalCount)> QueryAsync(
        string? patientId = null,
        string? doctorId = null,
        string? status = null,
        DateTime? fromDate = null,
        DateTime? toDate = null,
        string? keyword = null,
        int skip = 0,
        int limit = 20,
        CancellationToken ct = default);
    Task<MedicalAssessment> UpdateAsync(MedicalAssessment assessment, CancellationToken ct = default);
    Task<bool> SoftDeleteAsync(string id, string deletedBy, string deleteReason, CancellationToken ct = default);
    Task<string> GenerateNextAssessmentCodeAsync(DateTime date, CancellationToken ct = default);
}
