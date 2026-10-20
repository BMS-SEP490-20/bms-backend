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

    // Standard clinical test catalog
    private static readonly List<TestCatalogItemDto> CatalogItems = new()
    {
        new TestCatalogItemDto
        {
            Code = "SP-LIP-02",
            Name = "Lipid Profile Extended",
            Category = "Biochemistry",
            TubeType = "SST 4mL",
            TubeColor = "Gold",
            VolumeMl = 4.0,
            TurnaroundTime = "~4h",
            PreparationNotes = "Fast 12h",
            FastingRequired = true,
            FastingHours = 12,
            Price = 380000m
        },
        new TestCatalogItemDto
        {
            Code = "TST-GLU-F",
            Name = "Fasting Glucose",
            Category = "Biochemistry",
            TubeType = "NaF 2mL",
            TubeColor = "Green",
            VolumeMl = 2.0,
            TurnaroundTime = "~1h",
            PreparationNotes = "Fast 8h",
            FastingRequired = true,
            FastingHours = 8,
            Price = 80000m
        },
        new TestCatalogItemDto
        {
            Code = "SP-LFT-01",
            Name = "Liver Function Test (AST, ALT, GGT, Bili)",
            Category = "Biochemistry",
            TubeType = "SST 3mL",
            TubeColor = "Gold",
            VolumeMl = 3.0,
            TurnaroundTime = "~3h",
            PreparationNotes = "Fast 8h",
            FastingRequired = true,
            FastingHours = 8,
            Price = 280000m
        },
        new TestCatalogItemDto
        {
            Code = "TST-HBA1C",
            Name = "HbA1c (Glycated Hemoglobin)",
            Category = "Hematology",
            TubeType = "EDTA 2mL",
            TubeColor = "Lavender",
            VolumeMl = 2.0,
            TurnaroundTime = "~2h",
            PreparationNotes = "None",
            FastingRequired = false,
            FastingHours = 0,
            Price = 150000m
        },
        new TestCatalogItemDto
        {
            Code = "TST-CRP-H",
            Name = "hs-CRP (High-Sensitivity Cardiac CRP)",
            Category = "Immunology",
            TubeType = "SST 1mL",
            TubeColor = "Gold",
            VolumeMl = 1.0,
            TurnaroundTime = "~2h",
            PreparationNotes = "None",
            FastingRequired = false,
            FastingHours = 0,
            Price = 120000m
        },
        new TestCatalogItemDto
        {
            Code = "SP-CBC-01",
            Name = "Complete Blood Count (CBC 24-parameters)",
            Category = "Hematology",
            TubeType = "EDTA 2mL",
            TubeColor = "Lavender",
            VolumeMl = 2.0,
            TurnaroundTime = "~1h",
            PreparationNotes = "None",
            FastingRequired = false,
            FastingHours = 0,
            Price = 120000m
        },
        new TestCatalogItemDto
        {
            Code = "SP-KFT-01",
            Name = "Kidney Function Test (Urea, Creatinine, eGFR)",
            Category = "Biochemistry",
            TubeType = "SST 3mL",
            TubeColor = "Gold",
            VolumeMl = 3.0,
            TurnaroundTime = "~2h",
            PreparationNotes = "None",
            FastingRequired = false,
            FastingHours = 0,
            Price = 180000m
        },
        new TestCatalogItemDto
        {
            Code = "TST-THY-01",
            Name = "Thyroid Panel (TSH, FT3, FT4)",
            Category = "Immunology",
            TubeType = "SST 3mL",
            TubeColor = "Gold",
            VolumeMl = 3.0,
            TurnaroundTime = "~3h",
            PreparationNotes = "None",
            FastingRequired = false,
            FastingHours = 0,
            Price = 350000m
        }
    };

    public TestOrderService(ITestOrderRepository orderRepository, IUserRepository userRepository, BmsDbContext dbContext)
    {
        _orderRepository = orderRepository;
        _userRepository = userRepository;
        _dbContext = dbContext;
    }

    public Task<IEnumerable<TestCatalogItemDto>> GetAvailableCatalogAsync()
    {
        return Task.FromResult<IEnumerable<TestCatalogItemDto>>(CatalogItems);
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

        // Look up patient info if available
        var patientName = request.PatientName ?? "Nguyễn Văn An";
        var patientGender = request.PatientGender ?? "Nam";
        var patientAge = request.PatientAge ?? 34;

        if (!string.IsNullOrEmpty(request.PatientId))
        {
            var user = await _userRepository.GetByIdAsync(request.PatientId);
            if (user != null)
            {
                patientName = user.FullName;
                patientGender = user.Gender ?? patientGender;
            }
        }

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
            // Cross-reference with catalog for clinical tube/pricing specifications
            var catalogItem = CatalogItems.FirstOrDefault(c => c.Code.Equals(itemReq.TestCode, StringComparison.OrdinalIgnoreCase));

            var item = new TestOrderItem
            {
                TestCode = itemReq.TestCode,
                TestName = itemReq.TestName ?? catalogItem?.Name ?? itemReq.TestCode,
                Category = itemReq.Category ?? catalogItem?.Category ?? "Biochemistry",
                TubeType = itemReq.TubeType ?? catalogItem?.TubeType ?? "SST 4mL",
                VolumeMl = itemReq.VolumeMl ?? catalogItem?.VolumeMl ?? 4.0,
                TurnaroundTime = itemReq.TurnaroundTime ?? catalogItem?.TurnaroundTime ?? "~2h",
                PreparationNotes = itemReq.PreparationNotes ?? catalogItem?.PreparationNotes ?? "None",
                Price = itemReq.Price ?? catalogItem?.Price ?? 100000m
            };

            totalAmount += item.Price;

            if (catalogItem != null && catalogItem.FastingRequired)
            {
                fastingRequired = true;
                if (catalogItem.FastingHours > maxFastingHours)
                {
                    maxFastingHours = catalogItem.FastingHours;
                }
            }
            else if (item.PreparationNotes.Contains("Fast", StringComparison.OrdinalIgnoreCase))
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
            DoctorId = request.DoctorId ?? currentUserId ?? "USR-DOC-01",
            DoctorName = request.DoctorName ?? "Dr. Trần Thị Bình",
            Priority = priority,
            Status = orderStatus,
            ClinicalIndication = request.ClinicalIndication,
            SpecialInstructions = request.SpecialInstructions,
            ScheduledDate = request.ScheduledDate ?? DateTime.UtcNow.Date,
            TotalAmount = totalAmount,
            FastingRequired = fastingRequired,
            FastingHours = maxFastingHours,
            Items = orderItems,
            CreatedBy = currentUserId ?? "USR-DOC-01",
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
