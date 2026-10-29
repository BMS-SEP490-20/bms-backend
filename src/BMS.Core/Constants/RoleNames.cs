namespace BMS.Core.Constants;

/// <summary>
/// Single source of truth for the six BMS role names carried in the JWT <c>role</c> claim
/// (decision 30/10/2026 point 1: 1 Admin, 2 Manager, 3 Lab, 4 Doctor, 5 Receptionist, 6 Patient).
/// </summary>
/// <remarks>
/// WHY constants: <c>[Authorize(Roles = ...)]</c> compares role strings ordinally (case-sensitive),
/// so a single typo such as <c>LabTechnician</c> or <c>lab_user</c> silently locks the intended role out.
/// Every attribute must use these constants or the combined sets in <see cref="RoleSets"/>.
/// </remarks>
public static class RoleNames
{
    /// <summary>Role 1 – system administration (accounts, roles, backup). No clinical data.</summary>
    public const string Admin = "Admin";
    /// <summary>Role 2 – Quản lý (absorbs LabHead). Only role that signs results, unlocks Westgard, edits panic thresholds.</summary>
    public const string Manager = "Manager";
    /// <summary>Role 3 – Lab (absorbs LabTech, Phlebotomist, WarehouseStaff).</summary>
    public const string Lab = "Lab";
    /// <summary>Role 4 – Bác sĩ.</summary>
    public const string Doctor = "Doctor";
    /// <summary>Role 5 – Lễ tân (absorbs Cashier, Nurse). Only role that takes cash/POS payments.</summary>
    public const string Receptionist = "Receptionist";
    /// <summary>Role 6 – Bệnh nhân. Own data only.</summary>
    public const string Patient = "Patient";

    /// <summary>All six canonical names, indexed by role ID (index 0 unused).</summary>
    private static readonly string[] ById = ["", Admin, Manager, Lab, Doctor, Receptionist, Patient];

    /// <summary>Maps a seeded role ID (1–6) to its canonical claim name; any other ID returns <c>null</c>.</summary>
    public static string? FromRoleId(int? roleId)
        => roleId is >= 1 and <= 6 ? ById[roleId.Value] : null;

    /// <summary>Maps a canonical role name back to its seeded ID; unknown names return <c>null</c>.</summary>
    public static int? ToRoleId(string? roleName)
    {
        var canonical = Normalize(roleName);
        return canonical is null ? null : Array.IndexOf(ById, canonical);
    }

    /// <summary>
    /// Converts legacy role names still stored in old <c>roles</c> documents (LabHead, LabTech, Cashier…)
    /// to the canonical name. Used only when issuing tokens, never for authorization decisions.
    /// </summary>
    /// <remarks>WHY: existing DB rows predate the 6-role decision; mapping at token issue avoids a data migration.</remarks>
    public static string? Normalize(string? roleName)
    {
        if (string.IsNullOrWhiteSpace(roleName)) return null;
        return roleName.Trim().ToUpperInvariant() switch
        {
            "ADMIN" or "ADMINISTRATOR" => Admin,
            "MANAGER" or "LABHEAD" or "LAB_HEAD" => Manager,
            "LAB" or "LABTECH" or "LABTECHNICIAN" or "LAB_TECHNICIAN" or "LABORATORYUSER" or "LABUSER" or "LAB_USER"
                or "PHLEBOTOMIST" or "WAREHOUSESTAFF" or "WAREHOUSE" => Lab,
            "DOCTOR" => Doctor,
            "RECEPTIONIST" or "CASHIER" or "NURSE" => Receptionist,
            "PATIENT" => Patient,
            _ => null
        };
    }
}

/// <summary>
/// Comma-joined role sets for <c>[Authorize(Roles = ...)]</c>, named after the matrix rows they implement
/// (<c>/workspace/quy-trinh/ROLE-PERMISSION-MATRIX.md</c>, TIEU-CHI §1.2).
/// </summary>
public static class RoleSets
{
    private const string C = ",";

    /// <summary>Any of the six roles (account self-service, own notifications).</summary>
    public const string AnyRole = RoleNames.Admin + C + RoleNames.Manager + C + RoleNames.Lab + C + RoleNames.Doctor + C + RoleNames.Receptionist + C + RoleNames.Patient;
    /// <summary>All staff roles (everyone except Patient).</summary>
    public const string AllStaff = RoleNames.Admin + C + RoleNames.Manager + C + RoleNames.Lab + C + RoleNames.Doctor + C + RoleNames.Receptionist;
    /// <summary>Staff that may see clinical/lab data: Manager, Lab, Doctor (Admin excluded by decision 10).</summary>
    public const string ClinicalStaff = RoleNames.Manager + C + RoleNames.Lab + C + RoleNames.Doctor;
    /// <summary>Clinical staff plus the Patient (who is then restricted to own records by the service).</summary>
    public const string ClinicalStaffOrPatient = ClinicalStaff + C + RoleNames.Patient;
    /// <summary>Clinical staff plus Receptionist (patient lookup UC30, order list UC32, tracking search UC48).</summary>
    public const string ClinicalStaffOrReception = ClinicalStaff + C + RoleNames.Receptionist;
    /// <summary>Lab operations with Manager oversight (QC, verification, sample status).</summary>
    public const string LabOrManager = RoleNames.Lab + C + RoleNames.Manager;
    /// <summary>System administration and management (lab rooms UC34, lab profiles UC61, IoT config UC67).</summary>
    public const string AdminOrManager = RoleNames.Admin + C + RoleNames.Manager;
    /// <summary>Inventory read (UC56): Admin, Manager, Lab. Doctor denied (decision 10).</summary>
    public const string InventoryRead = RoleNames.Admin + C + RoleNames.Manager + C + RoleNames.Lab;
    /// <summary>Inventory write (UC56): Manager, Lab.</summary>
    public const string InventoryWrite = RoleNames.Manager + C + RoleNames.Lab;
    /// <summary>Shift viewing (UC35/36): every staff role.</summary>
    public const string ShiftViewers = AllStaff;
    /// <summary>Personal schedule (UC31): Doctor, Lab, Receptionist (own).</summary>
    public const string ScheduleOwners = RoleNames.Doctor + C + RoleNames.Lab + C + RoleNames.Receptionist;
    /// <summary>Lab and Receptionist (barcode printing UC39, sample check-in UC41).</summary>
    public const string LabOrReception = RoleNames.Lab + C + RoleNames.Receptionist;
    /// <summary>Doctor and Lab (medical notes UC51, test notes UC52).</summary>
    public const string DoctorOrLab = RoleNames.Doctor + C + RoleNames.Lab;
    /// <summary>Receptionist and Patient (own payments, appointments booking).</summary>
    public const string ReceptionOrPatient = RoleNames.Receptionist + C + RoleNames.Patient;
}
