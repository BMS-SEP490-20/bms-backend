using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using BMS.Application.DTOs.Backup;
using BMS.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BMS.Api.Controllers.Backup;

/// <summary>
/// Controller for Database Backup and Restoration procedures (UC80 / BR-242).
/// Specifically handles archiving patient diagnostic lab test records, verifying SHA-256 integrity, and authorized restoration.
/// Conforms to: Production Medical Standards, ISO 15189 Quality Management & AGENTS-CODE.md.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class BackupController : ControllerBase
{
    private readonly IBackupService _backupService;

    public BackupController(IBackupService backupService)
    {
        _backupService = backupService;
    }

    /// <summary>
    /// Creates a new backup snapshot of patient test results or full database.
    /// </summary>
    [HttpPost("create")]
    public async Task<IActionResult> CreateBackup(
        [FromBody] CreateBackupRequestDto dto,
        CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(new { success = false, message = "Dữ liệu yêu cầu không hợp lệ.", errors = ModelState });
        }

        string operatorId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "USR-ADMIN-01";
        string operatorName = User.FindFirstValue(ClaimTypes.Name) ?? "Hứa Khánh Duy (Lab Manager)";

        var (success, message, data) = await _backupService.CreateBackupAsync(dto, operatorId, operatorName, ct);
        if (!success)
        {
            return BadRequest(new { success = false, message });
        }

        return Ok(new { success = true, message, data });
    }

    /// <summary>
    /// Retrieves paginated list of backup snapshots with scope and status filters.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetBackups(
        [FromQuery] BackupQueryParameters query,
        CancellationToken ct)
    {
        var result = await _backupService.GetBackupsPagedAsync(query, ct);
        return Ok(new { success = true, data = result });
    }

    /// <summary>
    /// Retrieves detailed metadata of a specific backup snapshot.
    /// </summary>
    [HttpGet("{id}")]
    public async Task<IActionResult> GetBackupById(
        string id,
        CancellationToken ct)
    {
        var backup = await _backupService.GetBackupByIdAsync(id, ct);
        if (backup == null)
        {
            return NotFound(new { success = false, message = $"Không tìm thấy bản sao lưu với mã '{id}'." });
        }

        return Ok(new { success = true, data = backup });
    }

    /// <summary>
    /// Performs SHA-256 cryptographic integrity verification on the backup archive.
    /// </summary>
    [HttpPost("{id}/verify")]
    public async Task<IActionResult> VerifyIntegrity(
        string id,
        CancellationToken ct)
    {
        var result = await _backupService.VerifyIntegrityAsync(id, ct);
        return Ok(new { success = true, data = result });
    }

    /// <summary>
    /// Performs dry-run restoration validation (schema match, record counts, checksum validation).
    /// </summary>
    [HttpPost("{id}/restore/validate")]
    public async Task<IActionResult> ValidateRestoreDryRun(
        string id,
        CancellationToken ct)
    {
        var result = await _backupService.ValidateRestoreDryRunAsync(id, ct);
        return Ok(new { success = true, data = result });
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
        dto.BackupId = id;
        string operatorId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "USR-ADMIN-01";
        string operatorName = User.FindFirstValue(ClaimTypes.Name) ?? "Võ Gia Huy (System Admin)";
        string operatorRole = User.FindFirstValue(ClaimTypes.Role) ?? "Admin";

        var result = await _backupService.ExecuteRestoreAsync(dto, operatorId, operatorName, operatorRole, ct);
        if (!result.Success)
        {
            return BadRequest(new { success = false, message = result.Message });
        }

        return Ok(new { success = true, data = result });
    }

    /// <summary>
    /// Downloads the backup archive file.
    /// </summary>
    [HttpGet("{id}/download")]
    public async Task<IActionResult> DownloadBackup(
        string id,
        CancellationToken ct)
    {
        var (success, stream, fileName, contentType) = await _backupService.DownloadBackupAsync(id, ct);
        if (!success || stream == null)
        {
            return NotFound(new { success = false, message = $"Không tìm thấy file sao lưu '{id}'." });
        }

        return File(stream, contentType, fileName);
    }

    /// <summary>
    /// Retrieves historical restoration audit trail logs.
    /// </summary>
    [HttpGet("restore-history")]
    public async Task<IActionResult> GetAllRestoreHistory(
        [FromQuery] string? backupId,
        CancellationToken ct)
    {
        var logs = await _backupService.GetRestoreHistoryAsync(backupId, ct);
        return Ok(new { success = true, data = logs });
    }

    /// <summary>
    /// Retrieves restoration audit trail for a specific backup.
    /// </summary>
    [HttpGet("{id}/restore-history")]
    public async Task<IActionResult> GetRestoreHistoryByBackup(
        string id,
        CancellationToken ct)
    {
        var logs = await _backupService.GetRestoreHistoryAsync(id, ct);
        return Ok(new { success = true, data = logs });
    }

    /// <summary>
    /// Retrieves overall backup and storage metrics.
    /// </summary>
    [HttpGet("stats")]
    public async Task<IActionResult> GetStats(CancellationToken ct)
    {
        var stats = await _backupService.GetStatsAsync(ct);
        return Ok(new { success = true, data = stats });
    }

    /// <summary>
    /// Deletes a specific backup snapshot.
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteBackup(string id, CancellationToken ct)
    {
        bool deleted = await _backupService.DeleteBackupAsync(id, ct);
        if (!deleted)
        {
            return NotFound(new { success = false, message = $"Không tìm thấy bản sao lưu '{id}' để xóa." });
        }

        return Ok(new { success = true, message = "Xóa bản sao lưu thành công." });
    }
}
