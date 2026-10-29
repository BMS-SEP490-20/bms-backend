using BMS.Core.Entities;

namespace BMS.Core.Interfaces
{
    /// <summary>Data access for payment transactions (I3-019) in the <c>payments</c> collection.</summary>
    public interface IPaymentRepository
    {
        // ===== CRUD cơ bản =====
        /// <summary>Inserts a payment (a new id is assigned when empty).</summary>
        Task<Payment> CreateAsync(Payment payment, CancellationToken ct = default);
        /// <summary>Finds a payment by id.</summary>
        Task<Payment?> GetByIdAsync(Guid paymentId, CancellationToken ct = default);
        /// <summary>Finds a payment whose TxnRef or TransactionCode equals the reference (case-insensitive).</summary>
        Task<Payment?> GetByReferenceAsync(string reference, CancellationToken ct = default);
        /// <summary>Atomically sets status and gateway references; returns the updated payment or null.</summary>
        Task<Payment?> UpdateStatusAsync(Guid paymentId, string status, string? transactionCode, string? txnRef, CancellationToken ct = default);

        // ===== Thêm mới: hỗ trợ PHÂN TRANG =====
        /// <summary>Returns one page of payments, newest first.</summary>
        Task<IReadOnlyList<Payment>> GetAllPagedAsync(int page, int pageSize, CancellationToken ct = default);
        /// <summary>Returns one page of a patient's payments, newest first.</summary>
        Task<IReadOnlyList<Payment>> GetByPatientIdPagedAsync(string patientId, int page, int pageSize, CancellationToken ct = default);
        /// <summary>Counts all payments.</summary>
        Task<long> CountAllAsync(CancellationToken ct = default);
        /// <summary>Counts a patient's payments.</summary>
        Task<long> CountByPatientIdAsync(string patientId, CancellationToken ct = default);
    }
}
