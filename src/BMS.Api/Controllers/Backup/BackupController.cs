using BMS.Core.Constants;
using BMS.Core.Exceptions;
using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using BMS.Application.DTOs.Backup;
using BMS.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace BMS.Api.Controllers.Backup;

/// <summary>
/// Controller for Database Backup and Restoration procedures (UC80 / BR-242).
/// Specifically handles archiving patient diagnostic lab test records, verifying SHA-256 integrity, and authorized restoration.
/// Conforms to: Production Medical Standards, ISO 15189 Quality Management & AGENTS-CODE.md.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Route("api/v1/backup")]
// WHY: UC80: Admin only (incl. restore validate/execute). (ROLE-PERMISSION-MATRIX §1, TIEU-CHI §1.2)
[Authorize(Roles = RoleNames.Admin)]
public class BackupController : ControllerBase
{
    private readonly IBackupService _backupService;

    public BackupController(IBackupService backupService)
    {
        _backupService = backupService;
    }

    private string CurrentOperatorId =>
        User.FindFirstValue("sub") ?? User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "SYSTEM";

    private string CurrentOperatorName =>
        User.FindFirstValue("name") ?? User.FindFirstValue("fullName") ?? User.FindFirstValue(ClaimTypes.Name) ?? "Quản trị viên hệ thống";

    /// <summary>
    /// Creates a new backup snapshot of patient test results or full database.
    /// </summary>
    [HttpPost("create")]
    public async Task<IActionResult> CreateBackup(
        [FromBody] CreateBackupRequestDto dto,
        CancellationToken ct)
    {
        try
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(new { success = false, code = "MSG-ERR-VALIDATION", message = "Dữ liệu yêu cầu sao lưu không hợp lệ." });
            }

            var (success, message, data) = await _backupService.CreateBackupAsync(dto, CurrentOperatorId, CurrentOperatorName, ct);
            if (!success)
            {
                return BadRequest(new { success = false, code = "MSG-ERR-BACKUP-CREATE", message });
            }

            return Ok(new { success = true, message, data });
        }
        catch (AppException ex)
        {
            return StatusCode(ex.StatusCode, new { success = false, code = ex.Code, message = ex.Message });
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new { success = false, code = "MSG-ERR-INTERNAL", message = "Lỗi hệ thống khi khởi tạo bản sao lưu dữ liệu." });
        }
    }

    /// <summary>
    /// Retrieves paginated list of backup snapshots with scope and status filters.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetBackups(
        [FromQuery] BackupQueryParameters query,
        CancellationToken ct)
    {
        try
        {
            var result = await _backupService.GetBackupsPagedAsync(query, ct);
            return Ok(new { success = true, data = result });
        }
        catch (AppException ex)
        {
            return StatusCode(ex.StatusCode, new { success = false, code = ex.Code, message = ex.Message });
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new { success = false, code = "MSG-ERR-INTERNAL", message = "Lỗi hệ thống khi tải danh sách bản sao lưu." });
        }
    }

    /// <summary>
    /// Retrieves detailed metadata of a specific backup snapshot.
    /// </summary>
    [HttpGet("{id}")]
    public async Task<IActionResult> GetBackupById(
        string id,
        CancellationToken ct)
    {
        try
        {
            var backup = await _backupService.GetBackupByIdAsync(id, ct);
            if (backup == null)
            {
                return NotFound(new { success = false, code = "MSG-ERR-BACKUP-NOT-FOUND", message = $"Không tìm thấy bản sao lưu với mã '{id}'." });
            }

            return Ok(new { success = true, data = backup });
        }
        catch (AppException ex)
        {
            return StatusCode(ex.StatusCode, new { success = false, code = ex.Code, message = ex.Message });
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new { success = false, code = "MSG-ERR-INTERNAL", message = "Lỗi hệ thống khi tải chi tiết bản sao lưu." });
        }
    }

    /// <summary>
    /// Performs SHA-256 cryptographic integrity verification on the backup archive.
    /// </summary>
    [HttpPost("{id}/verify")]
    public async Task<IActionResult> VerifyIntegrity(
        string id,
        CancellationToken ct)
    {
        try
        {
            var result = await _backupService.VerifyIntegrityAsync(id, ct);
            return Ok(new { success = true, data = result });
        }
        catch (AppException ex)
        {
            return StatusCode(ex.StatusCode, new { success = false, code = ex.Code, message = ex.Message });
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new { success = false, code = "MSG-ERR-INTERNAL", message = "Lỗi hệ thống khi xác thực mã băm SHA-256 của bản sao lưu." });
        }
    }

    /// <summary>
    /// Performs dry-run restoration validation (schema match, record counts, checksum validation).
    /// </summary>
    [HttpPost("{id}/restore/validate")]
    public async Task<IActionResult> ValidateRestoreDryRun(
        string id,
        CancellationToken ct)
    {
        try
        {
            var result = await _backupService.ValidateRestoreDryRunAsync(id, ct);
            return Ok(new { success = true, data = result });
        }
        catch (AppException ex)
        {
            return StatusCode(ex.StatusCode, new { success = false, code = ex.Code, message = ex.Message });
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new { success = false, code = "MSG-ERR-INTERNAL", message = "Lỗi hệ thống khi kiểm tra sơ bộ bản sao lưu trước khi khôi phục." });
        }
    }

    /// <summary>
    /// Executes authorized data restoration with confirmation phrase.
    /// </summary>
    [HttpPost("{id}/restore/execute")]
    public async Task<IActionResult> ExecuteRestore(
        string id,
        [FromBody] RestoreRequestDto dto,
        CancellationToken ct)
    {
        try
        {
            dto.BackupId = id;
            string operatorRole = User.FindFirstValue(ClaimTypes.Role) ?? RoleNames.Admin;

            var result = await _backupService.ExecuteRestoreAsync(dto, CurrentOperatorId, CurrentOperatorName, operatorRole, ct);
            if (!result.Success)
            {
                return BadRequest(new { success = false, code = "MSG-ERR-RESTORE-FAILED", message = result.Message });
            }

            return Ok(new { success = true, data = result });
        }
        catch (AppException ex)
        {
            return StatusCode(ex.StatusCode, new { success = false, code = ex.Code, message = ex.Message });
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new { success = false, code = "MSG-ERR-INTERNAL", message = "Lỗi hệ thống khi thực thi khôi phục dữ liệu." });
        }
    }

    /// <summary>
    /// Downloads the backup archive file.
    /// </summary>
    [HttpGet("{id}/download")]
    public async Task<IActionResult> DownloadBackup(
        string id,
        CancellationToken ct)
    {
        try
        {
            var (success, stream, fileName, contentType) = await _backupService.DownloadBackupAsync(id, ct);
            if (!success || stream == null)
            {
                return NotFound(new { success = false, code = "MSG-ERR-BACKUP-NOT-FOUND", message = $"Không tìm thấy file sao lưu '{id}'." });
            }

            return File(stream, contentType, fileName);
        }
        catch (AppException ex)
        {
            return StatusCode(ex.StatusCode, new { success = false, code = ex.Code, message = ex.Message });
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new { success = false, code = "MSG-ERR-INTERNAL", message = "Lỗi hệ thống khi tải tệp sao lưu." });
        }
    }

    /// <summary>
    /// Retrieves historical restoration audit trail logs.
    /// </summary>
    [HttpGet("restore-history")]
    public async Task<IActionResult> GetAllRestoreHistory(
        [FromQuery] string? backupId,
        CancellationToken ct)
    {
        try
        {
            var logs = await _backupService.GetRestoreHistoryAsync(backupId, ct);
            return Ok(new { success = true, data = logs });
        }
        catch (AppException ex)
        {
            return StatusCode(ex.StatusCode, new { success = false, code = ex.Code, message = ex.Message });
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new { success = false, code = "MSG-ERR-INTERNAL", message = "Lỗi hệ thống khi tải nhật ký khôi phục dữ liệu." });
        }
    }

    /// <summary>
    /// Retrieves restoration audit trail for a specific backup.
    /// </summary>
    [HttpGet("{id}/restore-history")]
    public async Task<IActionResult> GetRestoreHistoryByBackup(
        string id,
        CancellationToken ct)
    {
        try
        {
            var logs = await _backupService.GetRestoreHistoryAsync(id, ct);
            return Ok(new { success = true, data = logs });
        }
        catch (AppException ex)
        {
            return StatusCode(ex.StatusCode, new { success = false, code = ex.Code, message = ex.Message });
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new { success = false, code = "MSG-ERR-INTERNAL", message = "Lỗi hệ thống khi tải nhật ký khôi phục của bản sao lưu." });
        }
    }

    /// <summary>
    /// Retrieves overall backup and storage metrics.
    /// </summary>
    [HttpGet("stats")]
    public async Task<IActionResult> GetStats(CancellationToken ct)
    {
        try
        {
            var stats = await _backupService.GetStatsAsync(ct);
            return Ok(new { success = true, data = stats });
        }
        catch (AppException ex)
        {
            return StatusCode(ex.StatusCode, new { success = false, code = ex.Code, message = ex.Message });
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new { success = false, code = "MSG-ERR-INTERNAL", message = "Lỗi hệ thống khi tải thống kê sao lưu." });
        }
    }

    /// <summary>
    /// Deletes a specific backup snapshot.
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteBackup(string id, CancellationToken ct)
    {
        try
        {
            bool deleted = await _backupService.DeleteBackupAsync(id, ct);
            if (!deleted)
            {
                return NotFound(new { success = false, code = "MSG-ERR-BACKUP-NOT-FOUND", message = $"Không tìm thấy bản sao lưu '{id}' để xóa." });
            }

            return Ok(new { success = true, message = "Xóa bản sao lưu thành công." });
        }
        catch (AppException ex)
        {
            return StatusCode(ex.StatusCode, new { success = false, code = ex.Code, message = ex.Message });
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new { success = false, code = "MSG-ERR-INTERNAL", message = "Lỗi hệ thống khi xóa bản sao lưu." });
        }
    }
}
