namespace TestOrder.Models.DTO
{
    public class AppointmentReadDto
    {
        public string AppointmentId { get; set; } = null!;
        public string? TestPackageId { get; set; }
        public string? TestPackageName { get; set; }
        public string? DoctorShiftId { get; set; }
        public string? Status { get; set; }
        public string CreatedBy { get; set; } = null!;
        public DateTime? CreatedAt { get; set; }
        public string? LabAssignmentId { get; set; }
        public DateTime? AppointmentTime { get; set; }
        public string? AppointmentType { get; set; }
        public string? ReasonForRescheduling { get; set; }
        public string? GuestName { get; set; }
        public string? GuestPhone { get; set; }
        public int? QueueNumber { get; set; }
    }
}
