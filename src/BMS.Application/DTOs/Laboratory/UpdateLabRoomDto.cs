namespace Testorder_service.Models.DTO
{
    public class UpdateLabRoomDto
    {
        public string? RoomName { get; set; }
        public string? RoomLocation { get; set; }
        public string? RoomType { get; set; }
        public bool? IsAvailable { get; set; }
        public string? AssignedEquipment { get; set; }
        public string? TechnicianInCharge { get; set; }
    }
}
