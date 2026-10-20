using System.Collections.Generic;
using System.Threading.Tasks;
using BMS.Core.Entities;

namespace BMS.Core.Interfaces;

/// <summary>
/// Data access contract for clinical laboratory test orders (UC32).
/// </summary>
public interface ITestOrderRepository
{
    Task<TestOrder?> GetByIdAsync(string id);
    Task<TestOrder?> GetByOrderNumberAsync(string orderNumber);
    Task<IEnumerable<TestOrder>> GetAllAsync(string? patientId = null, string? doctorId = null, TestOrderStatus? status = null);
    Task<TestOrder> CreateAsync(TestOrder order);
    Task<bool> UpdateAsync(TestOrder order);
    Task<bool> DeleteAsync(string id);
    Task<string> GenerateNextOrderNumberAsync();
}
