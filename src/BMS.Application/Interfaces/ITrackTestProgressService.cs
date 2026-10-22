using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BMS.Application.DTOs;

namespace BMS.Application.Interfaces;

/// <summary>
/// Service contract for tracking test progress throughout the specimen supply chain (UC48).
/// </summary>
public interface ITrackTestProgressService
{
    /// <summary>
    /// Tracks progress by OrderNumber, Barcode, or Sample ID.
    /// In public queries (e.g. unauthenticated patient portal), patient names are masked according to HIPAA.
    /// </summary>
    Task<TrackProgressResponse?> GetTrackingAsync(string identifier, bool isPublicQuery = true, CancellationToken ct = default);

    /// <summary>
    /// Retrieves active tracking progress records for a logged-in patient.
    /// </summary>
    Task<List<TrackProgressResponse>> GetPatientActiveTrackingsAsync(string patientId, CancellationToken ct = default);

    /// <summary>
    /// Searches and filters recent sample progress records for clinical staff.
    /// </summary>
    Task<List<TrackProgressResponse>> SearchTrackingsAsync(string? query, string? status, DateTime? fromDate, DateTime? toDate, int limit = 50, CancellationToken ct = default);
}
