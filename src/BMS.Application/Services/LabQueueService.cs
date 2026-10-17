using BMS.Application.DTOs;
using BMS.Application.Interfaces;
using BMS.Core.Entities;
using BMS.Infrastructure.Data;

namespace BMS.Application.Services;

/// <summary>
/// Service orchestrating laboratory sample queuing, status transitions, and STAT prioritization (UC17).
/// </summary>
public class LabQueueService : ILabQueueService
{
    private readonly BmsDbContext _context;

    public LabQueueService(BmsDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Retrieves filtered, prioritized specimen list for clinical laboratory technicians.
    /// </summary>
    public async Task<(List<LabQueueItemDto> Items, int TotalCount)> GetQueueAsync(QueueFilterDto filter)
    {
        await Task.Yield();
        var query = _context.Samples.AsQueryable();

        // 1. Status filter
        if (!string.IsNullOrWhiteSpace(filter.Status) && filter.Status != "All")
        {
            if (Enum.TryParse<SampleStatus>(filter.Status, true, out var statusEnum))
            {
                query = query.Where(s => s.Status == statusEnum);
            }
        }

        // 2. Priority filter
        if (!string.IsNullOrWhiteSpace(filter.Priority) && filter.Priority != "All")
        {
            if (Enum.TryParse<SamplePriority>(filter.Priority, true, out var prioEnum))
            {
                query = query.Where(s => s.Priority == prioEnum);
            }
        }

        // 3. Search query (Barcode or Patient Name)
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var s = filter.Search.Trim().ToLowerInvariant();
            query = query.Where(x => 
                x.Barcode.ToLowerInvariant().Contains(s) || 
                x.PatientName.ToLowerInvariant().Contains(s) ||
                x.TestPackageName.ToLowerInvariant().Contains(s));
        }

        // 4. Lab Room filter
        if (!string.IsNullOrWhiteSpace(filter.LabRoom) && filter.LabRoom != "All")
        {
            query = query.Where(x => x.LabRoom != null && x.LabRoom.Contains(filter.LabRoom, StringComparison.OrdinalIgnoreCase));
        }

        var total = query.Count();

        // Sort by priority first (Stat, Urgent, Normal), then CollectedAt
        var paged = query
            .OrderByDescending(s => s.Priority)
            .ThenBy(s => s.CollectedAt)
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .Select(s => new LabQueueItemDto
            {
                Id = s.Id,
                Barcode = s.Barcode,
                TestOrderId = s.TestOrderId,
                PatientId = s.PatientId,
                PatientName = s.PatientName,
                PatientGender = s.PatientGender,
                PatientAge = s.PatientAge,
                TestPackageName = s.TestPackageName,
                SampleType = s.SampleType,
                Status = s.Status.ToString(),
                Priority = s.Priority.ToString(),
                LabRoom = s.LabRoom,
                AssignedTechnicianName = s.AssignedTechnicianName,
                CollectedAt = s.CollectedAt,
                ReceivedAt = s.ReceivedAt,
                Notes = s.Notes
            })
            .ToList();

        return (paged, total);
    }

    public async Task<LabQueueItemDto?> GetSampleByIdAsync(string id)
    {
        await Task.Yield();
        var s = _context.Samples.FirstOrDefault(x => x.Id == id || x.Barcode == id);
        if (s == null) return null;

        return new LabQueueItemDto
        {
            Id = s.Id,
            Barcode = s.Barcode,
            TestOrderId = s.TestOrderId,
            PatientId = s.PatientId,
            PatientName = s.PatientName,
            PatientGender = s.PatientGender,
            PatientAge = s.PatientAge,
            TestPackageName = s.TestPackageName,
            SampleType = s.SampleType,
            Status = s.Status.ToString(),
            Priority = s.Priority.ToString(),
            LabRoom = s.LabRoom,
            AssignedTechnicianName = s.AssignedTechnicianName,
            CollectedAt = s.CollectedAt,
            ReceivedAt = s.ReceivedAt,
            Notes = s.Notes
        };
    }

    public async Task<bool> UpdateStatusAsync(string id, UpdateSampleStatusDto dto)
    {
        await Task.Yield();
        var sample = _context.Samples.FirstOrDefault(x => x.Id == id || x.Barcode == id);
        if (sample == null) return false;

        if (Enum.TryParse<SampleStatus>(dto.Status, true, out var newStatus))
        {
            sample.Status = newStatus;
            if (newStatus == SampleStatus.Processing) sample.ProcessedAt = DateTime.UtcNow;
            if (newStatus == SampleStatus.Completed) sample.CompletedAt = DateTime.UtcNow;
            if (newStatus == SampleStatus.Received) sample.ReceivedAt = DateTime.UtcNow;
        }

        if (!string.IsNullOrWhiteSpace(dto.TechnicianName))
        {
            sample.AssignedTechnicianName = dto.TechnicianName;
            sample.AssignedTechnicianId = dto.TechnicianId;
        }

        if (!string.IsNullOrWhiteSpace(dto.Notes)) sample.Notes = dto.Notes;
        if (!string.IsNullOrWhiteSpace(dto.RejectionReason)) sample.RejectionReason = dto.RejectionReason;

        return true;
    }

    public async Task<bool> RequestRerunAsync(string id, SampleRerunRequestDto dto)
    {
        await Task.Yield();
        var sample = _context.Samples.FirstOrDefault(x => x.Id == id || x.Barcode == id);
        if (sample == null) return false;

        sample.Status = SampleStatus.RerunRequested;
        sample.Priority = SamplePriority.Stat; // Escalated
        sample.RerunReason = dto.Reason;
        if (!string.IsNullOrWhiteSpace(dto.Notes))
        {
            sample.Notes = $"{sample.Notes} | Yêu cầu chạy lại bởi {dto.RequestedBy}: {dto.Reason}";
        }

        return true;
    }
}
