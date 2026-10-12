namespace iam_service.Modals.Dto
{
    public class AccountDto
    {
        public string Username { get; set; } = null!;

        public string Password { get; set; } = null!;

        public string Email { get; set; } = null!;



        public int RoleId { get; set; }

        public int Status { get; set; }
    }
}
