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
                    
                    // 1. Direct result match
                    var filter = Builders<BsonDocument>.Filter.Or(
                        Builders<BsonDocument>.Filter.Eq("_id", trimmed),
                        Builders<BsonDocument>.Filter.Eq("sample_id", trimmed),
                        Builders<BsonDocument>.Filter.Eq("sampleId", trimmed),
                        Builders<BsonDocument>.Filter.Eq("patient_id", trimmed)
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
                            doc = await _resultsCol.Find(Builders<BsonDocument>.Filter.Eq("sample_id", sId)).FirstOrDefaultAsync();
                        }
                    }

                    // 3. Match via patients by code/name/phone
                    if (doc == null && _patientsCol != null)
                    {
                        var patDoc = await _patientsCol.Find(Builders<BsonDocument>.Filter.Or(
                            Builders<BsonDocument>.Filter.Eq("_id", trimmed),
                            Builders<BsonDocument>.Filter.Eq("patient_code", trimmed),
                            Builders<BsonDocument>.Filter.Eq("phone", trimmed),
                            Builders<BsonDocument>.Filter.Eq("phone_number", trimmed)
                        )).FirstOrDefaultAsync();

                        if (patDoc != null)
                        {
                            var pId = patDoc.Contains("patient_code") ? patDoc["patient_code"].AsString : patDoc["_id"].AsString;
                            doc = await _resultsCol.Find(Builders<BsonDocument>.Filter.Eq("patient_id", pId)).FirstOrDefaultAsync();
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
                    var patFilter = Builders<BsonDocument>.Filter.Eq("patient_id", patientId.Trim());
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
                    .Sort(Builders<BsonDocument>.Sort.Descending("created_at"))
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

    private async Task<object> BuildDetailedResultAsync(BsonDocument doc)
    {
        var resultId = doc["_id"].AsString;
        var sampleId = doc.Contains("sample_id") ? doc["sample_id"].AsString : "";
        var packageId = doc.Contains("package_id") ? doc["package_id"].AsString : "";
        var patientId = doc.Contains("patient_id") ? doc["patient_id"].AsString : "";

        // Parameters
        var paramDocs = _paramsCol != null
            ? await _paramsCol.Find(Builders<BsonDocument>.Filter.Eq("result_id", resultId)).ToListAsync()
            : new List<BsonDocument>();

        var parameters = paramDocs.Select(p => new
        {
            code = p.Contains("code") ? p["code"].AsString : "",
            name = p.Contains("name") ? p["name"].AsString : "",
            value = p.Contains("value_numeric") ? (p["value_numeric"].IsDouble ? p["value_numeric"].AsDouble : (p["value_numeric"].IsInt32 ? (double)p["value_numeric"].AsInt32 : 0)) : 0,
            unit = p.Contains("unit") ? p["unit"].AsString : "",
            referenceRange = p.Contains("reference_display") ? p["reference_display"].AsString : "",
            flag = p.Contains("flag") ? p["flag"].AsString : "NORMAL",
            isAbnormal = p.Contains("is_abnormal") && p["is_abnormal"].AsBoolean
        }).ToList();

        // Enrich sample info
        string barcode = sampleId;
        string tubeColor = "Purple";
        string sampleType = "Blood";
        string priority = "Normal";
        if (_samplesCol != null && !string.IsNullOrWhiteSpace(sampleId))
        {
            var sDoc = await _samplesCol.Find(Builders<BsonDocument>.Filter.Eq("_id", sampleId)).FirstOrDefaultAsync();
            if (sDoc != null)
            {
                barcode = sDoc.Contains("barcode") ? sDoc["barcode"].AsString : sampleId;
                if (sDoc.Contains("tube_color")) tubeColor = sDoc["tube_color"].AsString;
                if (sDoc.Contains("sample_type")) sampleType = sDoc["sample_type"].AsString;
                if (sDoc.Contains("priority")) priority = sDoc["priority"].AsString;
            }
        }

        // Enrich patient info
        string patientName = "Bệnh nhân " + patientId;
        string patientGender = "Nam";
        string patientDob = "";
        string patientPhone = "";
        string patientAddress = "";
        if (_patientsCol != null && !string.IsNullOrWhiteSpace(patientId))
        {
            var pDoc = await _patientsCol.Find(Builders<BsonDocument>.Filter.Or(
                Builders<BsonDocument>.Filter.Eq("_id", patientId),
                Builders<BsonDocument>.Filter.Eq("patient_code", patientId)
            )).FirstOrDefaultAsync();

            if (pDoc != null)
            {
                patientName = pDoc.Contains("fullname") ? pDoc["fullname"].AsString : (pDoc.Contains("full_name") ? pDoc["full_name"].AsString : patientName);
                patientGender = pDoc.Contains("gender") ? pDoc["gender"].AsString : patientGender;
                patientPhone = pDoc.Contains("phone") ? pDoc["phone"].AsString : (pDoc.Contains("phone_number") ? pDoc["phone_number"].AsString : "");
                patientAddress = pDoc.Contains("address") ? pDoc["address"].AsString : "";
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
            var pkgDoc = await _packagesCol.Find(Builders<BsonDocument>.Filter.Eq("_id", packageId)).FirstOrDefaultAsync();
            if (pkgDoc != null)
            {
                packageName = pkgDoc.Contains("package_name") ? pkgDoc["package_name"].AsString : (pkgDoc.Contains("name") ? pkgDoc["name"].AsString : packageName);
            }
        }

        return new
        {
            id = resultId,
            sampleId = sampleId,
            barcode = barcode,
            packageId = packageId,
            packageName = packageName,
            patientId = patientId,
            patientName = patientName,
            patientGender = patientGender,
            patientDob = patientDob,
            patientPhone = patientPhone,
            patientAddress = patientAddress,
            tubeColor = tubeColor,
            sampleType = sampleType,
            priority = priority,
            equipmentCode = doc.Contains("equipment_code") && !doc["equipment_code"].IsBsonNull ? doc["equipment_code"].AsString : null,
            status = doc.Contains("status") ? doc["status"].AsString : "Approved",
            digitalSignature = doc.Contains("digital_signature") && !doc["digital_signature"].IsBsonNull ? doc["digital_signature"].AsString : null,
            approvedAt = doc.Contains("approved_at") && doc["approved_at"].IsBsonDateTime ? doc["approved_at"].ToUniversalTime() : (DateTime?)null,
            conclusionNotes = doc.Contains("conclusion_notes") && !doc["conclusion_notes"].IsBsonNull ? doc["conclusion_notes"].AsString : "",
            rerunRequested = doc.Contains("rerun_requested") && doc["rerun_requested"].AsBoolean,
            reportPdfUrl = doc.Contains("report_pdf_url") && !doc["report_pdf_url"].IsBsonNull ? doc["report_pdf_url"].AsString : null,
            parameters = parameters
        };
    }

    private async Task<object> BuildSummaryResultAsync(BsonDocument d)
    {
        var sampleId = d.Contains("sample_id") ? d["sample_id"].AsString : "";
        var packageId = d.Contains("package_id") ? d["package_id"].AsString : "";
        var patientId = d.Contains("patient_id") ? d["patient_id"].AsString : "";

        string patientName = patientId;
        if (_patientsCol != null && !string.IsNullOrWhiteSpace(patientId))
        {
            var pDoc = await _patientsCol.Find(Builders<BsonDocument>.Filter.Or(
                Builders<BsonDocument>.Filter.Eq("_id", patientId),
                Builders<BsonDocument>.Filter.Eq("patient_code", patientId)
            )).FirstOrDefaultAsync();
            if (pDoc != null)
            {
                patientName = pDoc.Contains("fullname") ? pDoc["fullname"].AsString : (pDoc.Contains("full_name") ? pDoc["full_name"].AsString : patientId);
            }
        }

        string packageName = packageId;
        if (_packagesCol != null && !string.IsNullOrWhiteSpace(packageId))
        {
            var pkgDoc = await _packagesCol.Find(Builders<BsonDocument>.Filter.Eq("_id", packageId)).FirstOrDefaultAsync();
            if (pkgDoc != null)
            {
                packageName = pkgDoc.Contains("package_name") ? pkgDoc["package_name"].AsString : (pkgDoc.Contains("name") ? pkgDoc["name"].AsString : packageId);
            }
        }

        string barcode = sampleId;
        if (_samplesCol != null && !string.IsNullOrWhiteSpace(sampleId))
        {
            var sDoc = await _samplesCol.Find(Builders<BsonDocument>.Filter.Eq("_id", sampleId)).FirstOrDefaultAsync();
            if (sDoc != null && sDoc.Contains("barcode")) barcode = sDoc["barcode"].AsString;
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
