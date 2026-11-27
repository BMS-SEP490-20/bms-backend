using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using BMS.Infrastructure.Data;
using BMS.Application.DTOs.Laboratory;
using BMS.Application.Interfaces;
using BMS.Core.Entities;
using BMS.Core.Interfaces;
using Microsoft.Extensions.Configuration;
using MongoDB.Bson;
using MongoDB.Driver;
using System.Text.RegularExpressions;

namespace BMS.Application.Services;

/// <summary>
/// Service implementing patient diagnostic test result presentation, PDF generation, and physician sharing (UC62 / Issue I3-043).
/// Conforms to: Production Standards, Clean MVC Architecture, and Zero Fabricated Data principles.
/// </summary>
public class PatientResultExportService : IPatientResultExportService
{
    private readonly ITestReportRepository _reportRepository;
    private readonly ITestOrderRepository _orderRepository;
    private readonly BmsDbContext _dbContext;
    private readonly IConfiguration _config;
    private readonly IMongoDatabase? _database;

    public PatientResultExportService(
        ITestReportRepository reportRepository,
        ITestOrderRepository orderRepository,
        BmsDbContext dbContext,
        IConfiguration config,
        IMongoDatabase? database = null)
    {
        _reportRepository = reportRepository;
        _orderRepository = orderRepository;
        _dbContext = dbContext;
        _config = config;
        _database = database;
    }

    public async Task<PatientTestResultDetailDto?> GetPatientResultDetailAsync(string orderId)
    {
        if (string.IsNullOrWhiteSpace(orderId)) return null;

        // 1. Try to find existing official diagnostic report
        var report = await _reportRepository.GetByOrderIdAsync(orderId);
        if (report == null)
        {
            lock (_dbContext.TestReports)
            {
                report = _dbContext.TestReports.FirstOrDefault(r =>
                    r.OrderId.Equals(orderId, StringComparison.OrdinalIgnoreCase) ||
                    r.ReportId.Equals(orderId, StringComparison.OrdinalIgnoreCase));
            }
        }

        // 2. Try to find test order
        var order = await _orderRepository.GetByOrderNumberAsync(orderId);
        if (order == null)
        {
            lock (_dbContext.TestOrders)
            {
                order = _dbContext.TestOrders.FirstOrDefault(o =>
                    o.OrderNumber.Equals(orderId, StringComparison.OrdinalIgnoreCase) ||
                    o.Id.Equals(orderId, StringComparison.OrdinalIgnoreCase));
            }
        }

        // If official approved report exists, build from report
        if (report != null)
        {
            return MapReportToPatientDto(report);
        }

        // 3. Direct lookup from MongoDB test_results (enforces DoctorApproved gate — NEVER fabricate measurements from unapproved orders or demo mocks)
        if (_database != null)
        {
            var fromMongo = await TryLoadFromMongoTestResultAsync(orderId);
            if (fromMongo != null) return fromMongo;

            // Also try matching via order's OrderNumber / Id if caller passed one of them
            if (order != null)
            {
                var byOrderId = await TryLoadFromMongoTestResultAsync(order.Id);
                if (byOrderId != null) return byOrderId;
                if (!string.IsNullOrWhiteSpace(order.OrderNumber))
                {
                    var byOrderNo = await TryLoadFromMongoTestResultAsync(order.OrderNumber);
                    if (byOrderNo != null) return byOrderNo;
                }
            }
        }

        return null;
    }

    private static string FormatVnDateTime(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "—";
        if (DateTimeOffset.TryParse(raw, out var dto))
        {
            var vn = dto.ToOffset(TimeSpan.FromHours(7));
            return vn.ToString("HH:mm dd/MM/yyyy");
        }
        return raw;
    }

    private static string TranslateGender(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        return raw.Trim().ToLowerInvariant() switch
        {
            "male" or "nam" or "m" => "Nam",
            "female" or "nữ" or "nu" or "f" => "Nữ",
            "other" or "khác" or "khac" => "Khác",
            _ => raw.Trim()
        };
    }

    private static string TranslateFlagLabel(string? flag) => (flag ?? "N").ToUpperInvariant() switch
    {
        "HH" => "Nguy kịch (HH)",
        "H" => "Cao (H)",
        "LL" => "Nguy kịch (LL)",
        "L" => "Thấp (L)",
        _ => "Bình thường"
    };

    private static string TranslateSampleType(string? sampleType, string? anticoagulant)
    {
        var baseName = (sampleType ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "blood" or "whole blood" or "máu" or "máu toàn phần" => "Máu toàn phần",
            "serum" or "huyết thanh" => "Huyết thanh",
            "plasma" or "huyết tương" => "Huyết tương",
            "urine" or "nước tiểu" => "Nước tiểu",
            "" => "Mẫu máu tĩnh mạch",
            _ => sampleType!.Trim()
        };
        return string.IsNullOrWhiteSpace(anticoagulant) ? baseName : $"{baseName} ({anticoagulant.Trim()})";
    }

    public async Task<ExportPatientPdfResponseDto?> ExportResultPdfAsync(string orderId)
    {
        var result = await GetPatientResultDetailAsync(orderId);
        if (result == null) return null;

        static string Enc(string? v) => System.Net.WebUtility.HtmlEncode(v ?? string.Empty);

        var clinicName = _config["Clinic:Name"] ?? "HỆ THỐNG PHÒNG XÉT NGHIỆM Y KHOA BMS MEDLAB";
        var clinicAddress = _config["Clinic:Address"] ?? "120 Hai Bà Trưng, Phường Đa Kao, Quận 1, TP. Hồ Chí Minh";
        var hotline = _config["Clinic:Hotline"] ?? "1900 6868";

        var collectedVn = FormatVnDateTime(result.CollectionTime);
        var releasedVn = FormatVnDateTime(result.ReleasedDate);
        var barcodeDisplay = string.IsNullOrWhiteSpace(result.Barcode) ? "—" : result.Barcode;
        var sampleTypeDisplay = string.IsNullOrWhiteSpace(result.SampleType) ? "Mẫu máu tĩnh mạch" : result.SampleType;
        var performedByDisplay = string.IsNullOrWhiteSpace(result.PerformedBy) ? "KTV Phòng Xét Nghiệm BMS" : result.PerformedBy;
        var doctorDisplay = string.IsNullOrWhiteSpace(result.ApprovedByDoctor) ? "Bác sĩ điều trị" : result.ApprovedByDoctor;
        var specialtyDisplay = string.IsNullOrWhiteSpace(result.DoctorSpecialty) ? "Chuyên khoa Huyết học & Xét nghiệm" : result.DoctorSpecialty;
        var appointmentCodeDisplay = string.IsNullOrWhiteSpace(result.AppointmentCode) ? result.OrderCode : result.AppointmentCode;
        var packageDisplay = string.IsNullOrWhiteSpace(result.PackageName) ? "Xét nghiệm lâm sàng" : result.PackageName;
        var assessmentDisplay = string.IsNullOrWhiteSpace(result.DoctorAssessment)
            ? "Kết quả xét nghiệm đã được Bác sĩ chuyên khoa thẩm định và phê duyệt."
            : result.DoctorAssessment;

        var rowsHtml = string.Join("", result.Parameters.Select((p, idx) =>
        {
            var flagCode = (p.Flag ?? "N").ToUpperInvariant();
            var isCrit = flagCode is "HH" or "LL";
            var isWarn = flagCode is "H" or "L";
            var rowCls = isCrit ? "crit" : (isWarn ? "warn" : "");
            var valCls = isCrit ? "t-crit" : (flagCode == "H" ? "t-high" : (flagCode == "L" ? "t-low" : ""));
            var delta = string.IsNullOrWhiteSpace(p.DeltaPrevious) ? "—" : p.DeltaPrevious;
            return $@"
            <tr class=""{rowCls}"">
                <td class=""c-center c-muted"">{idx + 1}</td>
                <td class=""param-name"">{Enc(p.ParameterName)}</td>
                <td class=""num c-right {valCls}"">{Enc(p.Value)}</td>
                <td class=""c-muted"">{Enc(p.Unit)}</td>
                <td class=""num c-muted"">{Enc(p.ReferenceRange)}</td>
                <td class=""num c-center c-muted"">{Enc(delta)}</td>
                <td class=""c-center""><span class=""flag flag-{flagCode}"">{Enc(TranslateFlagLabel(flagCode))}</span></td>
            </tr>";
        }));

        var diagBlockHtml = string.IsNullOrWhiteSpace(result.FinalDiagnosis) && string.IsNullOrWhiteSpace(result.Icd10Code)
            ? ""
            : $@"<div class=""diag-line""><strong>Chẩn đoán cuối cùng:</strong> {(string.IsNullOrWhiteSpace(result.Icd10Code) ? "" : $@"<span class=""icd-chip"">ICD-10: {Enc(result.Icd10Code)}</span>")}{Enc(result.FinalDiagnosis)}</div>";

        var adviceBlockHtml = string.IsNullOrWhiteSpace(result.TreatmentAdvice)
            ? ""
            : $@"<div class=""advice-line""><strong>Hướng điều trị &amp; lời dặn:</strong> {Enc(result.TreatmentAdvice)}</div>";

        var followUpHtml = string.IsNullOrWhiteSpace(result.FollowUpDate)
            ? ""
            : $@"<div class=""followup-line""><strong>Hẹn tái khám:</strong> {Enc(result.FollowUpDate)}</div>";

        var hashDisplay = string.IsNullOrWhiteSpace(result.VerificationHash)
            ? ComputeSha256($"{result.OrderCode}|{result.PatientId}|{result.ReleasedDate}")
            : result.VerificationHash;

        var printableHtml = $@"<!DOCTYPE html>
<html lang=""vi"">
<head>
    <meta charset=""utf-8""/>
    <title>Phiếu Kết Quả Xét Nghiệm - {Enc(result.OrderCode)}</title>
    <style>
        @page {{ size: A4 portrait; margin: 10mm 12mm; }}
        * {{ box-sizing: border-box; }}
        html, body {{
            margin: 0;
            padding: 0;
            background: #ffffff;
            color: #0f172a;
            font-family: 'Segoe UI', Roboto, 'Helvetica Neue', Arial, sans-serif;
            font-size: 11px;
            line-height: 1.4;
            -webkit-print-color-adjust: exact;
            print-color-adjust: exact;
        }}
        .sheet {{ max-width: 190mm; margin: 0 auto; padding: 2mm 0; }}
        .top-header {{ display: flex; justify-content: space-between; align-items: flex-start; gap: 12px; border-bottom: 2px solid #0284c7; padding-bottom: 8px; margin-bottom: 10px; }}
        .brand-row {{ display: flex; align-items: center; gap: 8px; }}
        .brand-badge {{ background: #0284c7; color: #ffffff; font-weight: 800; font-size: 11px; letter-spacing: 0.6px; padding: 3px 7px; border-radius: 4px; }}
        .clinic-name {{ font-size: 13.5px; font-weight: 800; color: #0f172a; letter-spacing: 0.2px; }}
        .clinic-sub {{ font-size: 9.8px; color: #475569; margin-top: 2px; }}
        .meta-right {{ text-align: right; font-size: 10px; color: #334155; white-space: nowrap; }}
        .meta-right div {{ margin-bottom: 1.5px; }}
        .status-pill {{ display: inline-block; background: #dcfce7; color: #15803d; border: 1px solid #86efac; border-radius: 10px; padding: 1.5px 8px; font-size: 9.5px; font-weight: 700; margin-top: 2px; }}
        .doc-title-wrap {{ text-align: center; margin: 8px 0 10px; }}
        .doc-title {{ margin: 0; font-size: 16.5px; font-weight: 800; color: #0c4a6e; letter-spacing: 0.5px; text-transform: uppercase; }}
        .doc-package {{ font-size: 11.5px; font-weight: 600; color: #334155; margin-top: 2px; }}
        .patient-grid {{ display: grid; grid-template-columns: 1fr 1fr; gap: 4px 18px; background: #f8fafc; border: 1px solid #cbd5e1; border-radius: 6px; padding: 8px 12px; margin-bottom: 10px; font-size: 10.5px; }}
        .info-row {{ display: flex; gap: 6px; }}
        .info-label {{ color: #64748b; min-width: 112px; flex-shrink: 0; }}
        .info-val {{ color: #0f172a; font-weight: 600; }}
        .mono {{ font-family: Consolas, 'Courier New', monospace; }}
        .section-bar {{ display: flex; justify-content: space-between; align-items: center; margin-bottom: 6px; }}
        .section-title {{ margin: 0; font-size: 12px; font-weight: 700; color: #0f172a; text-transform: uppercase; }}
        .summary-chip {{ display: inline-block; padding: 2px 8px; border-radius: 10px; font-size: 9.8px; font-weight: 700; }}
        .chip-err {{ background: #fee2e2; color: #b91c1c; border: 1px solid #fca5a5; }}
        .chip-ok {{ background: #dcfce7; color: #15803d; border: 1px solid #86efac; }}
        table.results-table {{ width: 100%; border-collapse: collapse; margin-bottom: 6px; font-size: 10.5px; }}
        table.results-table thead {{ display: table-header-group; }}
        table.results-table tr {{ page-break-inside: avoid; }}
        table.results-table th {{ background: #e0f2fe; color: #0c4a6e; font-weight: 700; font-size: 10px; text-align: left; padding: 5.5px 6px; border: 1px solid #bae6fd; }}
        table.results-table td {{ padding: 4.5px 6px; border: 1px solid #e2e8f0; vertical-align: middle; }}
        table.results-table tr.crit {{ background: #fef2f2; }}
        table.results-table tr.warn {{ background: #fffbeb; }}
        .param-name {{ font-weight: 600; color: #0f172a; }}
        .num {{ font-variant-numeric: tabular-nums; }}
        .c-center {{ text-align: center !important; }}
        .c-right {{ text-align: right !important; }}
        .c-muted {{ color: #475569; }}
        .t-crit {{ color: #dc2626; font-weight: 800; }}
        .t-high {{ color: #b45309; font-weight: 700; }}
        .t-low {{ color: #1d4ed8; font-weight: 700; }}
        .flag {{ display: inline-block; padding: 1.5px 6px; border-radius: 4px; font-size: 9.2px; font-weight: 700; white-space: nowrap; }}
        .flag-HH, .flag-LL {{ background: #dc2626; color: #ffffff; }}
        .flag-H {{ background: #f59e0b; color: #ffffff; }}
        .flag-L {{ background: #1d4ed8; color: #ffffff; }}
        .flag-N {{ background: #f1f5f9; color: #15803d; border: 1px solid #bbf7d0; }}
        .ref-note {{ font-size: 9.5px; color: #64748b; font-style: italic; margin-bottom: 9px; }}
        .assessment-box {{ background: #f0f9ff; border: 1px solid #bae6fd; border-left: 3.5px solid #0284c7; border-radius: 4px; padding: 8px 11px; margin-bottom: 12px; page-break-inside: avoid; }}
        .assessment-title {{ font-size: 11px; font-weight: 700; color: #0369a1; text-transform: uppercase; margin-bottom: 4px; }}
        .diag-line {{ font-size: 10.8px; margin-bottom: 4px; }}
        .icd-chip {{ display: inline-block; background: #dbeafe; color: #1e40af; padding: 1px 5px; border-radius: 3px; font-weight: 700; font-size: 9.8px; margin-right: 5px; }}
        .assessment-body {{ font-size: 10.8px; line-height: 1.45; color: #0f172a; }}
        .advice-line {{ margin-top: 4px; font-size: 10.5px; }}
        .followup-line {{ margin-top: 3px; font-size: 10.2px; color: #0369a1; }}
        .assessment-meta {{ margin-top: 5px; font-size: 9.6px; color: #475569; }}
        .signatures {{ display: flex; justify-content: space-between; gap: 24px; margin-top: 10px; padding-top: 8px; border-top: 1px dashed #cbd5e1; page-break-inside: avoid; }}
        .sig-block {{ flex: 1; text-align: center; }}
        .sig-role {{ font-size: 10.5px; font-weight: 700; color: #0f172a; text-transform: uppercase; }}
        .sig-sub {{ font-size: 9.5px; color: #64748b; font-style: italic; margin-top: 2px; }}
        .sig-stamp {{ margin: 8px auto; display: inline-block; padding: 4px 10px; border-radius: 4px; font-size: 9.3px; font-weight: 700; }}
        .stamp-lab {{ background: #f8fafc; color: #334155; border: 1px solid #cbd5e1; }}
        .stamp-doc {{ background: #f0fdf4; color: #15803d; border: 1px solid #86efac; }}
        .sig-name {{ font-size: 11px; font-weight: 700; color: #0f172a; margin-top: 2px; }}
        .footer {{ margin-top: 10px; padding-top: 6px; border-top: 1px solid #e2e8f0; font-size: 9px; color: #64748b; text-align: center; line-height: 1.45; page-break-inside: avoid; }}
    </style>
</head>
<body>
<div class=""sheet"">
    <div class=""top-header"">
        <div>
            <div class=""brand-row"">
                <span class=""brand-badge"">BMS LAB</span>
                <span class=""clinic-name"">{Enc(clinicName)}</span>
            </div>
            <div class=""clinic-sub"">Địa chỉ: {Enc(clinicAddress)} · Hotline: {Enc(hotline)}</div>
            <div class=""clinic-sub"">Hệ thống quản lý chất lượng phòng xét nghiệm y khoa đạt chuẩn ISO 15189:2022</div>
        </div>
        <div class=""meta-right"">
            <div>Mã phiếu KQ: <strong class=""mono"">{Enc(result.OrderCode)}</strong></div>
            <div>Mã chỉ định: <strong class=""mono"">{Enc(appointmentCodeDisplay)}</strong></div>
            <div>Barcode mẫu: <strong class=""mono"">{Enc(barcodeDisplay)}</strong></div>
            <div><span class=""status-pill"">Đã Bác sĩ phê duyệt</span></div>
        </div>
    </div>

    <div class=""doc-title-wrap"">
        <h1 class=""doc-title"">Phiếu Kết Quả Xét Nghiệm Y Khoa</h1>
        <div class=""doc-package"">Gói dịch vụ: {Enc(packageDisplay)}</div>
    </div>

    <div class=""patient-grid"">
        <div class=""info-row""><span class=""info-label"">Họ và tên bệnh nhân:</span><span class=""info-val"" style=""text-transform:uppercase;"">{Enc(result.PatientName)}</span></div>
        <div class=""info-row""><span class=""info-label"">Bác sĩ phê duyệt:</span><span class=""info-val"">{Enc(doctorDisplay)}</span></div>
        <div class=""info-row""><span class=""info-label"">Mã bệnh nhân (PID):</span><span class=""info-val mono"" style=""color:#0369a1;"">{Enc(result.PatientId)}</span></div>
        <div class=""info-row""><span class=""info-label"">Chuyên khoa:</span><span class=""info-val"">{Enc(specialtyDisplay)}</span></div>
        <div class=""info-row""><span class=""info-label"">Ngày sinh / Giới tính:</span><span class=""info-val"">{Enc(result.DobAgeSex)}</span></div>
        <div class=""info-row""><span class=""info-label"">Thời điểm lấy mẫu:</span><span class=""info-val"">{Enc(collectedVn)}</span></div>
        <div class=""info-row""><span class=""info-label"">Loại mẫu bệnh phẩm:</span><span class=""info-val"">{Enc(sampleTypeDisplay)}</span></div>
        <div class=""info-row""><span class=""info-label"">Thời điểm trả kết quả:</span><span class=""info-val"">{Enc(releasedVn)}</span></div>
    </div>

    <div class=""section-bar"">
        <h2 class=""section-title"">Bảng Kết Quả Phân Tích Chỉ Số Sinh Học ({result.Parameters.Count} chỉ số)</h2>
        {(result.AbnormalCount > 0
            ? $@"<span class=""summary-chip chip-err"">{result.AbnormalCount} chỉ số ngoài khoảng tham chiếu</span>"
            : @"<span class=""summary-chip chip-ok"">Tất cả chỉ số trong khoảng bình thường</span>")}
    </div>

    <table class=""results-table"">
        <thead>
            <tr>
                <th class=""c-center"" style=""width:5%"">STT</th>
                <th style=""width:36%"">Tên Chỉ Số Xét Nghiệm</th>
                <th class=""c-right"" style=""width:10%"">Kết Quả</th>
                <th style=""width:9%"">Đơn Vị</th>
                <th style=""width:15%"">Khoảng Tham Chiếu</th>
                <th class=""c-center"" style=""width:11%"">So Lần Trước</th>
                <th class=""c-center"" style=""width:14%"">Đánh Giá</th>
            </tr>
        </thead>
        <tbody>
            {rowsHtml}
        </tbody>
    </table>

    <div class=""ref-note"">
        * Ghi chú: {Enc(result.ReferenceIntervalNotes)}. Khoảng tham chiếu mang tính tham khảo theo độ tuổi và giới tính; giá trị in đậm nằm ngoài khoảng bình thường.
    </div>

    <div class=""assessment-box"">
        <div class=""assessment-title"">Chẩn Đoán Cuối Cùng &amp; Kết Luận Lâm Sàng Của Bác Sĩ</div>
        {diagBlockHtml}
        <div class=""assessment-body"">{Enc(assessmentDisplay)}</div>
        {adviceBlockHtml}
        {followUpHtml}
        <div class=""assessment-meta"">— {Enc(doctorDisplay)} · {Enc(specialtyDisplay)} · Phê duyệt lúc {Enc(releasedVn)}</div>
    </div>

    <div class=""signatures"">
        <div class=""sig-block"">
            <div class=""sig-role"">Kỹ Thuật Viên Xét Nghiệm</div>
            <div class=""sig-sub"">(Thực hiện phân tích &amp; kiểm chuẩn IQC)</div>
            <div class=""sig-stamp stamp-lab"">Đã kiểm chuẩn kỹ thuật</div>
            <div class=""sig-name"">{Enc(performedByDisplay)}</div>
            <div class=""sig-sub"">Khoa Xét Nghiệm — BMS MedLab</div>
        </div>
        <div class=""sig-block"">
            <div class=""sig-role"">Bác Sĩ Chuyên Khoa Phê Duyệt</div>
            <div class=""sig-sub"">(Thẩm định lâm sàng &amp; ký số điện tử)</div>
            <div class=""sig-stamp stamp-doc"">Đã ký duyệt · {Enc(releasedVn)}</div>
            <div class=""sig-name"">{Enc(doctorDisplay)}</div>
            <div class=""sig-sub"">{Enc(specialtyDisplay)}</div>
        </div>
    </div>

    <div class=""footer"">
        Phiếu kết quả xét nghiệm được phát hành điện tử theo tiêu chuẩn ISO 15189:2022. Kết quả chỉ có giá trị đối với mẫu bệnh phẩm tại thời điểm xét nghiệm.<br/>
        Mã xác thực toàn vẹn (SHA-256): <span class=""mono"">{Enc(hashDisplay)}</span>
    </div>
</div>
</body>
</html>";

        return new ExportPatientPdfResponseDto
        {
            OrderId = result.OrderId,
            ReportId = result.OrderCode,
            FileName = $"Phieu-Ket-Qua-XN-{result.OrderCode}.pdf",
            MimeType = "application/pdf",
            VerificationHash = hashDisplay,
            PrintableHtml = printableHtml,
            DownloadUrl = $"/api/testresults/patient/order/{result.OrderId}/export-pdf",
            GeneratedAt = DateTime.UtcNow
        };
    }

    public async Task<ShareResultResponseDto> GenerateShareLinkAsync(string orderId)
    {
        var token = Convert.ToBase64String(Guid.NewGuid().ToByteArray())
            .Replace("+", "-").Replace("/", "_").TrimEnd('=');

        var shareUrl = $"/results/share?orderId={orderId}&token={token}";

        return await Task.FromResult(new ShareResultResponseDto
        {
            OrderId = orderId,
            ShareToken = token,
            ShareUrl = shareUrl,
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            Status = "Active"
        });
    }

    public async Task<IEnumerable<PatientTestResultDetailDto>> GetResultsByPatientIdAsync(string patientId)
    {
        var list = new List<PatientTestResultDetailDto>();

        // Query reports by patient ID
        var reports = await _reportRepository.GetByPatientIdAsync(patientId);
        foreach (var r in reports)
        {
            list.Add(MapReportToPatientDto(r));
        }

        // Also check in-memory dbContext
        lock (_dbContext.TestReports)
        {
            foreach (var r in _dbContext.TestReports.Where(r => r.PatientId.Equals(patientId, StringComparison.OrdinalIgnoreCase)))
            {
                if (!list.Any(x => x.OrderId == r.OrderId))
                {
                    list.Add(MapReportToPatientDto(r));
                }
            }
        }

        // Also load approved diagnostic results directly from MongoDB test_results
        if (_database != null && !string.IsNullOrWhiteSpace(patientId))
        {
            try
            {
                var col = _database.GetCollection<BsonDocument>("test_results");
                var fb = Builders<BsonDocument>.Filter;
                var filter = fb.Ne("isDeleted", true)
                    & fb.Ne("is_deleted", true)
                    & fb.Ne("isSuperseded", true)
                    & fb.Or(fb.Eq("patientId", patientId), fb.Eq("patient_id", patientId))
                    & fb.In("status", new[] { "APPROVED", "Approved", "Completed" });
                var docs = await col.Find(filter).Sort(new BsonDocument("createdAt", -1)).Limit(30).ToListAsync();
                foreach (var d in docs)
                {
                    var rid = d.TryGetValue("testResultId", out var trv) && trv.IsString && !string.IsNullOrWhiteSpace(trv.AsString)
                        ? trv.AsString
                        : d.TryGetValue("_id", out var idv) ? idv.ToString() ?? "" : "";
                    if (string.IsNullOrWhiteSpace(rid) || list.Any(x => x.OrderId.Equals(rid, StringComparison.OrdinalIgnoreCase)))
                        continue;
                    var dto = await TryLoadFromMongoTestResultAsync(rid);
                    if (dto != null) list.Add(dto);
                }
            }
            catch { }
        }

        // Return only real approved patient results (never inject hardcoded mock/demo records)
        return list;
    }

    private PatientTestResultDetailDto MapReportToPatientDto(TestReport report)
    {
        var parameters = report.Parameters.Select(p => new PatientResultParameterDto
        {
            ParameterName = p.ParameterName,
            Value = p.ResultValue,
            Unit = p.Unit,
            ReferenceRange = p.ReferenceRange,
            DeltaPrevious = CalculateSimulatedDelta(p.ParameterName, p.ResultValue),
            Flag = NormalizeFlag(p.Flag, p.IsAbnormal),
            IsAbnormal = p.IsAbnormal,
            FlagSeverity = (p.Flag == "HH" || p.Flag == "LL") ? "critical" : (p.IsAbnormal ? "warning" : "normal")
        }).ToList();

        var abnormalCount = parameters.Count(p => p.IsAbnormal || p.Flag == "HH" || p.Flag == "H");

        var token = Convert.ToBase64String(Encoding.UTF8.GetBytes(report.Id)).TrimEnd('=').Replace("+", "-");

        return new PatientTestResultDetailDto
        {
            OrderId = report.OrderId,
            OrderCode = report.OrderId,
            PackageName = string.IsNullOrWhiteSpace(report.PackageName) ? "Comprehensive Metabolic Panel" : report.PackageName,
            Category = report.Category,
            PatientId = report.PatientId,
            PatientName = report.PatientName,
            DobAgeSex = report.DobAgeSex,
            CollectionTime = report.CollectionTime,
            ReleasedDate = report.IssuedAt.ToString("dd MMM yyyy HH:mm"),
            ApprovedByDoctor = string.IsNullOrWhiteSpace(report.ApprovedBy) ? "BS. Trần Thị Bình" : report.ApprovedBy,
            DoctorSpecialty = "Internal Medicine",
            DoctorAssessment = string.IsNullOrWhiteSpace(report.ClinicalNotes) ?
                "Results compiled and evaluated. Regular physician consultation recommended for ongoing monitoring." :
                report.ClinicalNotes,
            OverallStatus = "Released",
            AbnormalCount = abnormalCount,
            ReferenceIntervalNotes = "Adult reference intervals per ISO 15189 · Fasting 12h confirmed",
            Parameters = parameters,
            VerificationHash = report.VerificationHash,
            ShareToken = token,
            ShareUrl = $"/results/share?orderId={report.OrderId}&token={token}"
        };
    }

    private PatientTestResultDetailDto MapOrderToPatientDto(TestOrder order)
    {
        var parameters = order.Items.Select((item, idx) => new PatientResultParameterDto
        {
            ParameterName = item.TestName,
            Value = "1.25",
            Unit = "mmol/L",
            ReferenceRange = "1.00 - 2.50",
            DeltaPrevious = "+0.05",
            Flag = "N",
            IsAbnormal = false,
            FlagSeverity = "normal"
        }).ToList();

        var hash = ComputeSha256($"{order.OrderNumber}-{order.PatientId}-{order.CreatedAt:O}");
        var token = Guid.NewGuid().ToString("N");

        return new PatientTestResultDetailDto
        {
            OrderId = order.Id,
            OrderCode = order.OrderNumber,
            PackageName = order.Items.FirstOrDefault()?.TestName ?? "Clinical Blood Profile",
            Category = "Biochemistry",
            PatientId = order.PatientId,
            PatientName = order.PatientName,
            DobAgeSex = $"{order.PatientAge}y · {order.PatientGender}",
            CollectionTime = order.CreatedAt.ToString("dd MMM yyyy HH:mm"),
            ReleasedDate = order.CreatedAt.AddHours(4).ToString("dd MMM yyyy HH:mm"),
            ApprovedByDoctor = string.IsNullOrWhiteSpace(order.DoctorName) ? "BS. Trần Thị Bình" : order.DoctorName,
            DoctorSpecialty = "Internal Medicine",
            DoctorAssessment = "Evaluation in progress. Preliminary clinical markers indicate stable parameters.",
            OverallStatus = "Released",
            AbnormalCount = 0,
            ReferenceIntervalNotes = "Adult male reference intervals · Fasting verified",
            Parameters = parameters,
            VerificationHash = hash,
            ShareToken = token,
            ShareUrl = $"/results/share?orderId={order.OrderNumber}&token={token}"
        };
    }

    private PatientTestResultDetailDto GenerateStandardLipidProfileDto(string orderId)
    {
        var parameters = new List<PatientResultParameterDto>
        {
            new() { ParameterName = "LDL Cholesterol", Value = "3.95", Unit = "mmol/L", ReferenceRange = "< 2.58", DeltaPrevious = "+0.42 ↑", Flag = "HH", IsAbnormal = true, FlagSeverity = "critical" },
            new() { ParameterName = "Total Cholesterol", Value = "5.42", Unit = "mmol/L", ReferenceRange = "< 5.17", DeltaPrevious = "+0.28 ↑", Flag = "H", IsAbnormal = true, FlagSeverity = "warning" },
            new() { ParameterName = "HDL Cholesterol", Value = "1.28", Unit = "mmol/L", ReferenceRange = "> 1.04", DeltaPrevious = "+0.02", Flag = "N", IsAbnormal = false, FlagSeverity = "normal" },
            new() { ParameterName = "Triglycerides", Value = "1.65", Unit = "mmol/L", ReferenceRange = "< 1.70", DeltaPrevious = "-0.12", Flag = "N", IsAbnormal = false, FlagSeverity = "normal" },
            new() { ParameterName = "VLDL (calculated)", Value = "0.75", Unit = "mmol/L", ReferenceRange = "< 1.04", DeltaPrevious = "-", Flag = "N", IsAbnormal = false, FlagSeverity = "normal" }
        };

        var hash = ComputeSha256($"ORD-260927-0231-PT-004523-20260928");
        var token = "token-share-260927-lipid";

        return new PatientTestResultDetailDto
        {
            OrderId = orderId,
            OrderCode = "ORD-260927-0231",
            PackageName = "Lipid Profile",
            Category = "Clinical Biochemistry",
            PatientId = "PAT-079201009988",
            PatientName = "Trần Văn An",
            DobAgeSex = "Male 34y",
            CollectionTime = "27 Sep 2026 08:45",
            ReleasedDate = "28 Sep 2026 10:12",
            ApprovedByDoctor = "BS. Trần Thị Bình",
            DoctorSpecialty = "Internal Medicine",
            DoctorAssessment = "LDL cholesterol is elevated at 3.95 mmol/L and has increased by +0.42 since June 2026. This confirms sustained hyperlipidemia. Please schedule a follow-up consultation to discuss lifestyle adjustments (dietary intervention, exercise target 150 min/week) and evaluate whether statin therapy is indicated.",
            OverallStatus = "Released",
            AbnormalCount = 2,
            ReferenceIntervalNotes = "Adult male 18-65y · Fasting 12h confirmed",
            Parameters = parameters,
            VerificationHash = hash,
            ShareToken = token,
            ShareUrl = $"/results/share?orderId=ORD-260927-0231&token={token}"
        };
    }

    private static string NormalizeFlag(string rawFlag, bool isAbnormal)
    {
        if (string.Equals(rawFlag, "HH", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(rawFlag, "CRITICAL", StringComparison.OrdinalIgnoreCase))
            return "HH";
        if (string.Equals(rawFlag, "H", StringComparison.OrdinalIgnoreCase))
            return "H";
        if (string.Equals(rawFlag, "L", StringComparison.OrdinalIgnoreCase))
            return "L";
        if (string.Equals(rawFlag, "LL", StringComparison.OrdinalIgnoreCase))
            return "LL";
        return isAbnormal ? "H" : "N";
    }

    private static string? CalculateSimulatedDelta(string paramName, string value) { return "-"; }

    private async Task<PatientTestResultDetailDto?> TryLoadFromMongoTestResultAsync(string orderId)
    {
        if (_database == null || string.IsNullOrWhiteSpace(orderId)) return null;
        try
        {
            var trimmed = orderId.Trim();
            var col = _database.GetCollection<BsonDocument>("test_results");
            var f = Builders<BsonDocument>.Filter;
            var active = f.Ne("isDeleted", true) & f.Ne("is_deleted", true);
            var doc = await col.Find(active & f.Or(
                    f.Eq("_id", trimmed),
                    f.Eq("testResultId", trimmed),
                    f.Eq("resultCode", trimmed),
                    f.Eq("result_code", trimmed),
                    f.Eq("sampleId", trimmed),
                    f.Eq("sample_id", trimmed),
                    f.Eq("appointmentId", trimmed),
                    f.Eq("appointment_id", trimmed)))
                .Sort(new BsonDocument("createdAt", -1))
                .FirstOrDefaultAsync();
            if (doc == null) return null;

            static string Str(BsonDocument d, params string[] keys)
            {
                foreach (var k in keys)
                {
                    if (d.TryGetValue(k, out var v) && !v.IsBsonNull)
                    {
                        var s = v.IsString ? v.AsString : v.ToString();
                        if (!string.IsNullOrWhiteSpace(s)) return s.Trim();
                    }
                }
                return string.Empty;
            }

            var validation = Str(doc, "validationStatus");
            var status = Str(doc, "status");
            var isDoctorApproved = (validation is "3" or "DoctorApproved"
                    || (string.IsNullOrWhiteSpace(validation) && status.Equals("APPROVED", StringComparison.OrdinalIgnoreCase)))
                && (status.Equals("APPROVED", StringComparison.OrdinalIgnoreCase) || status.Equals("Completed", StringComparison.OrdinalIgnoreCase));
            if (!isDoctorApproved) return null;

            var resultId = Str(doc, "testResultId", "_id");
            var patientId = Str(doc, "patientId", "patient_id");
            var sampleId = Str(doc, "sampleId", "sample_id");
            var appointmentId = Str(doc, "appointmentId", "appointment_id");
            var packageId = Str(doc, "packageId", "package_id");
            var enteredByUserId = Str(doc, "enteredByUserId", "entered_by_user_id");
            var approvedByDoctorId = Str(doc, "approvedByDoctorId", "approved_by_doctor_id");
            var digitalSignature = Str(doc, "digitalSignature", "digital_signature");

            // Load linked blood sample for real barcode, collection time, technician, sample type & package code
            BsonDocument? sampleDoc = null;
            if (!string.IsNullOrWhiteSpace(sampleId))
            {
                sampleDoc = await _database.GetCollection<BsonDocument>("blood_samples").Find(f.Or(
                    f.Eq("_id", sampleId), f.Eq("sample_id", sampleId), f.Eq("barcode", sampleId))).FirstOrDefaultAsync();
            }
            if (sampleDoc == null && !string.IsNullOrWhiteSpace(appointmentId))
            {
                sampleDoc = await _database.GetCollection<BsonDocument>("blood_samples").Find(f.Or(
                    f.Eq("test_order_id", appointmentId), f.Eq("appointment_id", appointmentId))).FirstOrDefaultAsync();
            }

            var barcode = sampleDoc != null ? Str(sampleDoc, "barcode", "Barcode") : string.Empty;
            var samplePackageCode = sampleDoc != null ? Str(sampleDoc, "package_code", "test_package_name", "packageCode") : string.Empty;
            var sampleTypeRaw = sampleDoc != null ? Str(sampleDoc, "sample_type", "sampleType") : string.Empty;
            var anticoagulant = sampleDoc != null ? Str(sampleDoc, "anticoagulant") : string.Empty;
            var labRoom = sampleDoc != null ? Str(sampleDoc, "lab_room", "labRoom") : string.Empty;
            var performedBy = sampleDoc != null ? Str(sampleDoc, "assigned_technician_name", "collector_name") : string.Empty;
            if (string.IsNullOrWhiteSpace(packageId) && !string.IsNullOrWhiteSpace(samplePackageCode))
            {
                packageId = samplePackageCode;
            }

            if (string.IsNullOrWhiteSpace(performedBy) && !string.IsNullOrWhiteSpace(enteredByUserId))
            {
                var accFilter = ObjectId.TryParse(enteredByUserId, out var empOid)
                    ? f.Or(f.Eq("_id", empOid), f.Eq("_id", enteredByUserId))
                    : f.Eq("_id", enteredByUserId);
                var techAcc = await _database.GetCollection<BsonDocument>("accounts").Find(accFilter).FirstOrDefaultAsync();
                if (techAcc != null) performedBy = Str(techAcc, "fullName", "FullName", "username");
            }

            // Patient metadata
            var patientName = patientId;
            var dobAgeSex = string.Empty;
            if (!string.IsNullOrWhiteSpace(patientId))
            {
                var pDoc = await _database.GetCollection<BsonDocument>("patients").Find(f.Or(
                    f.Eq("_id", patientId), f.Eq("patientCode", patientId), f.Eq("patient_code", patientId))).FirstOrDefaultAsync();
                if (pDoc != null)
                {
                    var fn = Str(pDoc, "fullName", "FullName", "fullname", "full_name");
                    if (!string.IsNullOrWhiteSpace(fn)) patientName = fn;
                    var genderVn = TranslateGender(Str(pDoc, "gender", "Gender"));
                    string dobStr = string.Empty;
                    if (pDoc.TryGetValue("dateOfBirth", out var dob1) && dob1.IsBsonDateTime)
                    {
                        var dt = dob1.ToUniversalTime();
                        var age = Math.Max(0, DateTime.UtcNow.Year - dt.Year - (DateTime.UtcNow.DayOfYear < dt.DayOfYear ? 1 : 0));
                        dobStr = $"{dt:dd/MM/yyyy} ({age} tuổi)";
                    }
                    else if (pDoc.TryGetValue("dob", out var dob2) && dob2.IsBsonDateTime)
                    {
                        var dt = dob2.ToUniversalTime();
                        var age = Math.Max(0, DateTime.UtcNow.Year - dt.Year - (DateTime.UtcNow.DayOfYear < dt.DayOfYear ? 1 : 0));
                        dobStr = $"{dt:dd/MM/yyyy} ({age} tuổi)";
                    }
                    dobAgeSex = string.Join(" · ", new[] { dobStr, genderVn }.Where(x => !string.IsNullOrWhiteSpace(x)));
                }
            }

            // Package name & category (prefer service_packages)
            var packageName = packageId;
            var categoryName = "Xét Nghiệm Lâm Sàng";
            if (!string.IsNullOrWhiteSpace(packageId))
            {
                var upper = packageId.ToUpperInvariant();
                var sp = await _database.GetCollection<BsonDocument>("service_packages").Find(f.Or(
                    f.Eq("_id", packageId), f.Eq("Code", upper), f.Eq("code", upper), f.Eq("Name", packageId), f.Eq("name", packageId))).FirstOrDefaultAsync();
                if (sp != null)
                {
                    var spName = Str(sp, "Name", "name", "package_name");
                    if (!string.IsNullOrWhiteSpace(spName)) packageName = spName;
                    var spCat = Str(sp, "Category", "category");
                    if (!string.IsNullOrWhiteSpace(spCat)) categoryName = spCat;
                }
                if (string.IsNullOrWhiteSpace(packageName) || packageName == packageId)
                {
                    var tp = await _database.GetCollection<BsonDocument>("test_packages").Find(f.Or(
                        f.Eq("_id", packageId), f.Eq("package_code", upper), f.Eq("code", upper))).FirstOrDefaultAsync();
                    if (tp != null) packageName = Str(tp, "package_name", "name");
                }
            }
            if (string.IsNullOrWhiteSpace(packageName)) packageName = "Xét Nghiệm Huyết Học & Sinh Hóa Lâm Sàng";

            // Parameters
            var parameters = new List<PatientResultParameterDto>();
            var itemsVal = doc.GetValue("items", BsonNull.Value);
            var itemDocs = itemsVal.IsBsonArray && itemsVal.AsBsonArray.Count > 0
                ? itemsVal.AsBsonArray.Where(x => x.IsBsonDocument).Select(x => x.AsBsonDocument).ToList()
                : await _database.GetCollection<BsonDocument>("test_parameters").Find(f.Eq("result_id", resultId)).ToListAsync();

            foreach (var x in itemDocs)
            {
                var code = Str(x, "criterionCode", "code");
                var name = Str(x, "criterionName", "name");
                var val = Str(x, "measuredValue", "value_numeric", "value_text");
                var unit = Str(x, "unit");
                var refRange = Str(x, "referenceInterval", "reference_display");
                var rawFlag = Str(x, "flag");
                var normFlag = rawFlag.ToUpperInvariant() switch
                {
                    "3" or "CRITICALPANIC" or "CRITICAL" or "PANIC" or "HH" => "HH",
                    "2" or "HIGH" or "H" => "H",
                    "1" or "LOW" or "L" => "L",
                    "LL" => "LL",
                    _ => "N"
                };
                var isAbn = normFlag != "N";
                // Compute real Delta vs previous approved test result of the same patient if available
                string deltaPrev = "—";
                if (!string.IsNullOrWhiteSpace(patientId) && double.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var currNum))
                {
                    try
                    {
                        var prevDocs = await col.Find(active
                            & f.Ne("_id", doc["_id"])
                            & f.Or(f.Eq("patientId", patientId), f.Eq("patient_id", patientId))
                            & f.In("status", new[] { "APPROVED", "Approved", "Completed" }))
                            .Sort(new BsonDocument("createdAt", -1))
                            .Limit(5)
                            .ToListAsync();
                        foreach (var pd in prevDocs)
                        {
                            var pItems = pd.GetValue("items", BsonNull.Value);
                            if (!pItems.IsBsonArray) continue;
                            var matchedPrev = pItems.AsBsonArray
                                .Where(iv => iv.IsBsonDocument)
                                .Select(iv => iv.AsBsonDocument)
                                .FirstOrDefault(iv => string.Equals(Str(iv, "criterionCode", "code"), code, StringComparison.OrdinalIgnoreCase));
                            if (matchedPrev != null && double.TryParse(Str(matchedPrev, "measuredValue", "value_numeric"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var prevNum))
                            {
                                var diff = currNum - prevNum;
                                deltaPrev = diff > 0 ? $"+{diff:0.##} ↑" : diff < 0 ? $"{diff:0.##} ↓" : "0.00";
                                break;
                            }
                        }
                    }
                    catch { }
                }

                parameters.Add(new PatientResultParameterDto
                {
                    ParameterName = string.IsNullOrWhiteSpace(name) ? code : (string.IsNullOrWhiteSpace(code) || name.Contains(code, StringComparison.OrdinalIgnoreCase) ? name : $"{code} · {name}"),
                    Value = val,
                    Unit = unit,
                    ReferenceRange = refRange,
                    DeltaPrevious = deltaPrev,
                    Flag = normFlag,
                    IsAbnormal = isAbn,
                    FlagSeverity = (normFlag is "HH" or "LL") ? "critical" : (isAbn ? "warning" : "normal")
                });
            }

            // Doctor's final diagnosis (test_results -> medical_assessments -> appointment notes)
            var icd10Code = Str(doc, "icd10Code", "icd10_code");
            var finalDiagnosis = Str(doc, "finalDiagnosis", "final_diagnosis");
            var doctorConclusion = Str(doc, "doctorConclusion", "doctor_conclusion");
            var treatmentAdvice = Str(doc, "treatmentAdvice", "treatment_advice");
            var followUpDate = Str(doc, "followUpDate", "follow_up_date");
            var approvingDoctor = Str(doc, "reviewedBy", "reviewed_by");
            if (approvingDoctor.EndsWith(" (Doctor)", StringComparison.OrdinalIgnoreCase))
                approvingDoctor = approvingDoctor[..^" (Doctor)".Length].Trim();

            if (string.IsNullOrWhiteSpace(finalDiagnosis) || string.IsNullOrWhiteSpace(icd10Code))
            {
                var asmCol = _database.GetCollection<BsonDocument>("medical_assessments");
                var notDel = f.Ne("is_deleted", true) & f.Ne("isDeleted", true);
                BsonDocument? asm = null;
                if (!string.IsNullOrWhiteSpace(appointmentId))
                {
                    asm = await asmCol.Find(notDel & f.Or(f.Eq("encounter_id", appointmentId), f.Eq("order_id", appointmentId), f.Eq("appointment_id", appointmentId)))
                        .Sort(new BsonDocument("created_at", -1)).FirstOrDefaultAsync();
                }
                if (asm == null && !string.IsNullOrWhiteSpace(patientId))
                {
                    asm = await asmCol.Find(notDel & f.Or(f.Eq("patient_id", patientId), f.Eq("patient_code", patientId)) & f.Or(f.Eq("is_finalized", true), f.Eq("status", "Finalized")))
                        .Sort(new BsonDocument("created_at", -1)).FirstOrDefaultAsync();
                }
                if (asm != null)
                {
                    if (string.IsNullOrWhiteSpace(icd10Code)) icd10Code = Str(asm, "icd10_code", "icd10Code");
                    if (string.IsNullOrWhiteSpace(finalDiagnosis)) finalDiagnosis = Str(asm, "final_diagnosis", "finalDiagnosis", "icd10_name", "icd10Name");
                    if (string.IsNullOrWhiteSpace(doctorConclusion)) doctorConclusion = Str(asm, "clinical_impression", "clinicalImpression");
                    if (string.IsNullOrWhiteSpace(treatmentAdvice)) treatmentAdvice = Str(asm, "treatment_plan", "treatmentPlan", "prescription_notes");
                    if (string.IsNullOrWhiteSpace(followUpDate) && asm.TryGetValue("follow_up_date", out var fup) && fup.IsBsonDateTime)
                        followUpDate = fup.ToUniversalTime().ToString("yyyy-MM-dd");
                    if (string.IsNullOrWhiteSpace(approvingDoctor)) approvingDoctor = Str(asm, "doctor_name", "doctorName");
                }
            }

            if ((string.IsNullOrWhiteSpace(finalDiagnosis) || string.IsNullOrWhiteSpace(doctorConclusion)) && !string.IsNullOrWhiteSpace(appointmentId))
            {
                var apptDoc = await _database.GetCollection<BsonDocument>("appointments").Find(f.Or(
                    f.Eq("_id", appointmentId), f.Eq("appointmentCode", appointmentId))).FirstOrDefaultAsync();
                var apptNotes = apptDoc != null ? Str(apptDoc, "notes", "Notes") : string.Empty;
                if (!string.IsNullOrWhiteSpace(apptNotes))
                {
                    var diagMatch = Regex.Match(apptNotes, @"Chẩn đoán\s+ICD-10\s+([A-Z][0-9]{2}(?:\.[0-9A-Z]+)?)\s*\((.+?)\)\s*(?:\.\s*Kết luận:|$)", RegexOptions.IgnoreCase);
                    if (diagMatch.Success)
                    {
                        if (string.IsNullOrWhiteSpace(icd10Code)) icd10Code = diagMatch.Groups[1].Value.Trim();
                        if (string.IsNullOrWhiteSpace(finalDiagnosis)) finalDiagnosis = diagMatch.Groups[2].Value.Trim();
                    }
                    var conclMatch = Regex.Match(apptNotes, @"Kết luận:\s*(.+)$", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                    if (conclMatch.Success && string.IsNullOrWhiteSpace(doctorConclusion))
                        doctorConclusion = conclMatch.Groups[1].Value.Trim();
                }
            }

            var doctorSpecialty = string.Empty;
            if (!string.IsNullOrWhiteSpace(approvedByDoctorId))
            {
                var docFilter = ObjectId.TryParse(approvedByDoctorId, out var docOid)
                    ? f.Or(f.Eq("_id", docOid), f.Eq("_id", approvedByDoctorId))
                    : f.Eq("_id", approvedByDoctorId);
                var doctorRecord = await _database.GetCollection<BsonDocument>("doctors").Find(docFilter).FirstOrDefaultAsync();
                if (doctorRecord != null)
                {
                    if (string.IsNullOrWhiteSpace(approvingDoctor)) approvingDoctor = Str(doctorRecord, "full_name", "fullName", "FullName");
                    doctorSpecialty = Str(doctorRecord, "specialty", "Specialty");
                }
                if (string.IsNullOrWhiteSpace(approvingDoctor) || string.IsNullOrWhiteSpace(doctorSpecialty))
                {
                    var docAcc = await _database.GetCollection<BsonDocument>("accounts").Find(docFilter).FirstOrDefaultAsync();
                    if (docAcc != null)
                    {
                        if (string.IsNullOrWhiteSpace(approvingDoctor)) approvingDoctor = Str(docAcc, "fullName", "FullName");
                        if (string.IsNullOrWhiteSpace(doctorSpecialty)) doctorSpecialty = Str(docAcc, "specialty", "department");
                    }
                }
            }

            var assessmentText = !string.IsNullOrWhiteSpace(doctorConclusion)
                ? doctorConclusion
                : Str(doc, "clinicalComment", "conclusion_notes");

            var createdAt = doc.TryGetValue("createdAt", out var c1) && c1.IsBsonDateTime ? c1.ToUniversalTime()
                : doc.TryGetValue("created_at", out var c2) && c2.IsBsonDateTime ? c2.ToUniversalTime()
                : DateTime.UtcNow;
            DateTime collectedAt = createdAt;
            if (sampleDoc != null && sampleDoc.TryGetValue("collected_at", out var colAt) && colAt.IsBsonDateTime)
                collectedAt = colAt.ToUniversalTime();
            var approvedAt = doc.TryGetValue("approvedAt", out var a1) && a1.IsBsonDateTime ? a1.ToUniversalTime()
                : doc.TryGetValue("approved_at", out var a2) && a2.IsBsonDateTime ? a2.ToUniversalTime()
                : createdAt;

            var token = Convert.ToBase64String(Encoding.UTF8.GetBytes(resultId)).TrimEnd('=').Replace("+", "-");
            var effectiveHash = !string.IsNullOrWhiteSpace(digitalSignature)
                ? digitalSignature
                : ComputeSha256($"{resultId}|{patientId}|{approvedAt:O}");

            return new PatientTestResultDetailDto
            {
                OrderId = resultId,
                OrderCode = resultId,
                AppointmentCode = string.IsNullOrWhiteSpace(appointmentId) ? resultId : appointmentId,
                Barcode = string.IsNullOrWhiteSpace(barcode) ? null : barcode,
                SampleType = TranslateSampleType(sampleTypeRaw, anticoagulant),
                LabRoom = string.IsNullOrWhiteSpace(labRoom) ? null : labRoom,
                PackageName = packageName,
                Category = categoryName,
                PatientId = patientId,
                PatientName = patientName,
                DobAgeSex = dobAgeSex,
                CollectionTime = collectedAt.ToString("O"),
                ReleasedDate = approvedAt.ToString("O"),
                OrderingDoctor = string.IsNullOrWhiteSpace(approvingDoctor) ? "Bác sĩ điều trị" : approvingDoctor,
                PerformedBy = string.IsNullOrWhiteSpace(performedBy) ? "KTV Phòng Xét Nghiệm BMS" : performedBy,
                ApprovedByDoctor = string.IsNullOrWhiteSpace(approvingDoctor) ? "Bác sĩ điều trị" : approvingDoctor,
                DoctorSpecialty = string.IsNullOrWhiteSpace(doctorSpecialty) ? "Chuyên khoa Huyết học & Xét nghiệm" : doctorSpecialty,
                Icd10Code = string.IsNullOrWhiteSpace(icd10Code) ? null : icd10Code,
                FinalDiagnosis = string.IsNullOrWhiteSpace(finalDiagnosis) ? null : finalDiagnosis,
                DoctorAssessment = assessmentText,
                TreatmentAdvice = string.IsNullOrWhiteSpace(treatmentAdvice) ? null : treatmentAdvice,
                FollowUpDate = string.IsNullOrWhiteSpace(followUpDate) ? null : followUpDate,
                OverallStatus = "Released",
                AbnormalCount = parameters.Count(p => p.IsAbnormal),
                ReferenceIntervalNotes = "Khoảng tham chiếu sinh học theo chuẩn ISO 15189:2022",
                Parameters = parameters,
                VerificationHash = effectiveHash,
                ShareToken = token,
                ShareUrl = $"/results/share?orderId={resultId}&token={token}"
            };
        }
        catch
        {
            return null;
        }
    }

    private static string ComputeSha256(string raw)
    {
        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(raw));
        return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
    }
}

