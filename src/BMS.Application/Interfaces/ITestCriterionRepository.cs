using BMS.Core.Entities;

namespace BMS.Application.Interfaces;

public interface ITestCriterionRepository
{
    Task<List<TestCriterion>> GetAllAsync(CancellationToken ct = default);
}