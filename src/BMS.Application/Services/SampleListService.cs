using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BMS.Application.DTOs.Laboratory;
using BMS.Application.Interfaces;
using BMS.Core.Constants;
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
    private readonly IMongoCollection<Sample>? _samplesCol;
    private readonly IMongoCollection<TestOrder>? _ordersCol;

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
                _samplesCol = database.GetCollection<Sample>("blood_samples");
                _ordersCol = database.GetCollection<TestOrder>("test_orders");
            }
            catch
            {
                _appointmentsCol = null;
                _patientsCol = null;
                _packagesCol = null;
                _samplesCol = null;
                _ordersCol = null;
            }
        }
    }

    /// <summary>Map CLSI tube color → short print code on barcode (EDTA / SST / CITRATE…).</summary>
    private static string TubePrintCode(string? colorOrType)
    {
        var t = (colorOrType ?? string.Empty).ToUpperInvariant();
        if (t.Contains("PURPLE") || t.Contains("EDTA") || t.Contains("TÍM") || t.Contains("TIM")) return "EDTA";
        if (t.Contains("LIGHTBLUE") || t.Contains("CITRATE") || t.Contains("BLUE") || t.Contains("XANH LAM")) return "CITRATE";
        if (t.Contains("GRAY") || t.Contains("GREY") || t.Contains("FLUOR") || t.Contains("NAF") || t.Contains("XÁM")) return "NaF";
        if (t.Contains("GREEN") || t.Contains("HEPARIN")) return "HEPARIN";
        if (t.Contains("CULTURE")) return "CULTURE";
        return "SST";
    }

    private static string TubeColorFromPrint(string printCode) => printCode.ToUpperInvariant() switch
    {
        "EDTA" => "Purple",
        "CITRATE" => "LightBlue",
        "NAF" => "Gray",
        "HEPARIN" => "Green",
        "CULTURE" => "BloodCulture",
        _ => "Gold"
    };

    private async Task<List<string>> ResolveTubePrintCodesAsync(Appointment appt, string packageName)
    {
        var tubes = new List<string>();

        // 1) Y lệnh đã phát hành → mỗi nhóm ống theo chỉ định (BN này, lịch này)
        if (_ordersCol != null)
        {
            try
            {
                // Y lệnh mới nhất của BN (TestOrder chưa map AppointmentId trên entity)
                var filter = Builders<TestOrder>.Filter.Eq(o => o.PatientId, appt.PatientId);
                var orders = await _ordersCol.Find(filter).SortByDescending(o => o.CreatedAt).Limit(8).ToListAsync();
                var match = orders.FirstOrDefault(o =>
                    o.Status != TestOrderStatus.Draft && o.Status != TestOrderStatus.Cancelled)
                    ?? orders.FirstOrDefault();

                if (match?.Items != null)
                {
                    foreach (var item in match.Items)
                    {
                        var spec = TubeRules.For(item.TestCode, item.TestName, item.Category);
                        var code = !string.IsNullOrWhiteSpace(item.TubeColor)
                            ? TubePrintCode(item.TubeColor)
                            : TubePrintCode(spec.Color);
                        if (!tubes.Contains(code, StringComparer.OrdinalIgnoreCase))
                            tubes.Add(code);
                    }
                }
            }
            catch
            {
                /* fall through */
            }
        }

        // 2) Gói trên lịch hẹn → đúng 1 loại ống theo TubeRules (CBC→EDTA, đông máu→CITRATE…)
        if (tubes.Count == 0 && _packagesCol != null && !string.IsNullOrEmpty(appt.PackageId))
        {
            try
            {
                var pkg = await _packagesCol.Find(p => p.Id == appt.PackageId || p.Code == appt.PackageId).FirstOrDefaultAsync();
                if (pkg != null)
                {
                    var spec = TubeRules.For(pkg.Code, pkg.Name, pkg.Category);
                    tubes.Add(TubePrintCode(spec.Color));
                }
            }
            catch { /* ignore */ }
        }

        // 3) Fallback theo tên gói/notes — chỉ ống thật sự cần, không ép đủ 3 loại
        if (tubes.Count == 0)
        {
            var hint = $"{packageName} {appt.Notes}";
            var spec = TubeRules.For(null, hint, null);
            tubes.Add(TubePrintCode(spec.Color));
        }

        return tubes;
    }

    private async Task<int> NextDaySpecimenSequenceAsync(string datePrefix)
    {
        if (_samplesCol == null) return (int)(DateTime.UtcNow.Ticks % 9000) + 1000;
        try
        {
            var prefix = $"SPC-{datePrefix}-";
            var existing = await _samplesCol
                .Find(Builders<Sample>.Filter.Regex(s => s.Barcode, new MongoDB.Bson.BsonRegularExpression($"^{System.Text.RegularExpressions.Regex.Escape(prefix)}")))
                .Project(s => s.Barcode)
                .ToListAsync();
            return existing.Count + 1;
        }
        catch
        {
            return (int)(DateTime.UtcNow.Ticks % 9000) + 1000;
        }
    }

    private async Task<string> AllocateUniqueTubeBarcodeAsync(string datePrefix, string patientTag, string aptTag, int seq, string tubeCode)
    {
        // SPC-yymmdd-PTxxxx-APxxxx-0007-EDTA — gắn BN + lịch, không trùng buổi sáng đông BN
        string Build(int s) =>
            $"SPC-{datePrefix}-{patientTag}-{aptTag}-{s:D4}-{tubeCode}";

        var candidate = Build(seq);
        if (_samplesCol == null) return candidate;

        var guard = 0;
        while (guard++ < 50)
        {
            var exists = await _samplesCol.Find(s => s.Barcode == candidate).Limit(1).AnyAsync();
            if (!exists) return candidate;
            seq++;
            candidate = Build(seq);
        }

        return $"SPC-{datePrefix}-{patientTag}-{aptTag}-{DateTime.UtcNow.Ticks % 100000:D5}-{tubeCode}";
    }

    private static string ShortTag(string? raw, int len = 4)
    {
        var alnum = new string((raw ?? "XXXX").Where(char.IsLetterOrDigit).ToArray());
        if (alnum.Length == 0) alnum = "XXXX";
        return alnum.Length <= len ? alnum.ToUpperInvariant().PadLeft(len, '0') : alnum[^len..].ToUpperInvariant();
    }

    public async Task<IEnumerable<CheckedInAppointmentDto>> GetCheckedInAppointmentsAsync()
    {
        var result = new List<CheckedInAppointmentDto>();

        if (_appointmentsCol != null)
        {
            try
            {
                // Main flow: after check-in / during doctor visit, Lab still needs to intake
                // ordered specimens. InProgress was previously excluded → empty sample-list UI.
                var fb = Builders<Appointment>.Filter;
                var filter = fb.Or(
                    fb.Eq(a => a.Status, AppointmentStatus.Confirmed),
                    fb.Eq(a => a.Status, AppointmentStatus.CheckedIn),
                    fb.Eq(a => a.Status, AppointmentStatus.Pending),
                    fb.Eq(a => a.Status, AppointmentStatus.InProgress)
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
                    if (_packagesCol != null && !string.IsNullOrEmpty(a.PackageId))
                    {
                        var pkg = await _packagesCol.Find(p => p.Id == a.PackageId || p.Code == a.PackageId).FirstOrDefaultAsync();
                        if (pkg != null && !string.IsNullOrEmpty(pkg.Name)) packageName = pkg.Name;
                    }

                    var tubes = await ResolveTubePrintCodesAsync(a, packageName);

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
        // Sequence theo ngày — tránh trùng khi buổi sáng nhiều BN
        int specimenSequence = await NextDaySpecimenSequenceAsync(datePrefix);

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
            string? itemPackageId = apptDoc.PackageId;
            string? itemPackageCode = null;
            if (_packagesCol != null && !string.IsNullOrEmpty(apptDoc.PackageId))
            {
                var pkg = await _packagesCol.Find(p => p.Id == apptDoc.PackageId || p.Code == apptDoc.PackageId).FirstOrDefaultAsync();
                if (pkg != null)
                {
                    if (!string.IsNullOrEmpty(pkg.Name)) itemPackageName = pkg.Name;
                    itemPackageId = pkg.Id ?? apptDoc.PackageId;
                    itemPackageCode = pkg.Code;
                }
            }

            var itemTubes = await ResolveTubePrintCodesAsync(apptDoc, itemPackageName);
            var patientTag = ShortTag(patientId);
            var aptTag = ShortTag(apptDoc.AppointmentCode ?? aptId);
            var baseSpecimenId = $"SPC-{datePrefix}-{patientTag}-{aptTag}-{specimenSequence:D4}";
            var createdTubeCodes = new List<string>();

            // Update appointment status to InProgress when accessioned by Lab
            if (_appointmentsCol != null)
            {
                var updateDef = Builders<Appointment>.Update
                    .Set(a => a.Status, AppointmentStatus.InProgress)
                    .Set(a => a.UpdatedAt, DateTime.UtcNow);
                await _appointmentsCol.UpdateOneAsync(a => a.Id == apptDoc.Id, updateDef);
            }

            // Mỗi ống = 1 barcode riêng, gắn đúng BN + lịch; kiểm tra không trùng DB
            foreach (var tubeType in itemTubes)
            {
                var tubeBarcode = await AllocateUniqueTubeBarcodeAsync(datePrefix, patientTag, aptTag, specimenSequence, tubeType);
                specimenSequence++;

                // Đã có đúng ống này cho BN+lịch → không tạo trùng
                if (_samplesCol != null)
                {
                    var dup = await _samplesCol.Find(s =>
                        s.Barcode == tubeBarcode ||
                        (s.PatientId == patientId &&
                         s.TestOrderId == (apptDoc.Id ?? aptId) &&
                         s.TubeColor == TubeColorFromPrint(tubeType) &&
                         s.Status != SampleStatus.Rejected)).Limit(1).AnyAsync();
                    if (dup) continue;
                }

                createdTubeCodes.Add(tubeType);
                var sampleEntity = new Sample
                {
                    Id = Guid.NewGuid().ToString(),
                    Barcode = tubeBarcode,
                    TestOrderId = apptDoc.AppointmentCode ?? aptId,
                    AppointmentId = apptDoc.Id ?? aptId,
                    PatientId = patientId,
                    PatientName = patientName,
                    TestPackageName = itemPackageName,
                    PackageId = itemPackageId,
                    PackageCode = itemPackageCode,
                    SampleType = tubeType is "EDTA" or "CITRATE" ? "Blood" : "Serum",
                    TubeColor = TubeColorFromPrint(tubeType),
                    VolumeMl = tubeType == "EDTA" ? 2.0 : 4.0,
                    Status = SampleStatus.PendingCollection,
                    Priority = SamplePriority.Normal,
                    LabRoom = request.Location,
                    Notes = $"Batch {batchCode}. Patient {patientId}. Apt {apptDoc.AppointmentCode ?? aptId}. Target {request.TargetTime}",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                if (_samplesCol != null)
                {
                    await _samplesCol.InsertOneAsync(sampleEntity);
                }

                lock (_dbContext.Samples)
                {
                    _dbContext.Samples.Add(sampleEntity);
                }
            }

            if (createdTubeCodes.Count == 0) continue;

            items.Add(new SampleCollectionItem
            {
                SpecimenId = baseSpecimenId,
                AppointmentId = apptDoc.AppointmentCode ?? aptId,
                PatientId = patientId,
                PatientName = patientName,
                PackageName = itemPackageName,
                Tubes = createdTubeCodes,
                CollectorId = collectorId,
                CollectorName = collectorDisplayName,
                Status = "PendingCollection"
            });
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
            // Ưu tiên barcode thật đã ghi DB (gắn BN + lịch), tránh tạo mã ảo trùng
            List<Sample> liveTubes = [];
            if (_samplesCol != null && !string.IsNullOrWhiteSpace(item.PatientId))
            {
                try
                {
                    var filter = Builders<Sample>.Filter.And(
                        Builders<Sample>.Filter.Eq(s => s.PatientId, item.PatientId),
                        Builders<Sample>.Filter.Or(
                            Builders<Sample>.Filter.Eq(s => s.TestOrderId, item.AppointmentId),
                            Builders<Sample>.Filter.Regex(s => s.Notes, new MongoDB.Bson.BsonRegularExpression(batch.BatchCode))
                        )
                    );
                    liveTubes = await _samplesCol.Find(filter).ToListAsync();
                }
                catch
                {
                    liveTubes = [];
                }
            }

            if (liveTubes.Count > 0)
            {
                foreach (var s in liveTubes.OrderBy(x => x.Barcode))
                {
                    var tube = TubePrintCode(s.TubeColor);
                    labels.Add(new SpecimenBarcodeLabelDto
                    {
                        SpecimenId = s.Barcode,
                        PatientName = item.PatientName,
                        Tube = tube,
                        PackageName = item.PackageName,
                        BarcodeCode128 = s.Barcode,
                        SvgBarcode = GenerateSvgBarcode(s.Barcode)
                    });
                }
                continue;
            }

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
