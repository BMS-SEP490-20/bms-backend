using payment_service.Models;

namespace payment_service.Repositories
{
    public interface IPaymentRepository
    {
        // ===== CRUD cơ bản =====
        Task<Payment> CreateAsync(Payment payment);
        Task<Payment?> GetByIdAsync(Guid paymentId);
        Task<IEnumerable<Payment>> GetByPatientIdAsync(Guid patientId);
        Task<IEnumerable<Payment>> GetAllAsync();
        Task<Payment?> GetByTxnRefAsync(string txnRef);
        Task UpdateAsync(Payment payment);

        // ===== Thêm mới: hỗ trợ PHÂN TRANG =====
        Task<IEnumerable<Payment>> GetAllPagedAsync(int page, int pageSize);
        Task<IEnumerable<Payment>> GetByPatientIdPagedAsync(Guid patientId, int page, int pageSize);
        Task<int> CountAllAsync();
        Task<int> CountByPatientIdAsync(Guid patientId);
    }
}
