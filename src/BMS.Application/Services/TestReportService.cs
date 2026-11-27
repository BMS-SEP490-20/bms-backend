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

        DateTime analysedTime = sample?.ReceivedAt ?? (sample?.CollectedAt != default && sample?.CollectedAt != null ? sample.CollectedAt : DateTime.UtcNow);

        // Load real APPROVED test parameters from test_results (enforce Doctor approval gate before publishing report)
        var parameters = new List<TestReportParameter>();
        TestResult? approvedResult = null;
        if (_resultsCol != null)
        {
            try
            {
                var candidates = await _resultsCol
                    .Find(r => !r.IsDeleted && !r.IsSuperseded &&
                        (r.SampleId == request.OrderId ||
                         r.AppointmentId == request.OrderId ||
                         (sample != null && (r.SampleId == sample.Id || r.SampleId == sample.Barcode))))
                    .SortByDescending(r => r.AnalysisRun)
                    .ThenByDescending(r => r.CreatedAt)
                    .ToListAsync();

                approvedResult = candidates.FirstOrDefault(r =>
                    r.ValidationStatus == ResultValidationStatus.DoctorApproved ||
                    string.Equals(r.Status, "APPROVED", StringComparison.OrdinalIgnoreCase) ||
                    !string.IsNullOrWhiteSpace(r.DigitalSignature));

                if (approvedResult == null)
                {
                    throw new InvalidOperationException("MSG-ERR-REPORT-NOT-APPROVED: Chỉ được phát hành phiếu kết quả sau khi Bác sĩ đã thẩm định và ký duyệt.");
                }

                if (approvedResult.Items != null)
                {
                    foreach (var item in approvedResult.Items)
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
            catch (InvalidOperationException)
            {
                throw;
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

        static string Enc(string? v) => System.Net.WebUtility.HtmlEncode(v ?? string.Empty);
        var clinicAddress = _config?["Clinic:Address"] ?? "120 Hai Bà Trưng, Phường Đa Kao, Quận 1, TP. Hồ Chí Minh";
        var hotline = _config?["Clinic:Hotline"] ?? "1900 6868";
        var issuedVn = reportDto.IssuedAt.ToUniversalTime().AddHours(7).ToString("HH:mm dd/MM/yyyy");
        var dobSexVn = (reportDto.DobAgeSex ?? string.Empty)
            .Replace("Male", "Nam", StringComparison.OrdinalIgnoreCase)
            .Replace("Female", "Nữ", StringComparison.OrdinalIgnoreCase);

        static string FlagVi(string? f) => (f ?? "N").ToUpperInvariant() switch
        {
            "HH" => "Nguy kịch (HH)",
            "H" => "Cao (H)",
            "LL" => "Nguy kịch (LL)",
            "L" => "Thấp (L)",
            _ => "Bình thường"
        };

        var html = $@"<!DOCTYPE html>
<html lang='vi'>
<head>
    <meta charset='utf-8'/>
    <title>Phiếu Kết Quả Xét Nghiệm - {Enc(reportDto.ReportId)}</title>
    <style>
        @page {{ size: A4 portrait; margin: 10mm 12mm; }}
        body {{ font-family: 'Segoe UI', Roboto, Arial, sans-serif; margin: 0; color: #0f172a; font-size: 11px; line-height: 1.4; -webkit-print-color-adjust: exact; print-color-adjust: exact; }}
        .header {{ display: flex; justify-content: space-between; border-bottom: 2px solid #0284c7; padding-bottom: 8px; margin-bottom: 10px; }}
        .logo {{ font-size: 14px; font-weight: 800; color: #0c4a6e; }}
        .sub {{ font-size: 10px; color: #475569; margin-top: 2px; }}
        .meta {{ text-align: right; font-size: 10px; color: #334155; }}
        .title {{ text-align: center; font-size: 16px; font-weight: 800; color: #0c4a6e; text-transform: uppercase; margin: 8px 0 4px; }}
        .pkg {{ text-align: center; font-size: 11.5px; font-weight: 600; color: #334155; margin-bottom: 10px; }}
        .grid {{ display: grid; grid-template-columns: 1fr 1fr; gap: 4px 16px; font-size: 10.5px; background: #f8fafc; border: 1px solid #cbd5e1; border-radius: 6px; padding: 8px 12px; margin-bottom: 10px; }}
        .table {{ width: 100%; border-collapse: collapse; margin-bottom: 10px; font-size: 10.5px; }}
        .table th, .table td {{ border: 1px solid #e2e8f0; padding: 5px 7px; text-align: left; }}
        .table th {{ background: #e0f2fe; color: #0c4a6e; font-size: 10px; }}
        .flag {{ font-weight: 700; color: #15803d; }}
        .flag-abn {{ font-weight: 700; color: #dc2626; }}
        .notes {{ background: #f0f9ff; border: 1px solid #bae6fd; border-left: 3.5px solid #0284c7; border-radius: 4px; padding: 8px 11px; margin-bottom: 12px; font-size: 10.5px; }}
        .signatures {{ display: flex; justify-content: space-between; margin-top: 12px; padding-top: 8px; border-top: 1px dashed #cbd5e1; font-size: 10.5px; text-align: center; }}
        .sig-col {{ width: 45%; }}
        .footer {{ font-size: 9px; color: #64748b; text-align: center; margin-top: 10px; border-top: 1px solid #e2e8f0; padding-top: 6px; }}
    </style>
</head>
<body>
    <div class='header'>
        <div>
            <div class='logo'>HỆ THỐNG PHÒNG XÉT NGHIỆM Y KHOA BMS MEDLAB</div>
            <div class='sub'>Địa chỉ: {Enc(clinicAddress)} · Hotline: {Enc(hotline)} · Chuẩn ISO 15189:2022</div>
        </div>
        <div class='meta'>
            <div>Mã phiếu KQ: <strong>{Enc(reportDto.ReportId)}</strong></div>
            <div>Thời điểm trả KQ: <strong>{Enc(issuedVn)}</strong></div>
        </div>
    </div>
    <div class='title'>Phiếu Kết Quả Xét Nghiệm Y Khoa</div>
    <div class='pkg'>Gói dịch vụ: {Enc(reportDto.PackageName)}</div>
    <div class='grid'>
        <div><strong>Họ và tên bệnh nhân:</strong> {Enc(reportDto.PatientName)}</div>
        <div><strong>Bác sĩ chỉ định:</strong> {Enc(reportDto.ReferringDoctor)}</div>
        <div><strong>Mã bệnh nhân:</strong> {Enc(reportDto.PatientId)}</div>
        <div><strong>Mã chỉ định:</strong> {Enc(reportDto.OrderId)}</div>
        <div><strong>Ngày sinh / Giới tính:</strong> {Enc(dobSexVn)}</div>
        <div><strong>Mã ống bệnh phẩm:</strong> {Enc(reportDto.SpecimenId)}</div>
        <div><strong>Thời điểm lấy mẫu:</strong> {Enc(reportDto.CollectionTime)}</div>
        <div><strong>Trạng thái:</strong> Đã Bác sĩ phê duyệt</div>
    </div>
    <table class='table'>
        <thead><tr><th>STT</th><th>Tên Chỉ Số Xét Nghiệm</th><th>Kết Quả</th><th>Đơn Vị</th><th>Khoảng Tham Chiếu</th><th>Đánh Giá</th></tr></thead>
        <tbody>
            {string.Join("", reportDto.Parameters.Select((p, idx) => $"<tr><td>{idx + 1}</td><td><strong>{Enc(p.ParameterName)}</strong></td><td><strong>{Enc(p.ResultValue)}</strong></td><td>{Enc(p.Unit)}</td><td>{Enc(p.ReferenceRange)}</td><td class='{(p.IsAbnormal ? "flag-abn" : "flag")}'>{Enc(FlagVi(p.Flag))}</td></tr>"))}
        </tbody>
    </table>
    <div class='notes'><strong>Kết luận lâm sàng của Bác sĩ:</strong><br/>{Enc(reportDto.ClinicalNotes)}</div>
    <div class='signatures'>
        <div class='sig-col'><strong>KỸ THUẬT VIÊN XÉT NGHIỆM</strong><br/><br/><strong>{Enc(reportDto.AnalysedBy)}</strong></div>
        <div class='sig-col'><strong>BÁC SĨ CHUYÊN KHOA PHÊ DUYỆT</strong><br/><br/><strong>{Enc(reportDto.ApprovedBy)}</strong></div>
    </div>
    <div class='footer'>Phiếu kết quả phát hành điện tử theo tiêu chuẩn ISO 15189:2022 · Mã xác thực SHA-256: {Enc(reportDto.VerificationHash)}</div>
</body>
</html>";

        return new ExportReportPdfResponseDto
        {
            ReportId = reportDto.ReportId,
            OrderId = reportDto.OrderId,
            PatientName = reportDto.PatientName,
            FileName = $"Phieu-Ket-Qua-XN-{reportDto.ReportId}.pdf",
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
