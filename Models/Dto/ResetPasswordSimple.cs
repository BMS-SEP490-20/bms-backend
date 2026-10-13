namespace iam_service.Modals.Dto
{
    public class ResetPasswordSimple
    {
        public string? Email { get; set; }
        public string? OtpCode { get; set; }
        public string? NewPassword { get; set; }
    }
}
