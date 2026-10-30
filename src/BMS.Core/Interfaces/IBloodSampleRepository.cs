using BMS.Core.Entities;

namespace BMS.Core.Interfaces;

/// <summary>
/// Data access for the <c>TestOrder/BloodSample</c> API (Print Barcodes #58, Validate Sample #73)
/// over the shared <c>blood_samples</c> collection. Soft-deleted samples are never returned.
/// </summary>
public interface IBloodSampleRepository
{
    /// <summary>Returns one page of samples (newest first) and the total count.</summary>
    Task<(IReadOnlyList<Sample> Items, long Total)> GetPagedAsync(int page, int pageSize, CancellationToken ct = default);

    /// <summary>Finds a sample by id or barcode.</summary>
    Task<Sample?> GetByIdAsync(string id, CancellationToken ct = default);

    /// <summary>Returns all samples of an appointment / test order.</summary>
    Task<IReadOnlyList<Sample>> GetByAppointmentIdAsync(string appointmentId, CancellationToken ct = default);

    /// <summary>True when any sample (including deleted) already uses the barcode.</summary>
    Task<bool> BarcodeExistsAsync(string barcode, CancellationToken ct = default);

    /// <summary>Inserts a new sample.</summary>
    Task CreateAsync(Sample entity, CancellationToken ct = default);

    /// <summary>
    /// Atomically applies field changes with <c>$set</c> and, when <paramref name="history"/> is given,
    /// appends it to <c>status_history</c> with <c>$push</c> in the same write. Returns false when not found.
    /// </summary>
    Task<bool> UpdateFieldsAsync(string id, SampleFieldChanges changes, SampleStatusHistoryEntry? history, CancellationToken ct = default);

    /// <summary>Soft-deletes a sample (<c>is_deleted = true</c>). Returns false when not found.</summary>
    Task<bool> SoftDeleteAsync(string id, CancellationToken ct = default);
}

/// <summary>Optional field changes for <see cref="IBloodSampleRepository.UpdateFieldsAsync"/>; null means unchanged.</summary>
public sealed record SampleFieldChanges(
    string? SampleType = null,
    DateTime? CollectedAt = null,
    double? VolumeMl = null,
    string? Status = null,
    string? StorageLocation = null,
    string? Notes = null,
    string? AssignedTechnicianId = null,
    string? RejectionReason = null,
    DateTime? ReceivedAt = null,
    string? ReceivedBy = null,
    DateTime? RejectedAt = null);
