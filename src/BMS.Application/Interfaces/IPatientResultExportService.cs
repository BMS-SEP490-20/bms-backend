using System.Collections.Generic;
using System.Threading.Tasks;
using BMS.Application.DTOs.Laboratory;

namespace BMS.Application.Interfaces;

/// <summary>
/// Service interface for patient portal diagnostic result retrieval, PDF export, and secure doctor sharing (UC62).
/// </summary>
public interface IPatientResultExportService
{
    /// <summary>
    /// Retrieves formatted patient test results summary including reference ranges, deltas, and doctor assessment.
    /// </summary>
    Task<PatientTestResultDetailDto?> GetPatientResultDetailAsync(string orderId);

    /// <summary>
    /// Generates printable HTML and PDF download metadata for a patient test result.
    /// </summary>
    Task<ExportPatientPdfResponseDto?> ExportResultPdfAsync(string orderId);

    /// <summary>
    /// Generates a time-limited secure link to share results with an external consulting doctor.
    /// </summary>
    Task<ShareResultResponseDto> GenerateShareLinkAsync(string orderId);

    /// <summary>
    /// Retrieves all released test results for a specific patient.
    /// </summary>
    Task<IEnumerable<PatientTestResultDetailDto>> GetResultsByPatientIdAsync(string patientId);
}
