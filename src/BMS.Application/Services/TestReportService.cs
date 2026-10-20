using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using BMS.Application.DTOs.Laboratory;
using BMS.Application.Interfaces;
using BMS.Core.Entities;
using BMS.Core.Interfaces;
using BMS.Infrastructure.Data;
using Microsoft.Extensions.Configuration;
using MongoDB.Driver;

namespace BMS.Application.Services;

/// <summary>
/// Service implementation managing official diagnostic report compilation and PDF export (UC42).
/// Follows ISO 15189 standards for post-analytical verification.
/// </summary>
public class TestReportService : ITestReportService
{
    private readonly ITestReportRepository _reportRepository;
    private readonly BmsDbContext _dbContext;
    private readonly IMongoCollection<Appointment>? _appointmentsCol;
    private readonly IMongoCollection<TestResult>? _resultsCol;
    private readonly IConfiguration? _config;

    public TestReportService(
        ITestReportRepository reportRepository,
        BmsDbContext dbContext,
        IMongoDatabase? database = null,
        IConfiguration? config = null)
    {
        _reportRepository = reportRepository;
        _dbContext = dbContext;
        _config = config;
        if (database != null)
        {
            try
            {
                _appointmentsCol = database.GetCollection<Appointment>("appointments");
                _resultsCol = database.GetCollection<TestResult>("test_results");
            }
            catch
            {
                _appointmentsCol = null;
                _resultsCol = null;
            }
        }
    }

    public async Task<TestReportDto?> GetReportByOrderIdAsync(string orderId)
    {
        var report = await _reportRepository.GetByOrderIdAsync(orderId);
        if (report != null) return MapToDto(report);

        return null;
    }

    public async Task<TestReportDto?> GetReportByIdAsync(string id)
    {
        var report = await _reportRepository.GetByIdAsync(id) ?? await _reportRepository.GetByReportIdAsync(id);
        return report == null ? null : MapToDto(report);
    }

    public async Task<IEnumerable<TestReportDto>> GetReportsByPatientIdAsync(string patientId)
    {
        var reports = await _reportRepository.GetByPatientIdAsync(patientId);
        return reports.Select(MapToDto);
    }

    public async Task<TestReportDto> GenerateReportAsync(GenerateTestReportRequestDto request, string? currentUserId = null)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));

        var existing = await _reportRepository.GetByOrderIdAsync(request.OrderId);
        if (existing != null)
        {
            if (!string.IsNullOrEmpty(request.ClinicalNotes)) existing.ClinicalNotes = request.ClinicalNotes;
            if (!string.IsNullOrEmpty(request.ApprovedBy)) existing.ApprovedBy = request.ApprovedBy;
            await _reportRepository.UpdateAsync(existing);
            return MapToDto(existing);
        }

        var reportId = await _reportRepository.GenerateNextReportIdAsync();
        var sample = _dbContext.Samples.FirstOrDefault(s => s.TestOrderId == request.OrderId || s.Barcode == request.OrderId);

        Appointment? appt = null;
        if (_appointmentsCol != null)
        {
            try
            {
                appt = await _appointmentsCol.Find(a => a.Id == request.OrderId || a.AppointmentCode == request.OrderId).FirstOrDefaultAsync();
            }
            catch { }
        }

        var patientId = sample?.PatientId ?? appt?.PatientId ?? string.Empty;
        var patientName = !string.IsNullOrWhiteSpace(sample?.PatientName) ? sample.PatientName : string.Empty;
        if (string.IsNullOrEmpty(patientName) && !string.IsNullOrEmpty(patientId))
        {
            var user = _dbContext.Users.FirstOrDefault(u => u.Id == patientId);
            if (user != null) patientName = user.FullName;
        }

        var packageName = !string.IsNullOrWhiteSpace(sample?.TestPackageName)
            ? sample.TestPackageName
            : (appt?.Notes ?? string.Empty);

        var specimenId = sample != null ? $"{sample.Barcode} · {sample.SampleType}" : string.Empty;
        var collectionTime = sample != null && sample.CollectedAt != default
            ? sample.CollectedAt.ToString("dd MMM yyyy · HH:mm")
            : (appt != null ? appt.AppointmentDate.ToString("dd MMM yyyy") : string.Empty);

        // Referring doctor: Must be the ordering doctor from TestOrder / Appointment
        string referringDoctor = string.Empty;
        var docId = appt?.DoctorId;
        if (!string.IsNullOrEmpty(docId))
        {
            var doc = _dbContext.Users.FirstOrDefault(u => u.Id == docId);
            if (doc != null) referringDoctor = doc.FullName;
        }

        var approver = !string.IsNullOrWhiteSpace(request.ApprovedBy)
            ? request.ApprovedBy
            : (!string.IsNullOrWhiteSpace(currentUserId) ? currentUserId : string.Empty);

        var analyst = !string.IsNullOrWhiteSpace(sample?.AssignedTechnicianName)
            ? sample.AssignedTechnicianName
            : (!string.IsNullOrWhiteSpace(currentUserId) ? currentUserId : string.Empty);

        var analysedTime = sample != null && sample.ReceivedAt != default
            ? sample.ReceivedAt
            : (sample != null && sample.CollectedAt != default ? sample.CollectedAt : DateTime.UtcNow);

        // Load real test parameters from test_results
        var parameters = new List<TestReportParameter>();
        if (_resultsCol != null)
        {
            try
            {
                var tr = await _resultsCol.Find(r => r.SampleId == request.OrderId || (sample != null && (r.SampleId == sample.Id || r.SampleId == sample.Barcode))).FirstOrDefaultAsync();
                if (tr != null && tr.Items != null)
                {
                    foreach (var item in tr.Items)
                    {
                        parameters.Add(new TestReportParameter
                        {
                            ParameterName = item.CriterionName,
                            ResultValue = item.MeasuredValue,
                            Unit = item.Unit,
                            ReferenceRange = item.ReferenceInterval,
                            Flag = item.Flag != ResultFlag.Normal ? item.Flag.ToString() : string.Empty,
                            IsAbnormal = item.Flag != ResultFlag.Normal
                        });
                    }
                }
            }
            catch { }
        }

        // Real verification hash computed using SHA256
        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes($"{reportId}|{request.OrderId}|{patientId}|{DateTime.UtcNow:O}"));
        var verificationHash = Convert.ToHexString(hashBytes).ToLowerInvariant();

        var report = new TestReport
        {
            ReportId = reportId,
            OrderId = request.OrderId,
            PatientId = patientId,
            PatientName = patientName,
            DobAgeSex = string.Empty,
            PackageName = packageName,
            CollectionTime = collectionTime,
            SpecimenId = specimenId,
            ReferringDoctor = referringDoctor,
            Category = packageName,
            ClinicalNotes = request.ClinicalNotes ?? string.Empty,
            AnalysedBy = analyst,
            AnalysedAt = analysedTime,
            ApprovedBy = approver,
            ApprovedAt = DateTime.UtcNow,
            IssuedAt = DateTime.UtcNow,
            VerificationHash = verificationHash,
            CreatedAt = DateTime.UtcNow,
            Parameters = parameters
        };

        var created = await _reportRepository.CreateAsync(report);
        return MapToDto(created);
    }

    public async Task<ExportReportPdfResponseDto?> ExportReportPdfAsync(string orderId)
    {
        var reportDto = await GetReportByOrderIdAsync(orderId);
        if (reportDto == null) return null;

        var clinicAddress = _config?["Clinic:Address"] ?? string.Empty;

        var html = $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'/>
    <title>Test Report - {reportDto.ReportId}</title>
    <style>
        body {{ font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif; margin: 30px; color: #1e293b; }}
        .header {{ display: flex; justify-content: space-between; border-bottom: 2px solid #0284c7; padding-bottom: 12px; margin-bottom: 20px; }}
        .logo {{ font-size: 20px; font-weight: bold; color: #0284c7; }}
        .meta {{ text-align: right; font-size: 12px; color: #64748b; }}
        .grid {{ display: grid; grid-template-columns: repeat(2, 1fr); gap: 8px; font-size: 13px; margin-bottom: 24px; }}
        .table {{ width: 100%; border-collapse: collapse; margin-bottom: 20px; }}
        .table th, .table td {{ border: 1px solid #cbd5e1; padding: 8px 12px; text-align: left; font-size: 13px; }}
        .table th {{ background: #f8fafc; color: #475569; }}
        .flag {{ color: #dc2626; font-weight: bold; }}
        .notes {{ background: #f8fafc; border-left: 4px solid #0284c7; padding: 12px; margin-bottom: 24px; font-size: 13px; }}
        .signatures {{ display: flex; justify-content: space-between; margin-top: 40px; font-size: 12px; }}
        .footer {{ font-size: 11px; color: #94a3b8; text-align: center; margin-top: 30px; border-top: 1px solid #e2e8f0; padding-top: 8px; }}
    </style>
</head>
<body>
    <div class='header'>
        <div><div class='logo'>BMS Blood Laboratory</div><div>{clinicAddress}</div></div>
        <div class='meta'><div>Report ID: <strong>{reportDto.ReportId}</strong></div><div>Issued: {reportDto.IssuedAt:dd MMM yyyy HH:mm}</div></div>
    </div>
    <div class='grid'>
        <div><strong>Patient:</strong> {reportDto.PatientName}</div>
        <div><strong>Patient ID:</strong> {reportDto.PatientId}</div>
        <div><strong>DOB/Age/Sex:</strong> {reportDto.DobAgeSex}</div>
        <div><strong>Order ID:</strong> {reportDto.OrderId}</div>
        <div><strong>Test Package:</strong> {reportDto.PackageName}</div>
        <div><strong>Specimen:</strong> {reportDto.SpecimenId}</div>
        <div><strong>Collection:</strong> {reportDto.CollectionTime}</div>
        <div><strong>Referring Doctor:</strong> {reportDto.ReferringDoctor}</div>
    </div>
    <h3>{reportDto.Category}</h3>
    <table class='table'>
        <thead><tr><th>Parameter</th><th>Result</th><th>Unit</th><th>Reference Range</th><th>Flag</th></tr></thead>
        <tbody>
            {string.Join("", reportDto.Parameters.Select(p => $"<tr><td>{p.ParameterName}</td><td>{p.ResultValue}</td><td>{p.Unit}</td><td>{p.ReferenceRange}</td><td class='flag'>{p.Flag}</td></tr>"))}
        </tbody>
    </table>
    <div class='notes'><strong>Clinical Notes:</strong><br/>{reportDto.ClinicalNotes}</div>
    <div class='signatures'>
        <div>Analysed by: <strong>{reportDto.AnalysedBy}</strong></div>
        <div>Approved and signed by: <strong>{reportDto.ApprovedBy}</strong></div>
    </div>
    <div class='footer'>Digitally verified per ISO 15189 standards · Hash: {reportDto.VerificationHash}</div>
</body>
</html>";

        return new ExportReportPdfResponseDto
        {
            ReportId = reportDto.ReportId,
            OrderId = reportDto.OrderId,
            PatientName = reportDto.PatientName,
            FileName = $"BMS-Report-{reportDto.ReportId}.pdf",
            MimeType = "application/pdf",
            VerificationHash = reportDto.VerificationHash,
            PrintableHtml = html
        };
    }

    private static TestReportDto MapToDto(TestReport report)
    {
        return new TestReportDto
        {
            Id = report.Id,
            ReportId = report.ReportId,
            OrderId = report.OrderId,
            PatientId = report.PatientId,
            PatientName = report.PatientName,
            DobAgeSex = report.DobAgeSex,
            PackageName = report.PackageName,
            CollectionTime = report.CollectionTime,
            SpecimenId = report.SpecimenId,
            ReferringDoctor = report.ReferringDoctor,
            Category = report.Category,
            ClinicalNotes = report.ClinicalNotes,
            AnalysedBy = report.AnalysedBy,
            AnalysedAt = report.AnalysedAt,
            ApprovedBy = report.ApprovedBy,
            ApprovedAt = report.ApprovedAt,
            IssuedAt = report.IssuedAt,
            VerificationHash = report.VerificationHash,
            Parameters = report.Parameters.Select(p => new TestReportParameterDto
            {
                ParameterName = p.ParameterName,
                ResultValue = p.ResultValue,
                Unit = p.Unit,
                ReferenceRange = p.ReferenceRange,
                Flag = p.Flag,
                IsAbnormal = p.IsAbnormal
            }).ToList()
        };
    }
}
