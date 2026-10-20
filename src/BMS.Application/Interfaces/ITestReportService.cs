using System.Collections.Generic;
using System.Threading.Tasks;
using BMS.Application.DTOs.Laboratory;

namespace BMS.Application.Interfaces;

/// <summary>
/// Service contract managing clinical laboratory test report compilation, verification, and PDF generation (UC42).
/// </summary>
public interface ITestReportService
{
    Task<TestReportDto?> GetReportByOrderIdAsync(string orderId);
    Task<TestReportDto?> GetReportByIdAsync(string id);
    Task<IEnumerable<TestReportDto>> GetReportsByPatientIdAsync(string patientId);
    Task<TestReportDto> GenerateReportAsync(GenerateTestReportRequestDto request, string? currentUserId = null);
    Task<ExportReportPdfResponseDto?> ExportReportPdfAsync(string orderId);
}
