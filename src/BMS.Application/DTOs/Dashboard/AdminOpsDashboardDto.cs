namespace BMS.Application.DTOs.Dashboard;

/// <summary>
/// Operations-only dashboard for the Admin role (UC12).
/// </summary>
/// <remarks>
/// WHY: decision 10 + Quy trình DOT2 follow-up 6 — Admin sees system operations only. This DTO deliberately has
/// no field that can carry patient identity, test results, panic values or sample data; add new metrics here
/// only if they are aggregate operational counts.
/// </remarks>
/// <param name="UsersPerRole">Account count per canonical role name (Admin, Manager, Lab, Doctor, Receptionist, Patient).</param>
/// <param name="LockedAccounts">Accounts with status Locked or an active lockout window.</param>
/// <param name="Logins">Distinct accounts whose last login falls in the last 24 h / 7 days.</param>
/// <param name="Backup">Latest backup run and 7-day success/failure counts.</param>
/// <param name="AuditLogRows">Total rows in the audit log.</param>
/// <param name="IotDevices">Configured IoT devices by status.</param>
/// <param name="Revenue">Total collected revenue (paid invoices), VND.</param>
/// <param name="GeneratedAtUtc">When the snapshot was computed (UTC).</param>
public sealed record AdminOpsDashboardDto(
    IReadOnlyDictionary<string, long> UsersPerRole,
    long LockedAccounts,
    AdminLoginStatsDto Logins,
    AdminBackupStatusDto Backup,
    long AuditLogRows,
    AdminIotDeviceStatsDto IotDevices,
    AdminRevenueDto Revenue,
    DateTime GeneratedAtUtc);

/// <summary>Login activity derived from <c>LastLoginAt</c> (no per-login event log exists yet).</summary>
/// <param name="DistinctAccountsLast24h">Accounts that last signed in during the past 24 hours.</param>
/// <param name="DistinctAccountsLast7d">Accounts that last signed in during the past 7 days.</param>
/// <param name="AccountsWithFailedAttempts">Accounts whose failed-login counter is above zero.</param>
public sealed record AdminLoginStatsDto(long DistinctAccountsLast24h, long DistinctAccountsLast7d, long AccountsWithFailedAttempts);

/// <summary>Backup health.</summary>
/// <param name="LastStatus">Status of the newest backup record, or null when none exists.</param>
/// <param name="LastCreatedAtUtc">Creation time of the newest backup (UTC).</param>
/// <param name="LastCompletedAtUtc">Completion time of the newest backup (UTC).</param>
/// <param name="CompletedLast7d">Completed backups in the last 7 days.</param>
/// <param name="FailedLast7d">Failed or corrupted backups in the last 7 days.</param>
public sealed record AdminBackupStatusDto(string? LastStatus, DateTime? LastCreatedAtUtc, DateTime? LastCompletedAtUtc, long CompletedLast7d, long FailedLast7d);

/// <summary>IoT device counts by configured status.</summary>
/// <param name="Online">Devices whose status is Active or Warning.</param>
/// <param name="Offline">Devices whose status is Inactive (or anything else).</param>
/// <remarks>WHY: devices do not send a heartbeat yet (telemetry is not persisted until Đợt 3 IoT), so
/// "online" is the configured status, not a live connection check.</remarks>
public sealed record AdminIotDeviceStatsDto(long Online, long Offline);

/// <summary>Revenue aggregate.</summary>
/// <param name="TotalCollected">Sum of <c>totalAmount</c> over invoices with status Paid, VND.</param>
/// <param name="PaidInvoiceCount">Number of paid invoices.</param>
public sealed record AdminRevenueDto(decimal TotalCollected, long PaidInvoiceCount);
