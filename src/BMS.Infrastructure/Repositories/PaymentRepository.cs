using System.Text.RegularExpressions;
using BMS.Core.Entities;
using BMS.Core.Interfaces;
using MongoDB.Bson;
using MongoDB.Driver;

namespace BMS.Infrastructure.Repositories
{
    /// <summary>
    /// MongoDB implementation of <see cref="IPaymentRepository"/> over <c>payments</c>.
    /// Replaces the EF Core PaymentDbContext implementation from the old payment microservice.
    /// </summary>
    public sealed class PaymentRepository : IPaymentRepository
    {
        private readonly IMongoCollection<Payment> _payments;

        /// <summary>Creates the repository bound to <c>payments</c>.</summary>
        public PaymentRepository(IMongoDatabase database)
        {
            _payments = database.GetCollection<Payment>("payments");
        }

        private static FilterDefinitionBuilder<Payment> F => Builders<Payment>.Filter;

        /// <inheritdoc />
        public async Task<Payment> CreateAsync(Payment payment, CancellationToken ct = default)
        {
            // The unique _id index rejects duplicates; Guid v4 collisions are not a practical concern,
            // so the old "loop until unused id" scan is unnecessary.
            if (payment.PaymentId == Guid.Empty) payment.PaymentId = Guid.NewGuid();
            await _payments.InsertOneAsync(payment, cancellationToken: ct);
            return payment;
        }

        /// <inheritdoc />
        public async Task<Payment?> GetByIdAsync(Guid paymentId, CancellationToken ct = default) =>
            await _payments.Find(F.Eq(p => p.PaymentId, paymentId)).FirstOrDefaultAsync(ct);

        /// <inheritdoc />
        public async Task<Payment?> GetByReferenceAsync(string reference, CancellationToken ct = default)
        {
            // One indexed-friendly query instead of "load all payments and filter in memory".
            var exact = new BsonRegularExpression($"^{Regex.Escape(reference)}$", "i");
            return await _payments.Find(F.Or(F.Regex(p => p.TxnRef, exact), F.Regex(p => p.TransactionCode, exact)))
                .FirstOrDefaultAsync(ct);
        }

        /// <inheritdoc />
        public async Task<Payment?> UpdateStatusAsync(Guid paymentId, string status, string? transactionCode, string? txnRef, CancellationToken ct = default)
        {
            var u = Builders<Payment>.Update;
            var updates = new List<UpdateDefinition<Payment>> { u.Set(p => p.Status, status) };
            if (!string.IsNullOrWhiteSpace(transactionCode)) updates.Add(u.Set(p => p.TransactionCode, transactionCode));
            if (!string.IsNullOrWhiteSpace(txnRef)) updates.Add(u.Set(p => p.TxnRef, txnRef));
            return await _payments.FindOneAndUpdateAsync(F.Eq(p => p.PaymentId, paymentId), u.Combine(updates),
                new FindOneAndUpdateOptions<Payment> { ReturnDocument = ReturnDocument.After }, ct);
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<Payment>> GetAllPagedAsync(int page, int pageSize, CancellationToken ct = default) =>
            await _payments.Find(F.Empty).SortByDescending(p => p.CreatedAt).Skip((page - 1) * pageSize).Limit(pageSize).ToListAsync(ct);

        /// <inheritdoc />
        public async Task<IReadOnlyList<Payment>> GetByPatientIdPagedAsync(string patientId, int page, int pageSize, CancellationToken ct = default) =>
            await _payments.Find(F.Eq(p => p.PatientId, patientId)).SortByDescending(p => p.CreatedAt)
                .Skip((page - 1) * pageSize).Limit(pageSize).ToListAsync(ct);

        /// <inheritdoc />
        public Task<long> CountAllAsync(CancellationToken ct = default) =>
            _payments.CountDocumentsAsync(F.Empty, cancellationToken: ct);

        /// <inheritdoc />
        public Task<long> CountByPatientIdAsync(string patientId, CancellationToken ct = default) =>
            _payments.CountDocumentsAsync(F.Eq(p => p.PatientId, patientId), cancellationToken: ct);
    }
}
