using BMS.Core.Entities;

namespace BMS.Core.Interfaces
{
    /// <summary>
    /// Data access for the <c>TestOrder/TestResult</c> API (Approve Test Results, I3-004) over <c>test_results</c>.
    /// Ids are matched against both the document id and the legacy <c>testResultId</c> field. Soft-deleted
    /// results are never returned.
    /// </summary>
    public interface ITestResultRepository
    {
        /// <summary>Returns one page of results (newest first) and the total count.</summary>
        Task<(IReadOnlyList<TestResult> Items, long Total)> GetPagedAsync(int page, int pageSize, CancellationToken ct = default);

        /// <summary>Returns a result by id, or null.</summary>
        Task<TestResult?> GetByIdAsync(string id, CancellationToken ct = default);

        /// <summary>Inserts a result.</summary>
        Task AddAsync(TestResult entity, CancellationToken ct = default);

        /// <summary>
        /// Atomically sets the given fields (<c>$set</c>) on one result that is NOT signed yet. Returns false when it
        /// does not exist or is signed (signed results are read-only, Quy trình Đợt 3A Q5).
        /// </summary>
        Task<bool> UpdateFieldsAsync(string id, TestResultFieldChanges changes, CancellationToken ct = default);

        /// <summary>Soft-deletes an unsigned result. Returns false when it does not exist or is signed.</summary>
        Task<bool> SoftDeleteAsync(string id, CancellationToken ct = default);

        /// <summary>
        /// True when any active result linked to the order is signed: by sample (<paramref name="sampleKeys"/> = sample ids
        /// and SMP- barcodes) or by <c>appointmentId</c> (<paramref name="orderKeys"/> = order id and ORD- number).
        /// </summary>
        Task<bool> AnySignedForOrderAsync(IReadOnlyCollection<string> sampleKeys, IReadOnlyCollection<string> orderKeys, CancellationToken ct = default);

        /// <summary>Returns all results of one appointment / test order.</summary>
        Task<IReadOnlyList<TestResult>> GetByAppointmentIdAsync(string appointmentId, CancellationToken ct = default);
    }

    /// <summary>Optional field changes for <see cref="ITestResultRepository.UpdateFieldsAsync"/>; null means unchanged.</summary>
    public sealed record TestResultFieldChanges(
        string? ReagentCode = null,
        decimal? Quantity = null,
        string? DeviceId = null,
        string? ReviewedBy = null,
        DateTime? ReviewedAt = null,
        string? Status = null,
        List<TestResultItem>? Items = null,
        ResultValidationStatus? ValidationStatus = null,
        string? ApprovedByDoctorId = null,
        DateTime? ApprovedAt = null);
}
