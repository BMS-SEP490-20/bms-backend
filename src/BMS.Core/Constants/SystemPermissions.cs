namespace BMS.Core.Constants;

/// <summary>
/// Granular permission constants categorized by clinical and operational modules.
/// Conforms to: NIST RBAC Standard & AGENTS-BUSINESS-RULES.md.
/// </summary>
public static class SystemPermissions
{
    // 1. Identity & Access Management (IAM)
    public const string IamView = "IAM_VIEW";
    public const string IamManage = "IAM_MANAGE";

    // 2. Reception & Appointments
    public const string ReceptionQueue = "RECEPTION_QUEUE";
    public const string ReceptionCheckin = "RECEPTION_CHECKIN";

    // 3. Clinical Examination (EMR)
    public const string ClinicExam = "CLINIC_EXAM";
    public const string ClinicOrder = "CLINIC_ORDER";

    // 4. Laboratory Operations
    public const string LabTest = "LAB_TEST";
    public const string LabApprove = "LAB_APPROVE";

    // 5. Billing & POS
    public const string BillingPos = "BILLING_POS";
    public const string BillingRefund = "BILLING_REFUND";

    // 6. Inventory & Bio-storage IoT
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
