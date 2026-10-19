namespace TestOrder.Models.DTO
{
    public class AppointmentCreateDto
    {
        public string? TestPackageId { get; set; }
        public string? DoctorShiftId { get; set; }
        public string? Status { get; set; }
        public string CreatedBy { get; set; } = null!;
        public string? LabAssignmentId { get; set; }
        public DateTime? AppointmentTime { get; set; }
        public string? AppointmentType { get; set; }
        public string? GuestName { get; set; }
        public string? GuestPhone { get; set; }
    }
}
