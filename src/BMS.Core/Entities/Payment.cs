using System.ComponentModel.DataAnnotations;

namespace payment_service.Models
{
    public class Payment
    {
        [Key]
        public Guid PaymentId { get; set; }

        [Required]
        public Guid PatientId { get; set; }

        [Required]
        public decimal Amount { get; set; }

        [MaxLength(200)]
        public string? TransactionCode { get; set; }

    // VnPay transaction reference (vnp_TxnRef)
    [MaxLength(100)]
    public string? TxnRef { get; set; }

    // Payment status: Pending, Success, Failed
    [MaxLength(50)]
    public string? Status { get; set; }

        public DateTime? PaidAt { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public string? Notes { get; set; }
    }
}
