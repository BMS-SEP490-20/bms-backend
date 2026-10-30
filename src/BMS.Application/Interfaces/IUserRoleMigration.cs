namespace BMS.Application.Interfaces;

/// <summary>
/// One-time data migration for the 6-role model: accounts self-registered before Đợt 2 were stored with
/// <c>RoleId = 9</c> (old "Patient" id in the 9-role draft); they must become <c>RoleId = 6</c> (Patient).
/// </summary>
public interface IUserRoleMigration
{
    /// <summary>
    /// Converts every account with the legacy patient role id to the canonical Patient id.
    /// Idempotent: a second run modifies nothing and only refreshes the migration marker.
    /// </summary>
    /// <param name="ct">Cancels the database calls (app shutdown).</param>
    /// <returns>Number of accounts converted in this run (0 when already migrated).</returns>
    Task<long> MigrateLegacyPatientRoleIdAsync(CancellationToken ct);
}
