using System.ComponentModel.DataAnnotations;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace BMS.Core.Entities
{
    /// <summary>
    /// Payment transaction recorded by Generate Receipt (I3-019) and the payment record API.
    /// Persisted in the <c>payments</c> collection. Ported from the legacy payment microservice:
    /// <see cref="PatientId"/> is now the BMS patient id (string), not a Guid.
    /// </summary>
    [BsonIgnoreExtraElements]
    public class Payment
    {
        /// <summary>Payment identifier.</summary>
        [Key]
        [BsonId]
        [BsonRepresentation(BsonType.String)]
        public Guid PaymentId { get; set; }

        /// <summary>BMS patient id.</summary>
        [Required]
        public string PatientId { get; set; } = string.Empty;

        /// <summary>Amount in VND.</summary>
        [Required]
        [BsonRepresentation(BsonType.Decimal128)]
        public decimal Amount { get; set; }

        /// <summary>Gateway transaction code or receipt number.</summary>
        [MaxLength(200)]
        public string? TransactionCode { get; set; }

        // VnPay transaction reference (vnp_TxnRef)
        /// <summary>VNPay transaction reference (vnp_TxnRef) or receipt number.</summary>
        [MaxLength(100)]
        public string? TxnRef { get; set; }

        // Payment status: Pending, Success, Failed
        /// <summary>Payment status: Pending, Success, Failed.</summary>
        [MaxLength(50)]
        public string? Status { get; set; }

        /// <summary>UTC time the payment completed.</summary>
        public DateTime? PaidAt { get; set; }

        /// <summary>UTC creation time.</summary>
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>Free-text notes.</summary>
        public string? Notes { get; set; }
    }
}
