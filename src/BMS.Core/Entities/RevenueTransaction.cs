using System;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace BMS.Core.Entities;

/// <summary>
/// Domain entity representing a financial revenue transaction for laboratory orders and services (UC78).
/// Maps to MongoDB collection "revenue_transactions".
/// </summary>
[BsonIgnoreExtraElements]
public class RevenueTransaction
{
    [BsonId]
    [BsonRepresentation(BsonType.String)]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [BsonElement("transaction_number")]
    public string TransactionNumber { get; set; } = string.Empty; // e.g. TXN-2026-000123

    [BsonElement("order_number")]
    public string OrderNumber { get; set; } = string.Empty; // e.g. ORD-2026-8891

    [BsonElement("patient_name")]
    public string PatientName { get; set; } = string.Empty;

    [BsonElement("patient_id")]
    public string PatientId { get; set; } = string.Empty;

    [BsonElement("service_type")]
    public string ServiceType { get; set; } = "InClinic"; // InClinic | HomeCollection

    [BsonElement("category")]
    public string Category { get; set; } = "Biochemistry"; // Biochemistry | Hematology | Immunology | Microbiology | GeneralCheckup

    [BsonElement("gross_amount")]
    public decimal GrossAmount { get; set; }

    [BsonElement("bhyt_coverage_amount")]
    public decimal BhytCoverageAmount { get; set; }

    [BsonElement("discount_amount")]
    public decimal DiscountAmount { get; set; }

    [BsonElement("home_surcharge_amount")]
    public decimal HomeSurchargeAmount { get; set; }

    [BsonElement("net_amount")]
    public decimal NetAmount { get; set; } // Gross - BHYT - Discount + Surcharge

    [BsonElement("payment_method")]
    public string PaymentMethod { get; set; } = "VietQR"; // Cash | VietQR | VNPay | BankTransfer | BhytDirect

    [BsonElement("referral_doctor")]
    public string ReferralDoctor { get; set; } = "BS. Trưởng Khoa";

    [BsonElement("referral_department")]
    public string ReferralDepartment { get; set; } = "Khoa Khám Bệnh";

    [BsonElement("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [BsonElement("cashier_name")]
    public string CashierName { get; set; } = "Thu ngân Nguyễn Tấn Phát";
}
