using System.Collections.Generic;
using System.Threading.Tasks;
using BMS.Core.Entities;

namespace BMS.Core.Interfaces;

/// <summary>
/// Repository interface for technical test result verifications (UC72).
/// </summary>
public interface ITestResultVerificationRepository
{
    Task<TestResultVerification?> GetBySpecimenIdAsync(string specimenId);
    Task<IEnumerable<TestResultVerification>> GetPendingQueueAsync();
    Task<bool> UpsertVerificationAsync(TestResultVerification verification);
}
