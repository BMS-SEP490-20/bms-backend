using BMS.Application.DTOs.TestResults;

namespace BMS.Application.Interfaces
{
    /// <summary>Test result CRUD and approval behind <c>TestOrder/TestResult</c> (I3-004).</summary>
    public interface ITestResultService
    {
        /// <summary>Returns one page of results and the total count.</summary>
        Task<(IReadOnlyList<TestResultDto> Items, long Total)> GetPagedAsync(int page, int pageSize, CancellationToken ct = default);
        /// <summary>Returns a result by id, or null.</summary>
        Task<TestResultDto?> GetByIdAsync(string id, CancellationToken ct = default);
        /// <summary>Creates a result with its parameters.</summary>
        Task<TestResultDto> CreateAsync(CreateFullTestResultDto dto, CancellationToken ct = default);
        /// <summary>Updates a result; throws ResourceNotFoundException when missing.</summary>
        Task UpdateAsync(string id, UpdateTestResultDto dto, CancellationToken ct = default);
        /// <summary>Soft-deletes a result; throws ResourceNotFoundException when missing.</summary>
        Task DeleteAsync(string id, CancellationToken ct = default);
        /// <summary>Returns the results of one appointment.</summary>
        Task<IReadOnlyList<TestResultDto>> GetByAppointmentIdAsync(string appointmentId, CancellationToken ct = default);

        /// <summary>
        /// Reviews, verifies, and digitally approves a blood test result.
        /// </summary>
        /// <param name="id">Test result identifier.</param>
        /// <param name="dto">Approval metadata including approver identity, role, and digital signature.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>The approved test result data transfer object.</returns>
        Task<TestResultDto> ApproveAsync(string id, ApproveTestResultDto dto, CancellationToken ct = default);
    }
}
