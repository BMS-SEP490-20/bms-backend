using System.Collections.Generic;
using System.Threading.Tasks;
using BMS.Application.DTOs.Laboratory;

namespace BMS.Application.Interfaces;

/// <summary>
/// Service contract for laboratory technical test result verification (UC72 / Issue I3-073).
/// Allows Lab Managers / Supervisors to inspect verification checklists, verify results, or return for correction.
/// </summary>
public interface ITestResultVerificationService
{
    Task<IEnumerable<VerificationQueueItemDto>> GetVerificationQueueAsync();
    Task<TestResultVerificationDetailDto?> GetVerificationDetailsBySpecimenIdAsync(string specimenId);
    Task<TestResultVerificationDetailDto> VerifyTestResultAsync(string specimenId, VerifyTestResultRequestDto request, string verifierName);
    Task<TestResultVerificationDetailDto> ReturnTestResultAsync(string specimenId, ReturnTestResultRequestDto request, string verifierName);
}
