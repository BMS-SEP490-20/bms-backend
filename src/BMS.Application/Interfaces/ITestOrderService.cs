using System.Collections.Generic;
using System.Threading.Tasks;
using BMS.Application.DTOs.Laboratory;

namespace BMS.Application.Interfaces;

/// <summary>
/// Service interface managing laboratory test order creation, validation, and retrieval (UC32).
/// </summary>
public interface ITestOrderService
{
    Task<IEnumerable<TestCatalogItemDto>> GetAvailableCatalogAsync();
    Task<IEnumerable<TestOrderResponseDto>> GetOrdersAsync(string? patientId = null, string? doctorId = null, string? status = null);
    Task<TestOrderResponseDto?> GetOrderByIdAsync(string id);
    Task<TestOrderResponseDto> CreateTestOrderAsync(CreateTestOrderRequestDto request, string? currentUserId = null);
    Task<TestOrderResponseDto?> UpdateOrderStatusAsync(string id, UpdateTestOrderStatusRequestDto request);
}
