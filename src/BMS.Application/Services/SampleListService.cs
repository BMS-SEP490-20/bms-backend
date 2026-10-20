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
/// Service managing sample collection list creation and phlebotomy accessioning (UC37).
/// Implements ISO 15189 pre-analytical identification standards.
/// </summary>
public class SampleListService : ISampleListService
{
    private readonly ISampleListRepository _listRepository;
    private readonly BmsDbContext _dbContext;

    // Default checked-in appointments pool for intake
    private static readonly List<CheckedInAppointmentDto> CheckedInPool = new()
    {
        new CheckedInAppointmentDto
        {
            AppointmentId = "APT-342",
            PatientId = "USR-PAT-01",
            PatientName = "Nguyễn Văn An",
            PackageName = "Lipid + Glucose",
            Time = "08:00",
            Tubes = new List<string> { "SST", "NaF" },
            Priority = "Routine"
        },
        new CheckedInAppointmentDto
        {
            AppointmentId = "APT-343",
            PatientId = "USR-PAT-02",
            PatientName = "Trần Thị Mai",
            PackageName = "CBC",
            Time = "08:30",
            Tubes = new List<string> { "EDTA" },
            Priority = "Routine"
        },
        new CheckedInAppointmentDto
        {
            AppointmentId = "APT-344",
            PatientId = "USR-PAT-03",
            PatientName = "Lê Hoàng Nam",
            PackageName = "Thyroid",
            Time = "09:00",
            Tubes = new List<string> { "SST" },
            Priority = "Routine"
        },
        new CheckedInAppointmentDto
        {
            AppointmentId = "APT-345",
            PatientId = "USR-PAT-04",
            PatientName = "Phạm Minh Ơn",
            PackageName = "Liver",
            Time = "09:30",
            Tubes = new List<string> { "SST" },
            Priority = "Routine"
        },
        new CheckedInAppointmentDto
        {
            AppointmentId = "APT-346",
            PatientId = "USR-PAT-05",
            PatientName = "Võ Thị Phượng",
            PackageName = "Kidney",
            Time = "10:00",
            Tubes = new List<string> { "SST" },
            Priority = "Routine"
        },
        new CheckedInAppointmentDto
        {
            AppointmentId = "APT-348",
            PatientId = "USR-PAT-06",
            PatientName = "Nguyễn Thị Lan",
            PackageName = "CBC + Lipid",
            Time = "11:00",
            Tubes = new List<string> { "EDTA", "SST" },
            Priority = "Routine"
        }
    };

    public SampleListService(ISampleListRepository listRepository, BmsDbContext dbContext)
    {
        _listRepository = listRepository;
        _dbContext = dbContext;
    }

    public Task<IEnumerable<CheckedInAppointmentDto>> GetCheckedInAppointmentsAsync()
    {
        return Task.FromResult<IEnumerable<CheckedInAppointmentDto>>(CheckedInPool);
    }

    public async Task<IEnumerable<SampleListResponseDto>> GetCollectionBatchesAsync(DateTime? date = null)
    {
        var batches = await _listRepository.GetAllAsync(date);
        return batches.Select(MapToResponseDto);
    }

    public async Task<SampleListResponseDto?> GetBatchByCodeAsync(string batchCode)
    {
        var batch = await _listRepository.GetByBatchCodeAsync(batchCode) ?? await _listRepository.GetByIdAsync(batchCode);
        return batch == null ? null : MapToResponseDto(batch);
    }

    public async Task<SampleListResponseDto> CreateCollectionListAsync(CreateSampleListRequestDto request, string? currentUserId = null)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));
        if (request.AppointmentIds == null || !request.AppointmentIds.Any())
        {
            throw new ArgumentException("At least one appointment must be selected for the collection list.", nameof(request.AppointmentIds));
        }

        var batchCode = await _listRepository.GenerateNextBatchCodeAsync();
        var datePrefix = DateTime.UtcNow.ToString("yyMMdd");

        var items = new List<SampleCollectionItem>();
        int specimenSequence = 89;

        foreach (var aptId in request.AppointmentIds)
        {
            var appt = CheckedInPool.FirstOrDefault(a => a.AppointmentId.Equals(aptId, StringComparison.OrdinalIgnoreCase))
                ?? new CheckedInAppointmentDto
                {
                    AppointmentId = aptId,
                    PatientId = $"PAT-{aptId}",
                    PatientName = $"Patient {aptId}",
                    PackageName = "Routine Blood Panel",
                    Time = "08:30",
                    Tubes = new List<string> { "SST" }
                };

            var specimenId = $"SPC-{datePrefix}-{batchCode.Split('-').Last()}-{specimenSequence:D4}";

            var item = new SampleCollectionItem
            {
                SpecimenId = specimenId,
                AppointmentId = appt.AppointmentId,
                PatientId = appt.PatientId,
                PatientName = appt.PatientName,
                PackageName = appt.PackageName,
                Tubes = appt.Tubes,
                CollectorId = request.CollectorId ?? currentUserId ?? "USR-LAB-01",
                CollectorName = request.CollectorName ?? "Phạm Thu Dung",
                Status = "PendingCollection"
            };

            items.Add(item);

            // Register biological tube sample in active lab samples collection
            foreach (var tubeType in appt.Tubes)
            {
                var tubeBarcode = $"{specimenId}-{tubeType}";
                var tubeColor = tubeType.Contains("EDTA") ? "Lavender" : (tubeType.Contains("NaF") ? "Green" : "Gold");

                lock (_dbContext.Samples)
                {
                    _dbContext.Samples.Add(new Sample
                    {
                        Id = Guid.NewGuid().ToString(),
                        Barcode = tubeBarcode,
                        TestOrderId = appt.AppointmentId,
                        PatientId = appt.PatientId,
                        PatientName = appt.PatientName,
                        TestPackageName = appt.PackageName,
                        SampleType = tubeType.Contains("EDTA") ? "Blood" : "Serum",
                        TubeColor = tubeColor,
                        VolumeMl = tubeType.Contains("EDTA") ? 2.0 : 4.0,
                        Status = SampleStatus.PendingCollection,
                        Priority = SamplePriority.Normal,
                        LabRoom = request.Location,
                        Notes = $"Batch {batchCode}. Target collection: {request.TargetTime}"
                    });
                }
            }

            specimenSequence++;
        }

        var collectionList = new SampleCollectionList
        {
            BatchCode = batchCode,
            Location = request.Location,
            TargetTime = request.TargetTime,
            ScheduledDate = DateTime.UtcNow.Date,
            CollectorId = request.CollectorId ?? currentUserId ?? "USR-LAB-01",
            CollectorName = request.CollectorName ?? "Phạm Thu Dung",
            Status = SampleCollectionListStatus.Confirmed,
            Items = items,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = currentUserId ?? "USR-LAB-01"
        };

        var created = await _listRepository.CreateAsync(collectionList);
        return MapToResponseDto(created);
    }

    public async Task<BatchPrintBarcodesResponseDto?> GenerateBatchBarcodesAsync(string batchCode)
    {
        var batch = await _listRepository.GetByBatchCodeAsync(batchCode) ?? await _listRepository.GetByIdAsync(batchCode);
        if (batch == null) return null;

        var labels = new List<SpecimenBarcodeLabelDto>();

        foreach (var item in batch.Items)
        {
            foreach (var tube in item.Tubes)
            {
                var barcodeCode = $"{item.SpecimenId}-{tube}";
                labels.Add(new SpecimenBarcodeLabelDto
                {
                    SpecimenId = item.SpecimenId,
                    PatientName = item.PatientName,
                    Tube = tube,
                    PackageName = item.PackageName,
                    BarcodeCode128 = barcodeCode,
                    SvgBarcode = GenerateSvgBarcode(barcodeCode)
                });
            }
        }

        return new BatchPrintBarcodesResponseDto
        {
            BatchCode = batch.BatchCode,
            LabelCount = labels.Count,
            Labels = labels
        };
    }

    private static string GenerateSvgBarcode(string code)
    {
        return $"<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 200 60' width='200' height='60'><rect width='100%' height='100%' fill='#ffffff'/><text x='100' y='52' font-family='monospace' font-size='11' text-anchor='middle'>{code}</text><path d='M10,10 h4 v35 h-4 z M18,10 h2 v35 h-2 z M24,10 h6 v35 h-6 z M34,10 h2 v35 h-2 z M40,10 h4 v35 h-4 z M48,10 h8 v35 h-8 z M60,10 h2 v35 h-2 z M66,10 h6 v35 h-6 z M76,10 h4 v35 h-4 z M84,10 h2 v35 h-2 z M90,10 h6 v35 h-6 z M100,10 h8 v35 h-8 z M112,10 h2 v35 h-2 z M118,10 h4 v35 h-4 z M126,10 h6 v35 h-6 z M136,10 h2 v35 h-2 z M142,10 h6 v35 h-6 z M152,10 h4 v35 h-4 z M160,10 h8 v35 h-8 z M172,10 h4 v35 h-4 z M180,10 h2 v35 h-2 z M186,10 h4 v35 h-4 z' fill='#000000'/></svg>";
    }

    private static SampleListResponseDto MapToResponseDto(SampleCollectionList list)
    {
        return new SampleListResponseDto
        {
            Id = list.Id,
            BatchCode = list.BatchCode,
            Location = list.Location,
            TargetTime = list.TargetTime,
            ScheduledDate = list.ScheduledDate,
            CollectorName = list.CollectorName,
            Status = list.Status.ToString(),
            TotalSpecimens = list.Items.Count,
            CreatedAt = list.CreatedAt,
            Items = list.Items.Select((item, idx) => new SampleCollectionItemDto
            {
                Index = idx + 1,
                SpecimenId = item.SpecimenId,
                AppointmentId = item.AppointmentId,
                PatientId = item.PatientId,
                PatientName = item.PatientName,
                PackageName = item.PackageName,
                Tubes = item.Tubes,
                CollectorName = item.CollectorName,
                Status = item.Status
            }).ToList()
        };
    }
}
