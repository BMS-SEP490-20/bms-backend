using Microsoft.EntityFrameworkCore;
using payment_service.Data;
using payment_service.Models;

namespace payment_service.Repositories
{
    public class PaymentRepository : IPaymentRepository
    {
        private readonly PaymentDbContext _db;

        public PaymentRepository(PaymentDbContext db)
        {
            _db = db;
        }

        // =============================
        // 🟢 CREATE
        // =============================
        public async Task<Payment> CreateAsync(Payment payment)
        {
            // Always ensure PaymentId is unique and not empty
            bool isInvalidId = payment.PaymentId == Guid.Empty ||
                await _db.Payments.AnyAsync(p => p.PaymentId == payment.PaymentId);

            if (isInvalidId)
            {
                Guid newId;
                do
                {
                    newId = Guid.NewGuid();
                }
                while (await _db.Payments.AnyAsync(p => p.PaymentId == newId));
                payment.PaymentId = newId;
            }

            payment.CreatedAt = DateTime.UtcNow;
            _db.Payments.Add(payment);
            await _db.SaveChangesAsync();
            return payment;
        }

        // =============================
        // 🟢 READ (GET)
        // =============================
        public Task<Payment?> GetByIdAsync(Guid paymentId)
        {
            return _db.Payments.FirstOrDefaultAsync(p => p.PaymentId == paymentId);
        }

        public Task<Payment?> GetByTxnRefAsync(string txnRef)
        {
            return _db.Payments.FirstOrDefaultAsync(p => p.TxnRef == txnRef);
        }

        public Task<IEnumerable<Payment>> GetByPatientIdAsync(Guid patientId)
        {
            return Task.FromResult<IEnumerable<Payment>>(
                _db.Payments.Where(p => p.PatientId == patientId).AsEnumerable()
            );
        }

        public Task<IEnumerable<Payment>> GetAllAsync()
        {
            return Task.FromResult<IEnumerable<Payment>>(_db.Payments.AsEnumerable());
        }

        // =============================
        // 🟢 UPDATE
        // =============================
        public async Task UpdateAsync(Payment payment)
        {
            _db.Payments.Update(payment);
            await _db.SaveChangesAsync();
        }

        // =============================
        // 🟡 NEW: Pagination methods
        // =============================

        // Lấy tất cả Payment có phân trang
        public async Task<IEnumerable<Payment>> GetAllPagedAsync(int page, int pageSize)
        {
            return await _db.Payments
                .OrderByDescending(p => p.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();
        }

        // Lấy Payment theo bệnh nhân có phân trang
        public async Task<IEnumerable<Payment>> GetByPatientIdPagedAsync(Guid patientId, int page, int pageSize)
        {
            return await _db.Payments
                .Where(p => p.PatientId == patientId)
                .OrderByDescending(p => p.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();
        }

        // Đếm tổng số Payment
        public async Task<int> CountAllAsync()
        {
            return await _db.Payments.CountAsync();
        }

        // Đếm tổng Payment của 1 bệnh nhân
        public async Task<int> CountByPatientIdAsync(Guid patientId)
        {
            return await _db.Payments.CountAsync(p => p.PatientId == patientId);
        }
    }
}
