using System;
using System.Collections.Generic;

namespace Testorder_service.Models.Entities;

public partial class LabRoom
{
    public string RoomId { get; set; } = null!;

    public string RoomName { get; set; } = null!;

    public string? RoomLocation { get; set; }

    public string? RoomType { get; set; }

    public bool? IsAvailable { get; set; }

    public string? AssignedEquipment { get; set; }

    public string? TechnicianInCharge { get; set; }

    public DateTime? CreatedAt { get; set; }

    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public virtual ICollection<LabAssignment> LabAssignments { get; set; } = new List<LabAssignment>();

    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public virtual ICollection<LabUserProfile> LabUserProfiles { get; set; } = new List<LabUserProfile>();
}
