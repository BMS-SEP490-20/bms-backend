namespace BMS.Application.DTOs;

public class DashboardStatsDto
{
    public string Role { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    // Common Cards / Metrics
    public List<StatCardDto> SummaryCards { get; set; } = new();

    // Role-specific sections
    public PatientDashboardData? PatientData { get; set; }
    public DoctorDashboardData? DoctorData { get; set; }
    public LabUserDashboardData? LabUserData { get; set; }
    public ReceptionistDashboardData? ReceptionistData { get; set; }
    public ManagerDashboardData? ManagerData { get; set; }
}

public class StatCardDto
{
    public string Key { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public string ColorVariant { get; set; } = "primary"; // primary, success, warning, danger, info
    public string? Subtitle { get; set; }
}

public class PatientDashboardData
{
    public int UpcomingAppointmentsCount { get; set; }
    public int ReadyResultsCount { get; set; }
    public int PendingResultsCount { get; set; }
    public List<DashboardAppointmentItemDto> RecentAppointments { get; set; } = new();
    public List<DashboardResultItemDto> RecentResults { get; set; } = new();
}

public class DoctorDashboardData
{
    public int TodayAppointmentsCount { get; set; }
    public int PendingApprovalsCount { get; set; }
    public int ReviewedTodayCount { get; set; }
    public List<DashboardAppointmentItemDto> TodaySchedule { get; set; } = new();
}

public class LabUserDashboardData
{
    public int QueueWaitingCount { get; set; }
    public int InProgressCount { get; set; }
    public int CompletedTodayCount { get; set; }
    public int RerunRequestedCount { get; set; }
    public List<LabQueueItemDto> PrioritySamples { get; set; } = new();
}

public class ReceptionistDashboardData
{
    public int CheckInsToday { get; set; }
    public int WaitingQueueCount { get; set; }
    public int TotalWalkIns { get; set; }
    public decimal PaymentsCollectedToday { get; set; }
}

public class ManagerDashboardData
{
    public int TotalOrdersToday { get; set; }
    public decimal RevenueToday { get; set; }
    public double AverageTatMinutes { get; set; } // Turnaround Time
    public int ActiveAlertsCount { get; set; }
    public double ColdStorageTemperature { get; set; }
}

public class DashboardAppointmentItemDto
{
    public string Id { get; set; } = string.Empty;
    public string AppointmentCode { get; set; } = string.Empty;
    public string PatientName { get; set; } = string.Empty;
    public string PackageName { get; set; } = string.Empty;
    public DateTime AppointmentTime { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? DoctorName { get; set; }
}

public class DashboardResultItemDto
{
    public string TestOrderId { get; set; } = string.Empty;
    public string PackageName { get; set; } = string.Empty;
    public DateTime ReleasedAt { get; set; }
    public string Status { get; set; } = string.Empty;
    public bool HasAbnormalFlag { get; set; }
}
