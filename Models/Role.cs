

namespace iam_service.Modals
{
    public partial class Role
    {
        public Role()
        {
            Accounts = new HashSet<Account>();
        }

        public int RoleId { get; set; }
        public string RoleName { get; set; } = null!;
        public string? Description { get; set; }

        [System.ComponentModel.DataAnnotations.Schema.NotMapped]
        public virtual ICollection<Account> Accounts { get; set; }
    }
}
