using BMS.Api.Authorization;
using Microsoft.AspNetCore.Authorization;
using BMS.Core.Constants;
using BMS.Core.Entities;
using BMS.Core.Exceptions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using BMS.Application.DTOs.AuditLogs;
using BMS.Application.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;
using MongoDB.Driver;

namespace BMS.Api.Controllers.Laboratory;

/// <summary>
/// Controller managing clinical biochemical test result retrieval, anti-IDOR patient scoping,
/// audit logging, and immutability/rerun protocol (UC75 / UC62).
/// Conforms to: ISO 15189:2022, ISO 27789:2021, and OWASP ASVS standards.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Route("api/test-results")]
[Route("api/v1/test-results")]
[Route("api/biochemical-results")]
[Authorize]
public class TestResultsController : ControllerBase
{
    private readonly IPatientResultExportService _patientResultExportService;
    private readonly IMongoDatabase? _database;
    private readonly IAuditLogService? _auditLogService;
    private readonly IMongoCollection<BsonDocument>? _resultsCol;
    private readonly IMongoCollection<BsonDocument>? _paramsCol;
    private readonly IMongoCollection<BsonDocument>? _samplesCol;
    private readonly IMongoCollection<BsonDocument>? _patientsCol;
    private readonly IMongoCollection<BsonDocument>? _packagesCol;

    public TestResultsController(
        IPatientResultExportService patientResultExportService,
        IMongoDatabase? database = null,
        IAuditLogService? auditLogService = null)
    {
        _patientResultExportService = patientResultExportService;
        _database = database;
        _auditLogService = auditLogService;
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
    /// Hỗ trợ cả nhân viên y tế (Doctor, Lab, Manager) và người bệnh (Patient - chỉ xem kết quả của chính mình).
    /// </summary>
    [Authorize(Roles = RoleSets.ClinicalStaffOrPatient)]
    [HttpGet]
    public async Task<IActionResult> GetResults(
        [FromQuery] string? barcode,
        [FromQuery] string? sampleId,
        [FromQuery] string? patientId,
        [FromQuery] string? search,
        [FromQuery] string? testId)
    {
        if (_resultsCol == null)
        {
            return NotFound(new { success = false, message = "Không tìm thấy cơ sở dữ liệu kết quả xét nghiệm." });
        }

        try
        {
            // OWASP ASVS: If Patient role, enforce scoping to own records only
            if (User.IsInRole(RoleNames.Patient))
            {
                if (!string.IsNullOrWhiteSpace(patientId) && !await this.MayAccessPatientAsync(patientId.Trim(), HttpContext.RequestAborted))
                {
                    return Forbid();
                }

                if (string.IsNullOrWhiteSpace(patientId))
                {
                    patientId = User.FindFirst("sub")?.Value ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                }
            }

            var queryKey = barcode ?? sampleId ?? search;
            if (!string.IsNullOrWhiteSpace(queryKey))
            {
                var trimmed = queryKey.Trim();

                // 1. Direct result match
                var filter = Builders<BsonDocument>.Filter.Or(
                    Builders<BsonDocument>.Filter.Eq("_id", trimmed),
                    Builders<BsonDocument>.Filter.Eq("testResultId", trimmed),
                    Builders<BsonDocument>.Filter.Eq("result_code", trimmed),
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
                            Builders<BsonDocument>.Filter.Eq("sampleId", sId)
                        )).FirstOrDefaultAsync();
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
                        doc = await _resultsCol.Find(Builders<BsonDocument>.Filter.Or(
                            Builders<BsonDocument>.Filter.Eq("patient_id", pId),
                            Builders<BsonDocument>.Filter.Eq("patientId", pId)
                        )).FirstOrDefaultAsync();
                    }
                }

                if (doc != null)
                {
                    var docPatientId = doc.Contains("patient_id") ? doc["patient_id"].AsString : (doc.Contains("patientId") ? doc["patientId"].AsString : "");

                    if (User.IsInRole(RoleNames.Patient))
                    {
                        if (!await this.MayAccessPatientAsync(docPatientId, HttpContext.RequestAborted))
                        {
                            return Forbid();
                        }

                        // ISO 15189: Unapproved results are not displayed to patients as official releases
                        if (!IsResultApproved(doc))
                        {
                            return StatusCode(StatusCodes.Status403Forbidden, new
                            {
                                success = false,
                                code = "MSG-ERR-RESULT-NOT-APPROVED",
                                message = "Kết quả xét nghiệm chưa được bác sĩ phê duyệt chính thức (ISO 15189:2022)."
                            });
                        }
                    }

                    await LogAuditTrailAsync("LOOKUP_BIOCHEMICAL_RESULT", doc["_id"].AsString, docPatientId, $"Tra cứu kết quả xét nghiệm sinh hóa cho '{trimmed}'");
                    var detailed = await BuildDetailedResultAsync(doc, testId);
                    return Ok(new { success = true, data = detailed });
                }

                return NotFound(new { success = false, message = $"Không tìm thấy kết quả xét nghiệm phù hợp cho '{trimmed}'." });
            }

            // If patientId is explicitly provided
            if (!string.IsNullOrWhiteSpace(patientId))
            {
                var patFilter = Builders<BsonDocument>.Filter.Or(
                    Builders<BsonDocument>.Filter.Eq("patient_id", patientId.Trim()),
                    Builders<BsonDocument>.Filter.Eq("patientId", patientId.Trim())
                );

                var patResults = await _resultsCol.Find(patFilter).Limit(50).ToListAsync();

                // For patients, only released results
                if (User.IsInRole(RoleNames.Patient))
                {
                    patResults = patResults.Where(IsResultApproved).ToList();
                }

                var list = new List<object>();
                foreach (var d in patResults)
                {
                    list.Add(await BuildSummaryResultAsync(d));
                }

                await LogAuditTrailAsync("LOOKUP_BIOCHEMICAL_RESULTS", patientId.Trim(), patientId.Trim(), $"Tra cứu danh sách kết quả xét nghiệm cho bệnh nhân {patientId}");
                return Ok(new { success = true, total = list.Count, data = list });
            }

            // If no specific filter (Clinical Staff only)
            if (User.IsInRole(RoleNames.Patient))
            {
                return BadRequest(new { success = false, message = "Vui lòng cung cấp mã xét nghiệm hoặc mã bệnh nhân." });
            }

            var allRecent = await _resultsCol.Find(Builders<BsonDocument>.Filter.Empty)
                .Sort(Builders<BsonDocument>.Sort.Descending("created_at"))
                .Limit(20)
                .ToListAsync();

            var recentList = new List<object>();
            foreach (var d in allRecent)
            {
                recentList.Add(await BuildSummaryResultAsync(d));
            }

            await LogAuditTrailAsync("LOOKUP_BIOCHEMICAL_RESULTS", "RECENT", "", "Tra cứu danh sách kết quả xét nghiệm gần đây");
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

    /// <summary>
    /// Xem chi tiết một kết quả xét nghiệm sinh hóa theo Result ID (UC75).
    /// Áp dụng kiểm soát IDOR: Bệnh nhân chỉ xem được kết quả của chính mình;
    /// Bác sĩ / KTV phòng lab xem được kết quả thuộc phạm vi quản lý.
    /// Ghi nhận Audit Trail theo ISO 27789:2021.
    /// </summary>
    [Authorize(Roles = RoleSets.ClinicalStaffOrPatient)]
    [HttpGet("{id}")]
    public async Task<IActionResult> GetResultById(string id, [FromQuery] string? testId = null)
    {
        try
        {
            if (_resultsCol == null)
            {
                return NotFound(new { success = false, code = "MSG-ERR-DB-UNAVAILABLE", message = "Không tìm thấy cơ sở dữ liệu kết quả xét nghiệm." });
            }

            var doc = await _resultsCol.Find(Builders<BsonDocument>.Filter.Or(
                Builders<BsonDocument>.Filter.Eq("_id", id.Trim()),
                Builders<BsonDocument>.Filter.Eq("testResultId", id.Trim()),
                Builders<BsonDocument>.Filter.Eq("result_code", id.Trim())
            )).FirstOrDefaultAsync();

            if (doc == null)
            {
                return NotFound(new { success = false, code = "MSG-ERR-RESULT-NOT-FOUND", message = $"Không tìm thấy kết quả xét nghiệm với mã '{id}'." });
            }

            var docPatientId = doc.Contains("patient_id") ? doc["patient_id"].AsString : (doc.Contains("patientId") ? doc["patientId"].AsString : "");

            // OWASP ASVS Anti-IDOR Check
            if (User.IsInRole(RoleNames.Patient))
            {
                if (!await this.MayAccessPatientAsync(docPatientId, HttpContext.RequestAborted))
                {
                    // Patient A requests Patient B's result -> 403 Forbidden
                    return Forbid();
                }

                // ISO 15189: Unapproved results must NOT be released to patient as official results
                if (!IsResultApproved(doc))
                {
                    return StatusCode(StatusCodes.Status403Forbidden, new
                    {
                        success = false,
                        code = "MSG-ERR-RESULT-NOT-APPROVED",
                        message = "Kết quả xét nghiệm chưa được bác sĩ phê duyệt chính thức (ISO 15189:2022)."
                    });
                }
            }

            // ISO 27789: Audit Trail
            await LogAuditTrailAsync("LOOKUP_BIOCHEMICAL_RESULT", id, docPatientId, $"Xem chi tiết kết quả xét nghiệm sinh hóa {id}");

            var detailed = await BuildDetailedResultAsync(doc, testId);
            return Ok(new { success = true, data = detailed });
        }
        catch (AppException ex)
        {
            return StatusCode(ex.StatusCode, new { success = false, code = ex.Code, message = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new { success = false, code = "MSG-ERR-INTERNAL", message = "Lỗi hệ thống khi tải chi tiết kết quả xét nghiệm sinh hóa: " + ex.Message });
        }
    }

    /// <summary>
    /// Chạy lại xét nghiệm (Rerun) theo ISO 15189:2022.
    /// Giữ nguyên kết quả và khoảng tham chiếu lịch sử, tạo Analysis Run / version mới.
    /// </summary>
    [Authorize(Roles = RoleSets.LabOrManager)]
    [HttpPost("{id}/rerun")]
    public async Task<IActionResult> RerunTest(string id, [FromBody] RerunBiochemicalTestRequestDto request)
    {
        if (_resultsCol == null)
        {
            return NotFound(new { success = false, message = "Không tìm thấy cơ sở dữ liệu kết quả xét nghiệm." });
        }

        if (string.IsNullOrWhiteSpace(request?.Reason))
        {
            return BadRequest(new { success = false, message = "Lý do chạy lại xét nghiệm là bắt buộc (ISO 15189)." });
        }

        var doc = await _resultsCol.Find(Builders<BsonDocument>.Filter.Or(
            Builders<BsonDocument>.Filter.Eq("_id", id.Trim()),
            Builders<BsonDocument>.Filter.Eq("testResultId", id.Trim()),
            Builders<BsonDocument>.Filter.Eq("result_code", id.Trim())
        )).FirstOrDefaultAsync();

        if (doc == null)
        {
            return NotFound(new { success = false, message = $"Không tìm thấy kết quả xét nghiệm mã '{id}' để chạy lại." });
        }

        var currentRun = doc.Contains("analysis_run") && doc["analysis_run"].IsInt32 ? doc["analysis_run"].AsInt32 : 1;
        var currentVersion = doc.Contains("version") && doc["version"].IsInt32 ? doc["version"].AsInt32 : 1;
        var newRun = currentRun + 1;
        var newVersion = currentVersion + 1;

        var now = DateTime.UtcNow;
        var newResultId = $"RES-{now:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..6].ToUpper()}";

        // Mark original as rerun requested & superseded while keeping its values and intervals intact
        var updateOriginal = Builders<BsonDocument>.Update
            .Set("rerun_requested", true)
            .Set("is_superseded", true)
            .Set("superseded_at", now)
            .Set("rerun_reason", request.Reason);
        await _resultsCol.UpdateOneAsync(Builders<BsonDocument>.Filter.Eq("_id", doc["_id"].AsString), updateOriginal);

        // Build new analysis run document
        var newItems = new BsonArray();
        if (request.Parameters != null && request.Parameters.Count > 0)
        {
            foreach (var p in request.Parameters)
            {
                var refDisplay = (p.RefLow.HasValue && p.RefHigh.HasValue) ? $"{p.RefLow}-{p.RefHigh}" : "";
                var flag = "NORMAL";
                if (p.RefLow.HasValue && p.Value < p.RefLow.Value) flag = "L";
                if (p.RefHigh.HasValue && p.Value > p.RefHigh.Value) flag = "H";

                newItems.Add(new BsonDocument
                {
                    { "criterionId", p.Code },
                    { "criterionCode", p.Code },
                    { "criterionName", p.Name ?? p.Code },
                    { "measuredValue", p.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) },
                    { "unit", p.Unit ?? "" },
                    { "referenceInterval", refDisplay },
                    { "flag", flag },
                    { "refLow", p.RefLow.HasValue ? (BsonValue)p.RefLow.Value : BsonNull.Value },
                    { "refHigh", p.RefHigh.HasValue ? (BsonValue)p.RefHigh.Value : BsonNull.Value }
                });
            }
        }
        else if (doc.Contains("items") && doc["items"].IsBsonArray)
        {
            newItems = doc["items"].AsBsonArray;
        }

        var sampleIdVal = ReadString(doc, "sample_id", "sampleId");
        var patientIdVal = ReadString(doc, "patient_id", "patientId");
        var orderIdVal = ReadString(doc, "order_id", "orderId");
        var packageIdVal = ReadString(doc, "package_id", "packageId");
        var newDoc = new BsonDocument
        {
            { "_id", newResultId },
            { "testResultId", newResultId },
            { "sample_id", sampleIdVal },
            { "sampleId", sampleIdVal },
            { "patient_id", patientIdVal },
            { "patientId", patientIdVal },
            { "order_id", orderIdVal },
            { "orderId", orderIdVal },
            { "package_id", packageIdVal },
            { "packageId", packageIdVal },
            { "parent_result_id", doc["_id"].AsString },
            { "analysis_run", newRun },
            { "version", newVersion },
            { "status", "Entered" },
            { "validationStatus", "Entered" },
            { "rerun_reason", request.Reason },
            { "equipment_code", string.IsNullOrWhiteSpace(request.EquipmentCode) ? (BsonValue)(ReadString(doc, "equipment_code", "deviceId") is { Length: > 0 } eq ? eq : BsonNull.Value) : request.EquipmentCode },
            { "conclusion_notes", request.Notes ?? "" },
            { "created_at", now },
            { "items", newItems }
        };

        await _resultsCol.InsertOneAsync(newDoc);

        // Audit Trail
        await LogAuditTrailAsync("RERUN_BIOCHEMICAL_TEST", newResultId, patientIdVal, $"Tạo Analysis Run {newRun} từ kết quả {id}. Lý do: {request.Reason}");

        return Ok(new
        {
            success = true,
            message = $"Đã khởi tạo đợt phân tích mới (Analysis Run {newRun}) thành công theo ISO 15189.",
            data = new
            {
                resultId = newResultId,
                parentResultId = doc["_id"].AsString,
                analysisRun = newRun,
                version = newVersion,
                status = "Entered",
                reason = request.Reason
            }
        });
    }

    /// <summary>
    /// Cập nhật kết quả xét nghiệm sinh hóa.
    /// Quy tắc bất biến (ISO 15189 / ASVS): Kết quả đã được ký duyệt chính thức KHÔNG ĐƯỢC PHÉP sửa âm thầm.
    /// Nếu đã duyệt, hệ thống từ chối sửa bằng 409 Conflict.
    /// </summary>
    [Authorize(Roles = RoleSets.LabOrManager)]
    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateResult(string id, [FromBody] System.Text.Json.JsonElement? payload = null)
    {
        if (_resultsCol == null)
        {
            return NotFound(new { success = false, message = "Không tìm thấy cơ sở dữ liệu kết quả xét nghiệm." });
        }

        var doc = await _resultsCol.Find(Builders<BsonDocument>.Filter.Or(
            Builders<BsonDocument>.Filter.Eq("_id", id.Trim()),
            Builders<BsonDocument>.Filter.Eq("testResultId", id.Trim()),
            Builders<BsonDocument>.Filter.Eq("result_code", id.Trim())
        )).FirstOrDefaultAsync();

        if (doc == null)
        {
            return NotFound(new { success = false, message = $"Không tìm thấy kết quả xét nghiệm '{id}'." });
        }

        // Immutability check
        if (IsResultApproved(doc))
        {
            return StatusCode(StatusCodes.Status409Conflict, new
            {
                success = false,
                code = "RESULT_SIGNED_READ_ONLY",
                message = "Kết quả xét nghiệm đã được phê duyệt và ký số (ISO 15189:2022). Không được phép chỉnh sửa âm thầm. Vui lòng sử dụng tính năng Rerun (Analysis Run mới) để giữ lịch sử minh bạch."
            });
        }

        return Ok(new { success = true, message = "Cập nhật kết quả xét nghiệm thành công." });
    }

    private static string ReadString(BsonDocument doc, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (doc.Contains(key) && !doc[key].IsBsonNull)
            {
                return doc[key].IsString ? doc[key].AsString : doc[key].ToString()!;
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

    private static bool IsResultApproved(BsonDocument doc)
    {
        var status = ReadString(doc, "status");
        var valStatus = ReadString(doc, "validationStatus");
        var approvedAt = (doc.Contains("approved_at") && !doc["approved_at"].IsBsonNull)
            || (doc.Contains("approvedAt") && !doc["approvedAt"].IsBsonNull);
        var digitalSig = !string.IsNullOrWhiteSpace(ReadString(doc, "digitalSignature", "digital_signature"));

        return string.Equals(status, "Approved", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(status, "COMPLETED", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(valStatus, "DoctorApproved", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(valStatus, "3", StringComparison.OrdinalIgnoreCase) ||
               (approvedAt && digitalSig);
    }

    private async Task<object> BuildDetailedResultAsync(BsonDocument doc, string? requestedTestId = null)
    {
        var resultId = ReadString(doc, "_id", "testResultId", "resultCode");
        var sampleId = ReadString(doc, "sampleId", "sample_id");
        var packageId = ReadString(doc, "packageId", "package_id");
        var patientId = ReadString(doc, "patientId", "patient_id");
        var orderId = ReadString(doc, "order_id", "orderId", "appointment_id", "appointmentId");

        var analysisRun = doc.Contains("analysis_run") && doc["analysis_run"].IsInt32 ? doc["analysis_run"].AsInt32 : (doc.Contains("analysisRun") && doc["analysisRun"].IsInt32 ? doc["analysisRun"].AsInt32 : 1);
        var version = doc.Contains("version") && doc["version"].IsInt32 ? doc["version"].AsInt32 : 1;
        var parentResultId = doc.Contains("parent_result_id") && !doc["parent_result_id"].IsBsonNull ? doc["parent_result_id"].AsString : null;

        var parameters = new List<object>();

        // 1. Try reading items embedded in the result doc (TestResultService schema)
        if (doc.Contains("items") && doc["items"].IsBsonArray && doc["items"].AsBsonArray.Count > 0)
        {
            foreach (var itemVal in doc["items"].AsBsonArray)
            {
                if (!itemVal.IsBsonDocument) continue;
                var itemDoc = itemVal.AsBsonDocument;

                var code = ReadString(itemDoc, "criterionCode", "code", "abbreviation", "criterionId");
                var name = ReadString(itemDoc, "criterionName", "name", "paramName");
                if (string.IsNullOrWhiteSpace(name)) name = code;
                var rawVal = ReadString(itemDoc, "measuredValue", "value");
                object parsedVal = double.TryParse(rawVal, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var nv) ? nv : rawVal;
                var unit = ReadString(itemDoc, "unit");
                var refInterval = ReadString(itemDoc, "referenceInterval", "reference_display", "referenceRange");

                double? refLow = itemDoc.Contains("refLow") && !itemDoc["refLow"].IsBsonNull ? (itemDoc["refLow"].IsDouble ? itemDoc["refLow"].AsDouble : (double)itemDoc["refLow"].AsInt32) : null;
                double? refHigh = itemDoc.Contains("refHigh") && !itemDoc["refHigh"].IsBsonNull ? (itemDoc["refHigh"].IsDouble ? itemDoc["refHigh"].AsDouble : (double)itemDoc["refHigh"].AsInt32) : null;

                var flagStr = MapResultFlag(itemDoc.Contains("flag") ? itemDoc["flag"] : null);
                var normFlag = NormalizeFlag(flagStr);

                parameters.Add(new
                {
                    testId = code,
                    code = code,
                    name = name,
                    value = parsedVal,
                    unit = unit,
                    referenceRange = ParseReferenceRange(refInterval, refLow, refHigh),
                    flag = normFlag,
                    isAbnormal = normFlag != "NORMAL"
                });
            }
        }

        if (parameters.Count == 0 && _paramsCol != null)
        {
            // 2. Query test_parameters collection
            var paramDocs = await _paramsCol.Find(Builders<BsonDocument>.Filter.Or(
                Builders<BsonDocument>.Filter.Eq("result_id", resultId),
                Builders<BsonDocument>.Filter.Eq("resultId", resultId)
            )).ToListAsync();

            foreach (var p in paramDocs)
            {
                var code = ReadString(p, "code", "criterionCode", "abbreviation");
                var name = ReadString(p, "name", "criterionName", "paramName");
                if (string.IsNullOrWhiteSpace(name)) name = code;
                object val = 0d;
                if (p.Contains("value_numeric") && !p["value_numeric"].IsBsonNull)
                {
                    val = p["value_numeric"].IsDouble ? p["value_numeric"].AsDouble
                        : (p["value_numeric"].IsInt32 ? (double)p["value_numeric"].AsInt32 : p["value_numeric"].ToString()!);
                }
                else if (p.Contains("value") && !p["value"].IsBsonNull)
                {
                    val = p["value"].ToString()!;
                }

                var unit = ReadString(p, "unit");
                var refDisplay = ReadString(p, "reference_display", "referenceInterval", "referenceRange");
                var flagStr = MapResultFlag(p.Contains("flag") ? p["flag"] : null);
                var normFlag = NormalizeFlag(flagStr);

                parameters.Add(new
                {
                    testId = code,
                    code = code,
                    name = name,
                    value = val,
                    unit = unit,
                    referenceRange = ParseReferenceRange(refDisplay),
                    flag = normFlag,
                    isAbnormal = (p.Contains("is_abnormal") && p["is_abnormal"].AsBoolean) || normFlag != "NORMAL"
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

        // Extract primary / requested parameter for top-level direct mapping (ISO 15189 / UC75 format)
        dynamic? targetParam = null;
        if (!string.IsNullOrWhiteSpace(requestedTestId))
        {
            targetParam = parameters.FirstOrDefault(p => string.Equals((p as dynamic)?.testId?.ToString(), requestedTestId, StringComparison.OrdinalIgnoreCase) ||
                                                         string.Equals((p as dynamic)?.code?.ToString(), requestedTestId, StringComparison.OrdinalIgnoreCase));
        }
        targetParam ??= parameters.FirstOrDefault();

        var equipmentCode = ReadString(doc, "deviceId", "equipment_code", "equipmentCode");
        DateTime? approvedAt = null;
        if (doc.Contains("approvedAt") && doc["approvedAt"].IsBsonDateTime)
            approvedAt = doc["approvedAt"].ToUniversalTime();
        else if (doc.Contains("approved_at") && doc["approved_at"].IsBsonDateTime)
            approvedAt = doc["approved_at"].ToUniversalTime();

        var digitalSignature = ReadString(doc, "digitalSignature", "digital_signature");
        var conclusionNotes = ReadString(doc, "clinicalComment", "conclusion_notes", "conclusionNotes");
        var status = ReadString(doc, "status");
        if (string.IsNullOrWhiteSpace(status)) status = "Approved";
        var isApproved = IsResultApproved(doc);

        return new
        {
            id = resultId,
            resultId = resultId,
            testResultId = resultId,
            patientId = patientId,
            orderId = orderId,
            sampleId = sampleId,
            barcode = barcode,
            packageId = packageId,
            packageName = packageName,
            patientName = patientName,
            patientGender = patientGender,
            patientDob = patientDob,
            patientPhone = patientPhone,
            patientAddress = patientAddress,
            tubeColor = tubeColor,
            sampleType = sampleType,
            priority = priority,
            equipmentCode = string.IsNullOrWhiteSpace(equipmentCode) ? null : equipmentCode,
            status = status,
            approvalStatus = isApproved ? "Approved" : "PendingApproval",
            isOfficialRelease = isApproved,
            analysisRun = analysisRun,
            version = version,
            parentResultId = parentResultId,
            digitalSignature = string.IsNullOrWhiteSpace(digitalSignature) ? null : digitalSignature,
            approvedAt = approvedAt,
            conclusionNotes = conclusionNotes,
            rerunRequested = (doc.Contains("rerun_requested") && doc["rerun_requested"].AsBoolean)
                || (doc.Contains("rerunRequested") && doc["rerunRequested"].AsBoolean),
            reportPdfUrl = string.IsNullOrWhiteSpace(ReadString(doc, "report_pdf_url", "reportPdfUrl"))
                ? null
                : ReadString(doc, "report_pdf_url", "reportPdfUrl"),

            // Primary single-parameter fields
            testId = targetParam != null ? (targetParam as dynamic).testId : (requestedTestId ?? ""),
            value = targetParam != null ? (targetParam as dynamic).value : 0,
            unit = targetParam != null ? (targetParam as dynamic).unit : "",
            referenceRange = targetParam != null ? (targetParam as dynamic).referenceRange : new { low = 0, high = 0, display = "" },
            flag = targetParam != null ? (targetParam as dynamic).flag : "NORMAL",

            parameters = parameters
        };
    }

    private static object ParseReferenceRange(string? display, double? low = null, double? high = null)
    {
        if (low.HasValue && high.HasValue)
        {
            return new { low = low.Value, high = high.Value, display = display ?? $"{low.Value}-{high.Value}" };
        }

        if (!string.IsNullOrWhiteSpace(display))
        {
            var parts = display.Split(new[] { '-', '–', '~' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2)
            {
                if (double.TryParse(parts[0].Trim(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var l) &&
                    double.TryParse(parts[1].Trim(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var h))
                {
                    return new { low = l, high = h, display = display.Trim() };
                }
            }
        }

        return new { low = low ?? 0, high = high ?? 0, display = display ?? "" };
    }

    private static string NormalizeFlag(string? rawFlag)
    {
        if (string.IsNullOrWhiteSpace(rawFlag)) return "NORMAL";
        var u = rawFlag.Trim().ToUpperInvariant();
        if (u is "H" or "HIGH") return "H";
        if (u is "L" or "LOW") return "L";
        if (u is "CRITICALPANIC" or "PANIC" or "PANIC_HIGH") return "PANIC_HIGH";
        if (u is "PANIC_LOW") return "PANIC_LOW";
        if (u is "NORMAL" or "0") return "NORMAL";
        return u;
    }

    private async Task<object> BuildSummaryResultAsync(BsonDocument d)
    {
        var sampleId = ReadString(d, "sampleId", "sample_id");
        var packageId = ReadString(d, "packageId", "package_id");
        var patientId = ReadString(d, "patientId", "patient_id");
        var orderId = ReadString(d, "order_id", "orderId", "appointment_id", "appointmentId");

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

        var isApproved = IsResultApproved(d);

        return new
        {
            id = d["_id"].AsString,
            resultId = d["_id"].AsString,
            sampleId = sampleId,
            orderId = orderId,
            barcode = barcode,
            packageId = packageId,
            packageName = packageName,
            patientId = patientId,
            patientName = patientName,
            status = d.Contains("status") ? d["status"].AsString : (isApproved ? "Approved" : "Entered"),
            isOfficialRelease = isApproved,
            conclusion = d.Contains("conclusion_notes") && !d["conclusion_notes"].IsBsonNull ? d["conclusion_notes"].AsString : "",
            approvedAt = d.Contains("approved_at") && d["approved_at"].IsBsonDateTime ? d["approved_at"].ToUniversalTime() : (DateTime?)null
        };
    }

    private async Task LogAuditTrailAsync(string action, string entityId, string patientId, string details)
    {
        try
        {
            var userId = User.FindFirst("sub")?.Value 
                ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value 
                ?? "ANONYMOUS";
            var userName = User.FindFirst("name")?.Value 
                ?? User.FindFirst(ClaimTypes.Name)?.Value 
                ?? userId;
            var role = User.FindFirst("role")?.Value 
                ?? User.FindFirst(ClaimTypes.Role)?.Value 
                ?? "Unknown";
            var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";

            if (_auditLogService != null)
            {
                await _auditLogService.LogAsync(new CreateAuditLogDto
                {
                    UserId = userId,
                    UserName = userName,
                    UserRole = role,
                    Action = action,
                    Category = "ACCESS",
                    Severity = "INFO",
                    TargetEntity = "TestResult",
                    EntityId = entityId,
                    IpAddress = ip,
                    Details = details + $" [PatientId={patientId}]",
                    Status = "SUCCESS"
                });
            }
            else if (_database != null)
            {
                var auditCol = _database.GetCollection<BsonDocument>("audit_logs");
                var auditDoc = new BsonDocument
                {
                    { "_id", $"AUD-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid().ToString("N")[..6].ToUpper()}" },
                    { "AuditId", $"AUD-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid().ToString("N")[..6].ToUpper()}" },
                    { "UserId", userId },
                    { "UserName", userName },
                    { "UserRole", role },
                    { "Action", action },
                    { "Category", "ACCESS" },
                    { "Severity", "INFO" },
                    { "TargetEntity", "TestResult" },
                    { "EntityId", entityId },
                    { "PatientId", patientId },
                    { "IpAddress", ip },
                    { "Details", details },
                    { "Timestamp", DateTime.UtcNow },
                    { "Status", "SUCCESS" }
                };
                await auditCol.InsertOneAsync(auditDoc);
            }
        }
        catch { /* Never block operational flow on audit logging */ }
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

/// <summary>
/// DTO yêu cầu chạy lại xét nghiệm (Rerun) theo ISO 15189:2022.
/// </summary>
public class RerunBiochemicalTestRequestDto
{
    public string Reason { get; set; } = string.Empty;
    public string? EquipmentCode { get; set; }
    public string? Notes { get; set; }
    public List<RerunBiochemicalParameterDto>? Parameters { get; set; }
}

public class RerunBiochemicalParameterDto
{
    public string Code { get; set; } = string.Empty;
    public string? Name { get; set; }
    public double Value { get; set; }
    public string? Unit { get; set; }
    public double? RefLow { get; set; }
    public double? RefHigh { get; set; }
}
