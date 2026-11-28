namespace BMS.Core.Constants;

/// <summary>
/// Granular permission constants categorized by clinical and operational modules.
/// Conforms to: NIST RBAC Standard & AGENTS-BUSINESS-RULES.md.
/// </summary>
public static class SystemPermissions
{
    public const string IamView = "IAM_VIEW";
    public const string IamManage = "IAM_MANAGE";

    public const string ReceptionQueue = "RECEPTION_QUEUE";
    public const string ReceptionCheckin = "RECEPTION_CHECKIN";

    public const string ClinicExam = "CLINIC_EXAM";
    public const string ClinicOrder = "CLINIC_ORDER";

    public const string LabTest = "LAB_TEST";
    public const string LabApprove = "LAB_APPROVE";

    public const string BillingPos = "BILLING_POS";
    public const string BillingRefund = "BILLING_REFUND";

    public const string InventoryView = "INVENTORY_VIEW";
    public const string IotMonitor = "IOT_MONITOR";

    /// <summary>
    /// Groups permissions by module category for frontend matrix accordion rendering.
    /// </summary>
    public static readonly Dictionary<string, List<string>> PermissionMatrix = new()
    {
        {
            "Identity & Access Management (IAM)",
            new List<string> { IamView, IamManage }
        },
        {
            "Reception & Queuing",
            new List<string> { ReceptionQueue, ReceptionCheckin }
        },
        {
            "Clinical Examination (EMR)",
            new List<string> { ClinicExam, ClinicOrder }
        },
        {
            "Laboratory & Testing",
            new List<string> { LabTest, LabApprove }
        },
        {
            "Billing & Cashier",
            new List<string> { BillingPos, BillingRefund }
        },
        {
            "Inventory & IoT Monitoring",
            new List<string> { InventoryView, IotMonitor }
        }
    };
}
