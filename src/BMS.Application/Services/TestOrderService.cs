using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BMS.Application.DTOs.Laboratory;
using BMS.Application.Interfaces;
using BMS.Core.Entities;
using BMS.Core.Interfaces;
using BMS.Infrastructure.Data;

namespace BMS.Application.Services;

/// <summary>
/// Service managing laboratory test order creation, validation, and retrieval (UC32).
/// Follows Clean Architecture and ISO 15189 pre-analytical standards.
/// </summary>
public class TestOrderService : ITestOrderService
{
    private readonly ITestOrderRepository _orderRepository;
    private readonly IUserRepository _userRepository;
    private readonly BmsDbContext _dbContext;

    private readonly IServicePackageRepository? _packageRepository;

    public TestOrderService(
        ITestOrderRepository orderRepository,
        IUserRepository userRepository,
        BmsDbContext dbContext,
        IServicePackageRepository? packageRepository = null)
    {
        _orderRepository = orderRepository;
        _userRepository = userRepository;
        _dbContext = dbContext;
        _packageRepository = packageRepository;
    }

    public async Task<IEnumerable<TestCatalogItemDto>> GetAvailableCatalogAsync()
    {
        if (_packageRepository != null)
        {
            try
            {
                var query = new ServicePackageQuery(
                    Keyword: null,
                    IsActive: true,
                    MinPrice: null,
                    MaxPrice: null,
                    SortBy: "Code",
                    SortDesc: false,
                    Page: 1,
                    PageSize: 100);

                var paged = await _packageRepository.SearchAsync(query);
                if (paged.Items.Count > 0)
                {
                    return paged.Items.Select(pkg => new TestCatalogItemDto
                    {
                        Code = pkg.Code,
                        Name = pkg.Name,
                        Category = !string.IsNullOrEmpty(pkg.Category) ? pkg.Category : string.Empty,
                        TubeType = string.Empty,
                        TubeColor = string.Empty,
                        VolumeMl = 0.0,
                        TurnaroundTime = string.Empty,
                        PreparationNotes = string.Empty,
                        FastingRequired = false,
                        FastingHours = 0,
                        Price = pkg.Price
                    }).ToList();
                }
            }
            catch
            {
                // Return empty list on failure
            }
        }

        return Enumerable.Empty<TestCatalogItemDto>();
    }

    public async Task<IEnumerable<TestOrderResponseDto>> GetOrdersAsync(string? patientId = null, string? doctorId = null, string? status = null)
    {
        TestOrderStatus? orderStatus = null;
        if (!string.IsNullOrEmpty(status) && Enum.TryParse<TestOrderStatus>(status, true, out var parsedStatus))
        {
            orderStatus = parsedStatus;
        }

        var orders = await _orderRepository.GetAllAsync(patientId, doctorId, orderStatus);
        return orders.Select(MapToResponseDto);
    }

    public async Task<TestOrderResponseDto?> GetOrderByIdAsync(string id)
    {
        var order = await _orderRepository.GetByIdAsync(id) ?? await _orderRepository.GetByOrderNumberAsync(id);
        return order == null ? null : MapToResponseDto(order);
    }

    public async Task<TestOrderResponseDto> CreateTestOrderAsync(CreateTestOrderRequestDto request, string? currentUserId = null)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));
        if (request.Items == null || !request.Items.Any())
        {
            throw new ArgumentException("At least one test or test package must be included in the order.", nameof(request.Items));
        }

        // Look up patient info from database (mandatory)
        if (string.IsNullOrWhiteSpace(request.PatientId))
        {
            throw new KeyNotFoundException("Patient ID is required.");
        }

        var patientUser = await _userRepository.GetByIdAsync(request.PatientId);
        if (patientUser == null)
        {
            throw new KeyNotFoundException($"Patient with ID '{request.PatientId}' not found.");
        }

        var patientName = !string.IsNullOrWhiteSpace(patientUser.FullName) ? patientUser.FullName : (request.PatientName ?? string.Empty);
        var patientGender = !string.IsNullOrWhiteSpace(patientUser.Gender) ? patientUser.Gender : (request.PatientGender ?? string.Empty);
        var patientAge = request.PatientAge ?? 0;

        // Resolve doctor info from authenticated context
        var docId = currentUserId ?? request.DoctorId;
        if (string.IsNullOrWhiteSpace(docId))
        {
            throw new UnauthorizedAccessException("Authenticated doctor identity is required.");
        }

        var docUser = await _userRepository.GetByIdAsync(docId);
        if (docUser == null)
        {
            throw new UnauthorizedAccessException("Doctor account could not be found.");
        }

        string doctorName = docUser.FullName;

        // Parse Priority
        var priority = SamplePriority.Normal;
        if (!string.IsNullOrEmpty(request.Priority))
        {
            if (request.Priority.Equals("Urgent", StringComparison.OrdinalIgnoreCase))
                priority = SamplePriority.Urgent;
            else if (request.Priority.Equals("STAT", StringComparison.OrdinalIgnoreCase))
                priority = SamplePriority.Stat;
        }

        // Build item details and calculate fasting requirements
        var orderItems = new List<TestOrderItem>();
        decimal totalAmount = 0;
        bool fastingRequired = false;
        int maxFastingHours = 0;

        foreach (var itemReq in request.Items)
        {
            ServicePackage? pkg = null;
            if (_packageRepository != null && !string.IsNullOrWhiteSpace(itemReq.TestCode))
            {
                try
                {
                    var search = await _packageRepository.SearchAsync(new ServicePackageQuery(
                        Keyword: itemReq.TestCode,
                        IsActive: true,
                        MinPrice: null,
                        MaxPrice: null,
                        SortBy: "Code",
                        SortDesc: false,
                        Page: 1,
                        PageSize: 5));
                    pkg = search.Items.FirstOrDefault(p => p.Code.Equals(itemReq.TestCode, StringComparison.OrdinalIgnoreCase) || p.Id.Equals(itemReq.TestCode, StringComparison.OrdinalIgnoreCase));
                }
                catch { }
            }

            var item = new TestOrderItem
            {
                TestCode = itemReq.TestCode,
                TestName = itemReq.TestName ?? pkg?.Name ?? itemReq.TestCode,
                Category = itemReq.Category ?? pkg?.Category ?? string.Empty,
                TubeType = itemReq.TubeType ?? string.Empty,
                VolumeMl = itemReq.VolumeMl ?? 0.0,
                TurnaroundTime = itemReq.TurnaroundTime ?? string.Empty,
                PreparationNotes = itemReq.PreparationNotes ?? string.Empty,
                Price = itemReq.Price ?? pkg?.Price ?? 0m
            };

            totalAmount += item.Price;

            if (item.PreparationNotes.Contains("Fast", StringComparison.OrdinalIgnoreCase))
            {
                fastingRequired = true;
                if (item.PreparationNotes.Contains("12h")) maxFastingHours = Math.Max(maxFastingHours, 12);
                else if (item.PreparationNotes.Contains("8h")) maxFastingHours = Math.Max(maxFastingHours, 8);
            }

            orderItems.Add(item);
        }

        var orderStatus = request.SaveAsDraft ? TestOrderStatus.Draft : TestOrderStatus.Ordered;

        var order = new TestOrder
        {
            PatientId = request.PatientId,
            PatientName = patientName,
            PatientGender = patientGender,
            PatientAge = patientAge,
            DoctorId = request.DoctorId ?? currentUserId ?? string.Empty,
            DoctorName = doctorName,
            Priority = priority,
            Status = orderStatus,
            ClinicalIndication = request.ClinicalIndication,
            SpecialInstructions = request.SpecialInstructions,
            ScheduledDate = request.ScheduledDate ?? DateTime.UtcNow.Date,
            TotalAmount = totalAmount,
            FastingRequired = fastingRequired,
            FastingHours = maxFastingHours,
            Items = orderItems,
            CreatedBy = currentUserId ?? string.Empty,
            CreatedAt = DateTime.UtcNow
        };

        var created = await _orderRepository.CreateAsync(order);

        // Pre-analytical tube batching: Automatically generate specimen collection slots for Phlebotomy (UC37)
        if (!request.SaveAsDraft)
        {
            var distinctTubes = orderItems
                .GroupBy(i => i.TubeType)
                .Select(g => new
                {
                    TubeType = g.Key,
                    PackageName = string.Join(", ", g.Select(x => x.TestName).Distinct()),
                    SampleType = g.Key.Contains("EDTA") ? "Blood" : "Serum",
                    VolumeMl = g.Sum(x => x.VolumeMl)
                });

            int tubeIndex = 1;
            foreach (var tube in distinctTubes)
            {
                var barcode = $"BC-{DateTime.UtcNow:yyyyMMdd}-{created.OrderNumber.Replace("ORD-", "")}-{tubeIndex:D2}";
                var sample = new Sample
                {
                    Id = Guid.NewGuid().ToString(),
                    Barcode = barcode,
                    TestOrderId = created.OrderNumber,
                    PatientId = created.PatientId,
                    PatientName = created.PatientName,
                    PatientGender = created.PatientGender,
                    PatientAge = created.PatientAge,
                    TestPackageName = tube.PackageName,
                    SampleType = tube.SampleType,
                    TubeColor = tube.TubeType.Contains("EDTA") ? "Lavender" : (tube.TubeType.Contains("NaF") ? "Green" : "Gold"),
                    VolumeMl = Math.Min(tube.VolumeMl, 6.0),
                    Status = SampleStatus.PendingCollection,
                    Priority = created.Priority,
                    LabRoom = "Phòng Lấy Máu & Bệnh Phẩm 101",
                    Notes = $"Order: {created.OrderNumber}. {created.ClinicalIndication}"
                };

                lock (_dbContext.Samples)
                {
                    _dbContext.Samples.Add(sample);
                }
                tubeIndex++;
            }
        }

        return MapToResponseDto(created);
    }

    public async Task<TestOrderResponseDto?> UpdateOrderStatusAsync(string id, UpdateTestOrderStatusRequestDto request)
    {
        var order = await _orderRepository.GetByIdAsync(id) ?? await _orderRepository.GetByOrderNumberAsync(id);
        if (order == null) return null;

        if (Enum.TryParse<TestOrderStatus>(request.Status, true, out var newStatus))
        {
            order.Status = newStatus;
            order.UpdatedAt = DateTime.UtcNow;
            if (!string.IsNullOrEmpty(request.Notes))
            {
                order.SpecialInstructions = (order.SpecialInstructions + " | Status note: " + request.Notes).Trim();
            }
            await _orderRepository.UpdateAsync(order);
        }

        return MapToResponseDto(order);
    }

    private static TestOrderResponseDto MapToResponseDto(TestOrder order)
    {
        return new TestOrderResponseDto
        {
            Id = order.Id,
            OrderNumber = order.OrderNumber,
            PatientId = order.PatientId,
            PatientName = order.PatientName,
            PatientGender = order.PatientGender,
            PatientAge = order.PatientAge,
            DoctorId = order.DoctorId,
            DoctorName = order.DoctorName,
            Priority = order.Priority.ToString(),
            Status = order.Status.ToString(),
            ClinicalIndication = order.ClinicalIndication,
            SpecialInstructions = order.SpecialInstructions,
            ScheduledDate = order.ScheduledDate,
            TotalAmount = order.TotalAmount,
            FastingRequired = order.FastingRequired,
            FastingHours = order.FastingHours,
            CreatedAt = order.CreatedAt,
            CreatedBy = order.CreatedBy,
            Items = order.Items.Select(i => new TestOrderItemDto
            {
                TestCode = i.TestCode,
                TestName = i.TestName,
                Category = i.Category,
                TubeType = i.TubeType,
                VolumeMl = i.VolumeMl,
                TurnaroundTime = i.TurnaroundTime,
                PreparationNotes = i.PreparationNotes,
                Price = i.Price
            }).ToList()
        };
    }
}
