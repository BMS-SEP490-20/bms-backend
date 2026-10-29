namespace BMS.Application.DTOs.Laboratory
{
    /// <summary>Payload for creating a lab room.</summary>
    public class CreateLabRoomDto
    {
        [System.ComponentModel.DataAnnotations.Required, System.ComponentModel.DataAnnotations.StringLength(100)]
        public string RoomName { get; set; } = null!;
        public string? RoomLocation { get; set; }
        public string? RoomType { get; set; }
        public bool? IsAvailable { get; set; }
        public string? AssignedEquipment { get; set; }
        public string? TechnicianInCharge { get; set; }
    }
}
