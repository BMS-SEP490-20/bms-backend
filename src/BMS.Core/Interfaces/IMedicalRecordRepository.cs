using BMS.Core.Entities;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BMS.Core.Interfaces;

/// <summary>
/// Repository interface for Electronic Medical Records (EMR).
/// </summary>
public interface IMedicalRecordRepository
{
    Task<List<MedicalRecord>> GetByPatientIdAsync(string patientId, CancellationToken ct = default);
    Task<MedicalRecord?> GetByIdAsync(string id, CancellationToken ct = default);
    Task<MedicalRecord?> GetByRecordCodeAsync(string recordCode, CancellationToken ct = default);
    Task<string> GenerateNextRecordCodeAsync(DateTime date, CancellationToken ct = default);
    Task InsertAsync(MedicalRecord record, CancellationToken ct = default);
    Task UpdateAsync(MedicalRecord record, CancellationToken ct = default);
    Task<bool> SoftDeleteAsync(string id, string deletedBy, string deleteReason, CancellationToken ct = default);
}
