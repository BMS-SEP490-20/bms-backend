using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BMS.Application.DTOs.Laboratory;
using BMS.Application.Interfaces;
using BMS.Core.Entities;
using BMS.Core.Interfaces;
using BMS.Infrastructure.Data;
using MongoDB.Driver;

namespace BMS.Application.Services;

/// <summary>
/// Service managing sample collection list creation and phlebotomy accessioning (UC37).
/// Implements ISO 15189 pre-analytical identification standards.
/// </summary>
public class SampleListService : ISampleListService
{
    private readonly ISampleListRepository _listRepository;
    private readonly BmsDbContext _dbContext;
    private readonly IMongoCollection<Appointment>? _appointmentsCol;
    private readonly IMongoCollection<Patient>? _patientsCol;
    private readonly IMongoCollection<ServicePackage>? _packagesCol;

    public SampleListService(ISampleListRepository listRepository, BmsDbContext dbContext, IMongoDatabase? database = null)
    {
        _listRepository = listRepository;
        _dbContext = dbContext;
        if (database != null)
        {
            try
            {
                _appointmentsCol = database.GetCollection<Appointment>("appointments");
                _patientsCol = database.GetCollection<Patient>("patients");
                _packagesCol = database.GetCollection<ServicePackage>("service_packages");
            }
            catch
            {
                _appointmentsCol = null;
                _patientsCol = null;
                _packagesCol = null;
            }
        }
    }

    public async Task<IEnumerable<CheckedInAppointmentDto>> GetCheckedInAppointmentsAsync()
    {
        var result = new List<CheckedInAppointmentDto>();

        if (_appointmentsCol != null)
        {
            try
            {
                var fb = Builders<Appointment>.Filter;
                var filter = fb.Or(
                    fb.Eq(a => a.Status, AppointmentStatus.Confirmed),
                    fb.Eq(a => a.Status, AppointmentStatus.CheckedIn),
                    fb.Eq(a => a.Status, AppointmentStatus.Pending)
                );

                var list = await _appointmentsCol.Find(filter).Limit(30).ToListAsync();
                foreach (var a in list)
                {
                    if (string.IsNullOrEmpty(a.PatientId)) continue;

                    string? patientName = null;
                    if (_patientsCol != null)
                    {
                        var pat = await _patientsCol.Find(p => p.Id == a.PatientId).FirstOrDefaultAsync();
                        if (pat != null && !string.IsNullOrEmpty(pat.FullName)) patientName = pat.FullName;
                    }

                    if (string.IsNullOrEmpty(patientName))
                    {
                        var user = _dbContext.Users.FirstOrDefault(u => u.Id == a.PatientId);
                        if (user != null) patientName = user.FullName;
                    }

                    if (string.IsNullOrEmpty(patientName)) continue;

                    string packageName = !string.IsNullOrEmpty(a.Notes) ? a.Notes : string.Empty;
                    var tubes = new List<string>();

                    if (_packagesCol != null && !string.IsNullOrEmpty(a.PackageId))
                    {
                        var pkg = await _packagesCol.Find(p => p.Id == a.PackageId || p.Code == a.PackageId).FirstOrDefaultAsync();
                        if (pkg != null)
                        {
                            if (!string.IsNullOrEmpty(pkg.Name)) packageName = pkg.Name;
                            if (pkg.Category?.Contains("Hematology", StringComparison.OrdinalIgnoreCase) == true)
                                tubes.Add("EDTA");
                            else
                                tubes.Add("SST");
                        }
                    }

                    if (tubes.Count == 0)
                    {
                        tubes.Add("SST");
                    }

                    result.Add(new CheckedInAppointmentDto
                    {
                        AppointmentId = !string.IsNullOrEmpty(a.AppointmentCode) ? a.AppointmentCode : a.Id,
                        PatientId = a.PatientId,
                        PatientName = patientName,
                        PackageName = packageName,
                        Time = a.StartTime.ToString(@"hh\:mm"),
                        Tubes = tubes,
                        Priority = "Routine"
                    });
                }
            }
            catch
            {
                // Fall back to empty result on DB exception
            }
        }

        return result;
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

        var collectorId = currentUserId ?? request.CollectorId;
        if (string.IsNullOrWhiteSpace(collectorId))
        {
            throw new UnauthorizedAccessException("Authenticated collector identity is required.");
        }

        string collectorDisplayName = !string.IsNullOrWhiteSpace(request.CollectorName)
            ? request.CollectorName
            : string.Empty;

        if (string.IsNullOrWhiteSpace(collectorDisplayName))
        {
            var user = _dbContext.Users.FirstOrDefault(u => u.Id == collectorId);
            collectorDisplayName = user?.FullName ?? collectorId;
        }

        foreach (var aptId in request.AppointmentIds)
        {
            Appointment? apptDoc = null;
            if (_appointmentsCol != null)
            {
                apptDoc = await _appointmentsCol.Find(a => a.Id == aptId || a.AppointmentCode == aptId).FirstOrDefaultAsync();
            }

            if (apptDoc == null || string.IsNullOrWhiteSpace(apptDoc.PatientId))
            {
                continue;
            }

            string patientId = apptDoc.PatientId;
            string? patientName = null;
            if (_patientsCol != null)
            {
                var pat = await _patientsCol.Find(p => p.Id == patientId).FirstOrDefaultAsync();
                if (pat != null && !string.IsNullOrEmpty(pat.FullName))
                {
                    patientName = pat.FullName;
                }
            }

            if (string.IsNullOrEmpty(patientName))
            {
                var user = _dbContext.Users.FirstOrDefault(u => u.Id == patientId);
                if (user != null) patientName = user.FullName;
            }

            if (string.IsNullOrEmpty(patientName))
            {
                continue;
            }

            string itemPackageName = !string.IsNullOrEmpty(apptDoc.Notes) ? apptDoc.Notes : string.Empty;
            var itemTubes = new List<string>();

            if (_packagesCol != null && !string.IsNullOrEmpty(apptDoc.PackageId))
            {
                var pkg = await _packagesCol.Find(p => p.Id == apptDoc.PackageId || p.Code == apptDoc.PackageId).FirstOrDefaultAsync();
                if (pkg != null)
                {
                    if (!string.IsNullOrEmpty(pkg.Name)) itemPackageName = pkg.Name;
                    if (pkg.Category?.Contains("Hematology", StringComparison.OrdinalIgnoreCase) == true)
                        itemTubes.Add("EDTA");
                    else
                        itemTubes.Add("SST");
                }
            }

            if (itemTubes.Count == 0)
            {
                itemTubes.Add("SST");
            }

            var specimenId = $"SPC-{datePrefix}-{batchCode.Split('-').Last()}-{specimenSequence:D4}";

            var item = new SampleCollectionItem
            {
                SpecimenId = specimenId,
                AppointmentId = apptDoc.AppointmentCode ?? aptId,
                PatientId = patientId,
                PatientName = patientName,
                PackageName = itemPackageName,
                Tubes = itemTubes,
                CollectorId = collectorId,
                CollectorName = collectorDisplayName,
                Status = "PendingCollection"
            };

            items.Add(item);

            // Register biological tube sample in active lab samples collection
            foreach (var tubeType in itemTubes)
            {
                var tubeBarcode = $"{specimenId}-{tubeType}";
                var tubeColor = tubeType.Contains("EDTA") ? "Lavender" : (tubeType.Contains("NaF") ? "Green" : "Gold");

                lock (_dbContext.Samples)
                {
                    _dbContext.Samples.Add(new Sample
                    {
                        Id = Guid.NewGuid().ToString(),
                        Barcode = tubeBarcode,
                        TestOrderId = apptDoc.AppointmentCode ?? aptId,
                        PatientId = patientId,
                        PatientName = patientName,
                        TestPackageName = itemPackageName,
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
            CollectorId = collectorId,
            CollectorName = collectorDisplayName,
            Status = SampleCollectionListStatus.Confirmed,
            Items = items,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = currentUserId ?? string.Empty
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
        return code;
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
