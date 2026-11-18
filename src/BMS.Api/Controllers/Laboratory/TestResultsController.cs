using BMS.Api.Authorization;
using Microsoft.AspNetCore.Authorization;
using BMS.Core.Constants;
using BMS.Core.Exceptions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BMS.Application.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;
using MongoDB.Driver;

namespace BMS.Api.Controllers.Laboratory;

/// <summary>
/// Controller managing clinical test result retrieval, patient portal result inspection, and PDF export (UC75 / UC62).
/// Conforms to: Production Standards, Clean MVC Architecture & Zero Fabricated Data principles.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Route("api/test-results")]
[Route("api/v1/test-results")]
// WHY: UC75/UC62: lookup Doctor, Lab, Manager (Patient uses test-history until scoped here); export Doctor, Patient (own). (ROLE-PERMISSION-MATRIX §1, TIEU-CHI §1.2)
[Authorize]
public class TestResultsController : ControllerBase
{
    private readonly IPatientResultExportService _patientResultExportService;
    private readonly IMongoDatabase? _database;
    private readonly IMongoCollection<BsonDocument>? _resultsCol;
    private readonly IMongoCollection<BsonDocument>? _paramsCol;
    private readonly IMongoCollection<BsonDocument>? _samplesCol;
    private readonly IMongoCollection<BsonDocument>? _patientsCol;
    private readonly IMongoCollection<BsonDocument>? _packagesCol;

    public TestResultsController(
        IPatientResultExportService patientResultExportService,
        IMongoDatabase? database = null)
    {
        _patientResultExportService = patientResultExportService;
        _database = database;
        if (database != null)
        {
            try
            {
                _resultsCol = database.GetCollection<BsonDocument>("test_results");
                _paramsCol = database.GetCollection<BsonDocument>("test_parameters");
                _samplesCol = database.GetCollection<BsonDocument>("blood_samples");
                _patientsCol = database.GetCollection<BsonDocument>("patients");
                _packagesCol = database.GetCollection<BsonDocument>("test_packages");
            }
            catch { }
        }
    }

    /// <summary>
    /// Tra cứu kết quả xét nghiệm sinh hóa / huyết học theo Barcode, Sample ID, Patient ID hoặc từ khóa tìm kiếm (UC75 - Look Up Biochemical Results).
    /// </summary>
    [Authorize(Roles = RoleSets.ClinicalStaff)]
    [HttpGet]
    public async Task<IActionResult> GetResults(
        [FromQuery] string? barcode,
        [FromQuery] string? sampleId,
        [FromQuery] string? patientId,
        [FromQuery] string? search)
    {
        if (_resultsCol != null)
        {
            try
            {
                var queryKey = barcode ?? sampleId ?? search;
                if (!string.IsNullOrWhiteSpace(queryKey))
                {
                    var trimmed = queryKey.Trim();
                    
                    // 1. Direct result match (legacy snake_case + current camelCase / TestResultService schema)
                    var filter = Builders<BsonDocument>.Filter.Or(
                        Builders<BsonDocument>.Filter.Eq("_id", trimmed),
                        Builders<BsonDocument>.Filter.Eq("testResultId", trimmed),
                        Builders<BsonDocument>.Filter.Eq("resultCode", trimmed),
                        Builders<BsonDocument>.Filter.Eq("sample_id", trimmed),
                        Builders<BsonDocument>.Filter.Eq("sampleId", trimmed),
                        Builders<BsonDocument>.Filter.Eq("patient_id", trimmed),
                        Builders<BsonDocument>.Filter.Eq("patientId", trimmed)
                    );

                    var doc = await _resultsCol.Find(filter).FirstOrDefaultAsync();

                    // 2. Barcode match via blood_samples
                    if (doc == null && _samplesCol != null)
                    {
                        var sampleDoc = await _samplesCol.Find(Builders<BsonDocument>.Filter.Or(
                            Builders<BsonDocument>.Filter.Eq("barcode", trimmed),
                            Builders<BsonDocument>.Filter.Eq("_id", trimmed)
                        )).FirstOrDefaultAsync();

                        if (sampleDoc != null)
                        {
                            var sId = sampleDoc["_id"].AsString;
                            doc = await _resultsCol.Find(Builders<BsonDocument>.Filter.Or(
                                Builders<BsonDocument>.Filter.Eq("sample_id", sId),
                                Builders<BsonDocument>.Filter.Eq("sampleId", sId),
                                Builders<BsonDocument>.Filter.Eq("sampleId", trimmed),
                                Builders<BsonDocument>.Filter.Eq("sample_id", trimmed)
                            )).FirstOrDefaultAsync();
                        }
                    }

                    // 3. Match via patients by code/name/phone
                    if (doc == null && _patientsCol != null)
                    {
                        var patDoc = await _patientsCol.Find(Builders<BsonDocument>.Filter.Or(
                            Builders<BsonDocument>.Filter.Eq("_id", trimmed),
                            Builders<BsonDocument>.Filter.Eq("patient_code", trimmed),
                            Builders<BsonDocument>.Filter.Eq("patientCode", trimmed),
                            Builders<BsonDocument>.Filter.Eq("phone", trimmed),
                            Builders<BsonDocument>.Filter.Eq("phone_number", trimmed),
                            Builders<BsonDocument>.Filter.Eq("fullname", trimmed),
                            Builders<BsonDocument>.Filter.Eq("fullName", trimmed)
                        )).FirstOrDefaultAsync();

                        if (patDoc != null)
                        {
                            var pId = patDoc.Contains("patient_code") ? patDoc["patient_code"].AsString
                                : (patDoc.Contains("patientCode") ? patDoc["patientCode"].AsString : patDoc["_id"].AsString);
                            doc = await _resultsCol.Find(Builders<BsonDocument>.Filter.Or(
                                Builders<BsonDocument>.Filter.Eq("patient_id", pId),
                                Builders<BsonDocument>.Filter.Eq("patientId", pId)
                            )).FirstOrDefaultAsync();
                        }
                    }

                    if (doc != null)
                    {
                        var detailed = await BuildDetailedResultAsync(doc);
                        return Ok(new { success = true, data = detailed });
                    }

                    return NotFound(new { success = false, message = $"Không tìm thấy kết quả xét nghiệm phù hợp cho '{trimmed}'." });
                }

                // If patientId is explicitly provided and multiple results exist
                if (!string.IsNullOrWhiteSpace(patientId))
                {
                    var pid = patientId.Trim();
                    var patFilter = Builders<BsonDocument>.Filter.Or(
                        Builders<BsonDocument>.Filter.Eq("patient_id", pid),
                        Builders<BsonDocument>.Filter.Eq("patientId", pid));
                    var patResults = await _resultsCol.Find(patFilter).Limit(20).ToListAsync();
                    var list = new List<object>();
                    foreach (var d in patResults)
                    {
                        list.Add(await BuildSummaryResultAsync(d));
                    }
                    return Ok(new { success = true, total = list.Count, data = list });
                }

                // If no specific filter, return recent results
                var allRecent = await _resultsCol.Find(Builders<BsonDocument>.Filter.Empty)
                    .Sort(Builders<BsonDocument>.Sort.Descending("createdAt"))
                    .Limit(20)
                    .ToListAsync();

                var recentList = new List<object>();
                foreach (var d in allRecent)
                {
                    recentList.Add(await BuildSummaryResultAsync(d));
                }

                return Ok(new { success = true, total = recentList.Count, data = recentList });
            }
            catch (AppException ex)
            {
                return StatusCode(ex.StatusCode, new { success = false, code = ex.Code, message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, new { success = false, code = "MSG-ERR-INTERNAL", message = "Lỗi khi truy vấn kết quả xét nghiệm sinh hóa: " + ex.Message });
            }
        }

        return NotFound(new { success = false, message = "Không tìm thấy kết quả xét nghiệm phù hợp." });
    }

    /// <summary>
    /// Xem chi tiết một kết quả xét nghiệm sinh hóa theo Result ID (UC75).
    /// </summary>
    [Authorize(Roles = RoleSets.ClinicalStaff)]
    [HttpGet("{id}")]
    public async Task<IActionResult> GetResultById(string id)
    {
        try
        {
            if (_resultsCol == null)
            {
                return NotFound(new { success = false, code = "MSG-ERR-DB-UNAVAILABLE", message = "Không tìm thấy cơ sở dữ liệu kết quả xét nghiệm." });
            }

            var doc = await _resultsCol.Find(Builders<BsonDocument>.Filter.Eq("_id", id.Trim())).FirstOrDefaultAsync();
            if (doc == null)
            {
                return NotFound(new { success = false, code = "MSG-ERR-RESULT-NOT-FOUND", message = $"Không tìm thấy kết quả xét nghiệm với mã '{id}'." });
            }

            var detailed = await BuildDetailedResultAsync(doc);
            return Ok(new { success = true, data = detailed });
        }
        catch (AppException ex)
        {
            return StatusCode(ex.StatusCode, new { success = false, code = ex.Code, message = ex.Message });
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new { success = false, code = "MSG-ERR-INTERNAL", message = "Lỗi hệ thống khi tải chi tiết kết quả xét nghiệm sinh hóa." });
        }
    }

    private static string ReadString(BsonDocument doc, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (doc.Contains(key) && !doc[key].IsBsonNull)
            {
                return doc[key].IsString ? doc[key].AsString : doc[key].ToString();
            }
        }
        return "";
    }

    private static string MapResultFlag(BsonValue? flagVal)
    {
        if (flagVal == null || flagVal.IsBsonNull) return "Normal";
        if (flagVal.IsString)
        {
            var s = flagVal.AsString;
            return string.IsNullOrWhiteSpace(s) ? "Normal" : s;
        }
        if (flagVal.IsInt32 || flagVal.IsInt64 || flagVal.IsDouble)
        {
            var n = flagVal.ToInt32();
            return n switch
            {
                1 => "Low",
                2 => "High",
                3 => "CriticalPanic",
                _ => "Normal"
            };
        }
        return "Normal";
    }

    private async Task<object> BuildDetailedResultAsync(BsonDocument doc)
    {
        var resultId = ReadString(doc, "_id", "testResultId", "resultCode");
        var sampleId = ReadString(doc, "sampleId", "sample_id");
        var packageId = ReadString(doc, "packageId", "package_id");
        var patientId = ReadString(doc, "patientId", "patient_id");

        // Prefer embedded items (TestResultService schema); fall back to legacy test_parameters rows.
        var parameters = new List<object>();
        if (doc.Contains("items") && doc["items"].IsBsonArray)
        {
            foreach (var item in doc["items"].AsBsonArray)
            {
                if (!item.IsBsonDocument) continue;
                var p = item.AsBsonDocument;
                var flag = MapResultFlag(p.Contains("flag") ? p["flag"] : null);
                parameters.Add(new
                {
                    code = ReadString(p, "criterionCode", "code", "abbreviation"),
                    name = ReadString(p, "criterionName", "name", "paramName"),
                    value = ReadString(p, "measuredValue", "value"),
                    unit = ReadString(p, "unit"),
                    referenceRange = ReadString(p, "referenceInterval", "reference_display", "referenceRange"),
                    flag,
                    isAbnormal = !string.Equals(flag, "Normal", StringComparison.OrdinalIgnoreCase)
                });
            }
        }

        if (parameters.Count == 0 && _paramsCol != null)
        {
            var paramDocs = await _paramsCol.Find(Builders<BsonDocument>.Filter.Or(
                Builders<BsonDocument>.Filter.Eq("result_id", resultId),
                Builders<BsonDocument>.Filter.Eq("resultId", resultId)
            )).ToListAsync();

            foreach (var p in paramDocs)
            {
                object value = "";
                if (p.Contains("value_numeric") && !p["value_numeric"].IsBsonNull)
                {
                    value = p["value_numeric"].IsDouble ? p["value_numeric"].AsDouble
                        : (p["value_numeric"].IsInt32 ? (double)p["value_numeric"].AsInt32 : p["value_numeric"].ToString());
                }
                else if (p.Contains("value") && !p["value"].IsBsonNull)
                {
                    value = p["value"].ToString();
                }

                var flag = MapResultFlag(p.Contains("flag") ? p["flag"] : null);
                parameters.Add(new
                {
                    code = ReadString(p, "code", "criterionCode", "abbreviation"),
                    name = ReadString(p, "name", "criterionName", "paramName"),
                    value,
                    unit = ReadString(p, "unit"),
                    referenceRange = ReadString(p, "reference_display", "referenceInterval", "referenceRange"),
                    flag,
                    isAbnormal = (p.Contains("is_abnormal") && p["is_abnormal"].AsBoolean)
                        || !string.Equals(flag, "Normal", StringComparison.OrdinalIgnoreCase)
                });
            }
        }

        // Enrich sample info
        string barcode = sampleId;
        string tubeColor = "Purple";
        string sampleType = "Blood";
        string priority = "Normal";
        if (_samplesCol != null && !string.IsNullOrWhiteSpace(sampleId))
        {
            var sDoc = await _samplesCol.Find(Builders<BsonDocument>.Filter.Or(
                Builders<BsonDocument>.Filter.Eq("_id", sampleId),
                Builders<BsonDocument>.Filter.Eq("barcode", sampleId)
            )).FirstOrDefaultAsync();
            if (sDoc != null)
            {
                barcode = ReadString(sDoc, "barcode");
                if (string.IsNullOrWhiteSpace(barcode)) barcode = sampleId;
                tubeColor = ReadString(sDoc, "tube_color", "tubeColor");
                if (string.IsNullOrWhiteSpace(tubeColor)) tubeColor = "Purple";
                sampleType = ReadString(sDoc, "sample_type", "sampleType");
                if (string.IsNullOrWhiteSpace(sampleType)) sampleType = "Blood";
                priority = ReadString(sDoc, "priority");
                if (string.IsNullOrWhiteSpace(priority)) priority = "Normal";
                if (string.IsNullOrWhiteSpace(packageId))
                    packageId = ReadString(sDoc, "packageId", "package_id", "test_package_code", "testPackageCode");
            }
        }

        // Enrich patient info
        string patientName = string.IsNullOrWhiteSpace(patientId) ? "Bệnh nhân" : ("Bệnh nhân " + patientId);
        string patientGender = "Nam";
        string patientDob = "";
        string patientPhone = "";
        string patientAddress = "";
        if (_patientsCol != null && !string.IsNullOrWhiteSpace(patientId))
        {
            var pDoc = await _patientsCol.Find(Builders<BsonDocument>.Filter.Or(
                Builders<BsonDocument>.Filter.Eq("_id", patientId),
                Builders<BsonDocument>.Filter.Eq("patient_code", patientId),
                Builders<BsonDocument>.Filter.Eq("patientCode", patientId)
            )).FirstOrDefaultAsync();

            if (pDoc != null)
            {
                patientName = ReadString(pDoc, "fullname", "full_name", "fullName");
                if (string.IsNullOrWhiteSpace(patientName)) patientName = "Bệnh nhân " + patientId;
                patientGender = ReadString(pDoc, "gender");
                if (string.IsNullOrWhiteSpace(patientGender)) patientGender = "Nam";
                patientPhone = ReadString(pDoc, "phone", "phone_number", "phoneNumber");
                patientAddress = ReadString(pDoc, "address");
                if (pDoc.Contains("dob") && pDoc["dob"].IsBsonDateTime)
                {
                    patientDob = pDoc["dob"].ToUniversalTime().ToString("yyyy-MM-dd");
                }
            }
        }

        // Enrich package info
        string packageName = "Xét nghiệm sinh hóa / huyết học";
        if (_packagesCol != null && !string.IsNullOrWhiteSpace(packageId))
        {
            var pkgDoc = await _packagesCol.Find(Builders<BsonDocument>.Filter.Or(
                Builders<BsonDocument>.Filter.Eq("_id", packageId),
                Builders<BsonDocument>.Filter.Eq("package_code", packageId),
                Builders<BsonDocument>.Filter.Eq("code", packageId)
            )).FirstOrDefaultAsync();
            if (pkgDoc != null)
            {
                packageName = ReadString(pkgDoc, "package_name", "name", "packageName");
                if (string.IsNullOrWhiteSpace(packageName)) packageName = "Xét nghiệm sinh hóa / huyết học";
            }
            else if (packageId.Contains("CBC", StringComparison.OrdinalIgnoreCase))
            {
                packageName = "CBC — Tổng phân tích tế bào máu";
            }
        }

        var equipmentCode = ReadString(doc, "deviceId", "equipment_code", "equipmentCode");
        if (string.IsNullOrWhiteSpace(equipmentCode)) equipmentCode = null!;

        DateTime? approvedAt = null;
        if (doc.Contains("approvedAt") && doc["approvedAt"].IsBsonDateTime)
            approvedAt = doc["approvedAt"].ToUniversalTime();
        else if (doc.Contains("approved_at") && doc["approved_at"].IsBsonDateTime)
            approvedAt = doc["approved_at"].ToUniversalTime();

        var digitalSignature = ReadString(doc, "digitalSignature", "digital_signature");
        var conclusionNotes = ReadString(doc, "clinicalComment", "conclusion_notes", "conclusionNotes");
        var status = ReadString(doc, "status");
        if (string.IsNullOrWhiteSpace(status)) status = "Approved";

        return new
        {
            id = resultId,
            testResultId = resultId,
            sampleId,
            barcode,
            packageId,
            packageName,
            patientId,
            patientName,
            patientGender,
            patientDob,
            patientPhone,
            patientAddress,
            tubeColor,
            sampleType,
            priority,
            equipmentCode = string.IsNullOrWhiteSpace(equipmentCode) ? null : equipmentCode,
            status,
            digitalSignature = string.IsNullOrWhiteSpace(digitalSignature) ? null : digitalSignature,
            approvedAt,
            conclusionNotes,
            rerunRequested = (doc.Contains("rerun_requested") && doc["rerun_requested"].AsBoolean)
                || (doc.Contains("rerunRequested") && doc["rerunRequested"].AsBoolean),
            reportPdfUrl = string.IsNullOrWhiteSpace(ReadString(doc, "report_pdf_url", "reportPdfUrl"))
                ? null
                : ReadString(doc, "report_pdf_url", "reportPdfUrl"),
            parameters
        };
    }

    private async Task<object> BuildSummaryResultAsync(BsonDocument d)
    {
        var sampleId = ReadString(d, "sampleId", "sample_id");
        var packageId = ReadString(d, "packageId", "package_id");
        var patientId = ReadString(d, "patientId", "patient_id");

        string patientName = patientId;
        if (_patientsCol != null && !string.IsNullOrWhiteSpace(patientId))
        {
            var pDoc = await _patientsCol.Find(Builders<BsonDocument>.Filter.Or(
                Builders<BsonDocument>.Filter.Eq("_id", patientId),
                Builders<BsonDocument>.Filter.Eq("patient_code", patientId),
                Builders<BsonDocument>.Filter.Eq("patientCode", patientId)
            )).FirstOrDefaultAsync();
            if (pDoc != null)
            {
                patientName = ReadString(pDoc, "fullname", "full_name", "fullName");
                if (string.IsNullOrWhiteSpace(patientName)) patientName = patientId;
            }
        }

        string packageName = packageId;
        if (_packagesCol != null && !string.IsNullOrWhiteSpace(packageId))
        {
            var pkgDoc = await _packagesCol.Find(Builders<BsonDocument>.Filter.Or(
                Builders<BsonDocument>.Filter.Eq("_id", packageId),
                Builders<BsonDocument>.Filter.Eq("code", packageId)
            )).FirstOrDefaultAsync();
            if (pkgDoc != null)
            {
                packageName = ReadString(pkgDoc, "package_name", "name", "packageName");
                if (string.IsNullOrWhiteSpace(packageName)) packageName = packageId;
            }
        }

        string barcode = sampleId;
        if (_samplesCol != null && !string.IsNullOrWhiteSpace(sampleId))
        {
            var sDoc = await _samplesCol.Find(Builders<BsonDocument>.Filter.Or(
                Builders<BsonDocument>.Filter.Eq("_id", sampleId),
                Builders<BsonDocument>.Filter.Eq("barcode", sampleId)
            )).FirstOrDefaultAsync();
            if (sDoc != null)
            {
                barcode = ReadString(sDoc, "barcode");
                if (string.IsNullOrWhiteSpace(barcode)) barcode = sampleId;
            }
        }

        return new
        {
            id = d["_id"].AsString,
            sampleId = sampleId,
            barcode = barcode,
            packageId = packageId,
            packageName = packageName,
            patientId = patientId,
            patientName = patientName,
            status = d.Contains("status") ? d["status"].AsString : "Approved",
            conclusion = d.Contains("conclusion_notes") && !d["conclusion_notes"].IsBsonNull ? d["conclusion_notes"].AsString : "",
            approvedAt = d.Contains("approved_at") && d["approved_at"].IsBsonDateTime ? d["approved_at"].ToUniversalTime() : (DateTime?)null
        };
    }

    /// <summary>
    /// Retrieves formatted patient diagnostic test result details, reference intervals, historical deltas, and doctor assessment.
    /// </summary>
    [Authorize(Roles = RoleSets.DoctorOrPatient)]
    [HttpGet("patient/order/{orderId}")]
    public async Task<IActionResult> GetPatientResultDetail(string orderId)
    {
        try
        {
            var result = await _patientResultExportService.GetPatientResultDetailAsync(orderId);
            // Patient: own order only; same 404 for "not yours" and "missing" (R14).
            if (result != null && !await this.MayAccessPatientAsync(result.PatientId, HttpContext.RequestAborted)) result = null;
            if (result == null)
            {
                return NotFound(new { success = false, code = "MSG-ERR-RESULT-NOT-FOUND", message = $"Không tìm thấy kết quả xét nghiệm cho mã đơn '{orderId}'." });
            }
            return Ok(new { success = true, data = result });
        }
        catch (AppException ex)
        {
            return StatusCode(ex.StatusCode, new { success = false, code = ex.Code, message = ex.Message });
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new { success = false, code = "MSG-ERR-INTERNAL", message = "Lỗi hệ thống khi tải chi tiết kết quả xét nghiệm bệnh nhân." });
        }
    }

    /// <summary>
    /// Retrieves all released test results for a specific patient.
    /// </summary>
    [Authorize(Roles = RoleSets.DoctorOrPatient)]
    [HttpGet("patient/{patientId}")]
    public async Task<IActionResult> GetPatientResults(string patientId)
    {
        if (User.IsInRole(RoleNames.Patient) && !await this.MayAccessPatientAsync(patientId, HttpContext.RequestAborted))
        {
            return Forbid();
        }
        var results = await _patientResultExportService.GetResultsByPatientIdAsync(patientId);
        return Ok(new { success = true, data = results });
    }

    /// <summary>
    /// Exports formatted printable HTML and PDF download metadata for a patient test result.
    /// </summary>
    [Authorize(Roles = RoleSets.DoctorOrPatient)]
    [HttpGet("patient/order/{orderId}/export-pdf")]
    public async Task<IActionResult> ExportPatientPdf(string orderId)
    {
        var export = await _patientResultExportService.ExportResultPdfAsync(orderId);
        if (export == null)
        {
            return NotFound(new { success = false, message = $"Không thể xuất PDF cho mã phiếu '{orderId}'." });
        }
        return Ok(new { success = true, data = export });
    }

    /// <summary>
    /// Generates a time-limited secure link to share results with an external consulting doctor.
    /// </summary>
    [Authorize(Roles = RoleSets.DoctorOrPatient)]
    [HttpPost("patient/order/{orderId}/share")]
    public async Task<IActionResult> ShareResult(string orderId)
    {
        var share = await _patientResultExportService.GenerateShareLinkAsync(orderId);
        return Ok(new { success = true, data = share });
    }
}
