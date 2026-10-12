namespace BMS.Core.Entities;

/// <summary>Vai trò – map với collection "roles" (_id = RoleId).</summary>
public class Role
{
    public int Id { get; set; }
    public string RoleName { get; set; } = default!;
    public string? Description { get; set; }
    public List<string> Permissions { get; set; } = new();
}
