namespace Testorder_service.Models.DTO
{
    public class LabRoomDto
    {
        public string RoomId { get; set; } = null!;
        public string RoomName { get; set; } = null!;
        public string? RoomLocation { get; set; }
        public string? RoomType { get; set; }
        public bool? IsAvailable { get; set; }
        public DateTime? CreatedAt { get; set; }
    }
}
