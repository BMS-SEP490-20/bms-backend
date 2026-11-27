using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
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
/// Service managing laboratory Test Order intake, CLSI H3-A6 multi-tube ordering,
/// common-root Accession Tube ID generation (SMP-YYMMDD-XXXX-01, -02, ...),
/// and ISO 15189 §7.2.6.2 two-identifier tube label generation (UC37).
/// </summary>
public class SampleListService : ISampleListService
{
    private readonly ISampleListRepository _listRepository;
    private readonly BmsDbContext _dbContext;
    private readonly ISequenceGenerator? _sequences;
    private readonly IMongoCollection<Appointment>? _appointmentsCol;
    private readonly IMongoCollection<Patient>? _patientsCol;
    private readonly IMongoCollection<ServicePackage>? _packagesCol;
    private readonly IMongoCollection<Sample>? _samplesCol;
    private readonly IMongoCollection<TestOrder>? _ordersCol;

    public SampleListService(
        ISampleListRepository listRepository,
        BmsDbContext dbContext,
        IMongoDatabase? database = null,
        ISequenceGenerator? sequences = null)
    {
        _listRepository = listRepository;
        _dbContext = dbContext;
        _sequences = sequences;
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

    /// <summary>Map CLSI tube color -> short print code on barcode (EDTA / SST / CITRATE...).</summary>
    private static string TubePrintCode(string? colorOrType)
    {
        var t = (colorOrType ?? string.Empty).ToUpperInvariant();
        if (t.Contains("CULTURE") || t.Contains("CẤY")) return "CULTURE";
        if (t.Contains("LIGHTBLUE") || t.Contains("CITRATE") || t.Contains("BLUE") || t.Contains("XANH LAM") || t.Contains("XANH DƯƠNG")) return "CITRATE";
        if (t.Contains("PURPLE") || t.Contains("EDTA") || t.Contains("TÍM") || t.Contains("TIM")) return "EDTA";
        if (t.Contains("GRAY") || t.Contains("GREY") || t.Contains("FLUOR") || t.Contains("NAF") || t.Contains("XÁM")) return "NaF";
        if (t.Contains("GREEN") || t.Contains("HEPARIN") || t.Contains("XANH LÁ")) return "HEPARIN";
        return "SST";
    }

    /// <summary>CLSI H3-A6 Order of Draw (1 = drawn first).</summary>
    private static int ClsiDrawOrder(string printCode) => printCode.ToUpperInvariant() switch
    {
        "CULTURE" => 1,
        "CITRATE" => 2,
        "SST" => 3,
        "HEPARIN" => 4,
        "EDTA" => 5,
        "NAF" => 6,
        _ => 9
    };

    private static string TubeColorFromPrint(string printCode) => printCode.ToUpperInvariant() switch
    {
        "EDTA" => "Purple",
        "CITRATE" => "LightBlue",
        "NAF" => "Gray",
        "HEPARIN" => "Green",
        "CULTURE" => "BloodCulture",
        _ => "Gold"
    };

    private static string TubeLabelVi(string printCode) => printCode.ToUpperInvariant() switch
    {
        "CULTURE" => "CẤY MÁU",
        "CITRATE" => "XANH · CITRATE",
        "SST" => "VÀNG · SST",
        "HEPARIN" => "XANH LÁ · HEP",
        "EDTA" => "TÍM · EDTA",
        "NAF" => "XÁM · NaF",
        _ => printCode.ToUpperInvariant()
    };

    private static string FormatGenderVi(string? raw)
    {
        var g = (raw ?? string.Empty).Trim().ToLowerInvariant();
        if (g is "male" or "nam" or "m" or "1") return "Nam";
        if (g is "female" or "nữ" or "nu" or "f" or "2") return "Nữ";
        return string.IsNullOrWhiteSpace(raw) ? string.Empty : raw.Trim();
    }

    private static string InferSampleType(string tubeColor, string tubeType)
    {
        var t = $"{tubeColor} {tubeType}".ToUpperInvariant();
        if (t.Contains("EDTA") || t.Contains("PURPLE") || t.Contains("TÍM")) return "Blood";
        if (t.Contains("CITRATE") || t.Contains("BLUE") || t.Contains("XANH")) return "Plasma";
        if (t.Contains("URINE") || t.Contains("NƯỚC TIỂU")) return "Urine";
        return "Serum";
    }

    private static string InferAnticoagulant(string tubePrintCode) => tubePrintCode.ToUpperInvariant() switch
    {
        "EDTA" => "K2-EDTA",
        "CITRATE" => "Sodium Citrate 3.2%",
        "HEPARIN" => "Lithium Heparin",
        "NAF" => "Sodium Fluoride / Potassium Oxalate",
        "CULTURE" => "Canh trường cấy máu",
        _ => "Clot Activator & Gel"
    };

    private static string NormalizeLabRoom(string? rawLocation)
    {
        if (!string.IsNullOrWhiteSpace(rawLocation))
        {
            var m = Regex.Match(rawLocation.Trim(), @"\b(LAB-[A-Z0-9-]+)\b", RegexOptions.IgnoreCase);
            if (m.Success) return m.Groups[1].Value.ToUpperInvariant();
        }
        return "LAB-ROOM-01";
    }

    private static List<string> ResolveTubesFromOrder(TestOrder order)
    {
        var tubes = new List<string>();
        if (order.Items != null)
        {
            foreach (var item in order.Items)
            {
                var spec = TubeRules.For(item.TestCode, item.TestName, item.Category);
                var code = !string.IsNullOrWhiteSpace(item.TubeColor)
                    ? TubePrintCode(item.TubeColor)
                    : TubePrintCode(spec.Color);
                if (!tubes.Contains(code, StringComparer.OrdinalIgnoreCase))
                    tubes.Add(code);
            }
        }
        if (tubes.Count == 0) tubes.Add("SST");
        return tubes.OrderBy(ClsiDrawOrder).ToList();
    }

    /// <summary>
    /// Allocates ONE common Accession Root per patient/order draw: SMP-YYMMDD-XXXX.
    /// All tubes belonging to the same draw share this root and get sequential suffixes (-01, -02, -03...).
    /// </summary>
    private async Task<string> AllocateAccessionRootAsync(DateOnly localDate, string? existingOrderId = null)
    {
        var dateShort = localDate.ToString("yyMMdd");

        // Reuse existing accession root if this order already had a tube collected today (e.g. re-collecting 1 rejected tube)
        if (_samplesCol != null && !string.IsNullOrWhiteSpace(existingOrderId))
        {
            var existingBarcodes = await _samplesCol
                .Find(s => s.TestOrderId == existingOrderId)
                .Project(s => s.Barcode)
                .ToListAsync();

            foreach (var bc in existingBarcodes)
            {
                var m = Regex.Match(bc ?? string.Empty, @"^(SMP-\d{6}-\d{4})-\d{2,}$", RegexOptions.IgnoreCase);
                if (m.Success) return m.Groups[1].Value.ToUpperInvariant();
            }
        }

        var dateKey = localDate.ToString("yyyy-MM-dd");
        for (var attempt = 0; attempt < 50; attempt++)
        {
            long seq;
            if (_sequences != null)
            {
                seq = await _sequences.NextAsync($"smp-acc:{dateKey}", CancellationToken.None);
            }
            else if (_samplesCol != null)
            {
                var prefix = $"SMP-{dateShort}-";
                var existingCount = await _samplesCol
                    .CountDocumentsAsync(Builders<Sample>.Filter.Regex(s => s.Barcode, new MongoDB.Bson.BsonRegularExpression($"^{Regex.Escape(prefix)}")));
                seq = existingCount + 1 + attempt;
            }
            else
            {
                seq = (DateTime.UtcNow.Ticks % 9000) + 1 + attempt;
            }

            var root = $"SMP-{dateShort}-{seq:D4}";
            if (_samplesCol == null) return root;

            var anyWithRoot = await _samplesCol
                .Find(Builders<Sample>.Filter.Regex(s => s.Barcode, new MongoDB.Bson.BsonRegularExpression($"^{Regex.Escape(root)}(-|$)")))
                .Limit(1)
                .AnyAsync();
            if (!anyWithRoot) return root;
        }

        return $"SMP-{dateShort}-{DateTime.UtcNow.Ticks % 10000:D4}";
    }

    private async Task<string> AllocateTubeBarcodeWithRootAsync(string accessionRoot, int startSuffix)
    {
        var suffix = Math.Max(1, startSuffix);
        for (var guard = 0; guard < 99; guard++)
        {
            var candidate = $"{accessionRoot}-{suffix:D2}";
            if (_samplesCol == null) return candidate;

            var exists = await _samplesCol.Find(s => s.Barcode == candidate).Limit(1).AnyAsync();
            if (!exists) return candidate;
            suffix++;
        }
        return $"{accessionRoot}-{DateTime.UtcNow.Ticks % 1000:D3}";
    }

    /// <summary>
    /// Step 1: Returns active Doctor Test Orders that still have at least 1 unaccepted (or rejected) tube
    /// waiting for Lab acceptance & Barcode generation.
    /// Supports partial rejection recovery: if 1 of 3 tubes was Rejected, only the missing tube re-appears here.
    /// </summary>
    public async Task<IEnumerable<CheckedInAppointmentDto>> GetCheckedInAppointmentsAsync()
    {
        var result = new List<CheckedInAppointmentDto>();
        if (_ordersCol == null) return result;

        try
        {
            // Map orderKey -> set of active (non-Rejected) tube print codes already in blood_samples
            var activeTubesByOrder = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            if (_samplesCol != null)
            {
                var activeSamples = await _samplesCol
                    .Find(Builders<Sample>.Filter.Ne(s => s.StatusString, SampleStatus.Rejected.ToString()))
                    .Project(s => new { s.TestOrderId, s.TubeColor, s.Notes })
                    .ToListAsync();

                void AddTube(string key, string tubeCode)
                {
                    if (string.IsNullOrWhiteSpace(key)) return;
                    if (!activeTubesByOrder.TryGetValue(key, out var set))
                    {
                        set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        activeTubesByOrder[key] = set;
                    }
                    set.Add(tubeCode);
                }

                foreach (var s in activeSamples)
                {
                    var tubeCode = TubePrintCode(s.TubeColor);
                    if (!string.IsNullOrWhiteSpace(s.TestOrderId))
                        AddTube(s.TestOrderId.Trim(), tubeCode);
                    if (!string.IsNullOrWhiteSpace(s.Notes))
                    {
                        var ordMatch = Regex.Match(s.Notes, @"\bORD-\d{8}-\d+\b", RegexOptions.IgnoreCase);
                        if (ordMatch.Success) AddTube(ordMatch.Value, tubeCode);
                    }
                }
            }

            var orderFilter = Builders<TestOrder>.Filter.And(
                Builders<TestOrder>.Filter.Ne(o => o.Status, TestOrderStatus.Draft),
                Builders<TestOrder>.Filter.Ne(o => o.Status, TestOrderStatus.Cancelled)
            );

            var orders = await _ordersCol
                .Find(orderFilter)
                .SortByDescending(o => o.CreatedAt)
                .Limit(100)
                .ToListAsync();

            var vnOffset = TimeSpan.FromHours(7);

            foreach (var order in orders)
            {
                var requiredTubes = ResolveTubesFromOrder(order);
                var existingTubes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                if (activeTubesByOrder.TryGetValue(order.Id, out var byId))
                    existingTubes.UnionWith(byId);
                if (!string.IsNullOrWhiteSpace(order.OrderNumber) && activeTubesByOrder.TryGetValue(order.OrderNumber, out var byNo))
                    existingTubes.UnionWith(byNo);
                if (!string.IsNullOrWhiteSpace(order.AppointmentId) && activeTubesByOrder.TryGetValue(order.AppointmentId, out var byApt))
                    existingTubes.UnionWith(byApt);

                var pendingTubes = requiredTubes
                    .Where(t => !existingTubes.Contains(t))
                    .OrderBy(ClsiDrawOrder)
                    .ToList();

                // All required tubes already have active samples -> order is fully in Step 2+
                if (pendingTubes.Count == 0) continue;

                string patientName = order.PatientName;
                if (string.IsNullOrWhiteSpace(patientName) && _patientsCol != null && !string.IsNullOrWhiteSpace(order.PatientId))
                {
                    var pat = await _patientsCol.Find(p => p.Id == order.PatientId || p.PatientCode == order.PatientId).FirstOrDefaultAsync();
                    if (pat != null && !string.IsNullOrWhiteSpace(pat.FullName)) patientName = pat.FullName;
                }
                if (string.IsNullOrWhiteSpace(patientName)) patientName = "Bệnh nhân";

                var testSummary = order.Items != null && order.Items.Count > 0
                    ? string.Join(", ", order.Items.Select(i => !string.IsNullOrWhiteSpace(i.TestName) ? i.TestName : i.TestCode).Distinct())
                    : "Xét nghiệm theo chỉ định bác sĩ";

                var localCreated = order.CreatedAt.Kind == DateTimeKind.Utc
                    ? order.CreatedAt + vnOffset
                    : order.CreatedAt;

                result.Add(new CheckedInAppointmentDto
                {
                    AppointmentId = !string.IsNullOrWhiteSpace(order.AppointmentId) ? order.AppointmentId : order.OrderNumber,
                    OrderId = order.Id,
                    OrderNumber = order.OrderNumber,
                    DoctorName = order.DoctorName,
                    PatientId = order.PatientId,
                    PatientName = patientName,
                    PackageName = testSummary,
                    Time = localCreated.ToString("HH:mm"),
                    Tubes = pendingTubes,
                    Priority = order.Priority.ToString()
                });
            }
        }
        catch
        {
            // Fall back to empty result on DB exception
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

    /// <summary>
    /// Step 1 Confirmation: Lab Technician accepts selected Doctor Test Orders,
    /// sorts tubes by CLSI H3-A6 Order of Draw, assigns common-root Tube IDs (SMP-YYMMDD-XXXX-01, -02, ...),
    /// and inserts PendingCollection specimens into blood_samples for Step 2 (Sample Queue & Collection).
    /// </summary>
    public async Task<SampleListResponseDto> CreateCollectionListAsync(CreateSampleListRequestDto request, string? currentUserId = null)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));
        if (request.AppointmentIds == null || !request.AppointmentIds.Any())
        {
            throw new ArgumentException("Vui lòng chọn ít nhất một chỉ định xét nghiệm để tiếp nhận.", nameof(request.AppointmentIds));
        }

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

        var batchCode = await _listRepository.GenerateNextBatchCodeAsync();
        var now = DateTime.UtcNow;
        var localDate = ClinicTime.LocalDate(now);
        var smpDay = localDate.ToString("yyyy-MM-dd");
        var normalizedRoom = NormalizeLabRoom(request.Location);

        var items = new List<SampleCollectionItem>();

        foreach (var rawKey in request.AppointmentIds.Where(k => !string.IsNullOrWhiteSpace(k)).Distinct())
        {
            var key = rawKey.Trim();
            TestOrder? matchedOrder = null;
            if (_ordersCol != null)
            {
                var orderFilter = Builders<TestOrder>.Filter.And(
                    Builders<TestOrder>.Filter.Ne(o => o.Status, TestOrderStatus.Draft),
                    Builders<TestOrder>.Filter.Ne(o => o.Status, TestOrderStatus.Cancelled),
                    Builders<TestOrder>.Filter.Or(
                        Builders<TestOrder>.Filter.Eq(o => o.Id, key),
                        Builders<TestOrder>.Filter.Eq(o => o.OrderNumber, key),
                        Builders<TestOrder>.Filter.Eq(o => o.AppointmentId, key)
                    )
                );
                matchedOrder = await _ordersCol.Find(orderFilter).SortByDescending(o => o.CreatedAt).FirstOrDefaultAsync();
            }

            Appointment? apptDoc = null;
            if (_appointmentsCol != null)
            {
                var aptLookup = matchedOrder?.AppointmentId ?? key;
                apptDoc = await _appointmentsCol.Find(a => a.Id == aptLookup || a.AppointmentCode == aptLookup).FirstOrDefaultAsync();
            }

            if (matchedOrder == null && apptDoc == null) continue;

            var patientId = matchedOrder?.PatientId ?? apptDoc?.PatientId ?? string.Empty;
            if (string.IsNullOrWhiteSpace(patientId)) continue;

            var patientName = matchedOrder?.PatientName ?? string.Empty;
            string? patientGender = matchedOrder?.PatientGender;
            int? patientAge = matchedOrder?.PatientAge;

            if (_patientsCol != null && (string.IsNullOrWhiteSpace(patientName) || string.IsNullOrWhiteSpace(patientGender)))
            {
                var pat = await _patientsCol.Find(p => p.Id == patientId || p.PatientCode == patientId).FirstOrDefaultAsync();
                if (pat != null)
                {
                    if (string.IsNullOrWhiteSpace(patientName) && !string.IsNullOrWhiteSpace(pat.FullName))
                        patientName = pat.FullName;
                    if (string.IsNullOrWhiteSpace(patientGender))
                        patientGender = pat.GenderString;
                }
            }
            if (string.IsNullOrWhiteSpace(patientName)) patientName = "Bệnh nhân";

            var targetOrderId = matchedOrder?.Id ?? apptDoc?.AppointmentCode ?? key;
            var displayOrderOrApt = matchedOrder?.OrderNumber ?? apptDoc?.AppointmentCode ?? key;

            // Build tube groups from Doctor's TestOrder items and SORT BY CLSI H3-A6 ORDER OF DRAW
            var tubeSpecs = new List<(string PrintCode, int ClsiOrder, string Color, string Type, string TestCodes, string PackageName, string? FirstCode)>();
            if (matchedOrder?.Items != null && matchedOrder.Items.Count > 0)
            {
                var groups = matchedOrder.Items
                    .GroupBy(i =>
                    {
                        var rule = TubeRules.For(i.TestCode, i.TestName, i.Category);
                        return !string.IsNullOrWhiteSpace(i.TubeColor)
                            ? TubePrintCode(i.TubeColor)
                            : TubePrintCode(rule.Color);
                    })
                    .OrderBy(g => ClsiDrawOrder(g.Key))
                    .ToList();

                foreach (var g in groups)
                {
                    var printCode = g.Key;
                    var first = g.First();
                    var color = !string.IsNullOrWhiteSpace(first.TubeColor) ? first.TubeColor : TubeColorFromPrint(printCode);
                    var type = !string.IsNullOrWhiteSpace(first.TubeType) ? first.TubeType : printCode;
                    var testCodes = string.Join(", ", g.Select(x => x.TestCode).Where(c => !string.IsNullOrWhiteSpace(c)).Distinct());
                    var testNames = string.Join(", ", g.Select(x => !string.IsNullOrWhiteSpace(x.TestName) ? x.TestName : x.TestCode).Distinct());
                    tubeSpecs.Add((printCode, ClsiDrawOrder(printCode), color, type, string.IsNullOrWhiteSpace(testCodes) ? testNames : testCodes, testNames, first.TestCode));
                }
            }
            else if (apptDoc != null)
            {
                string pkgName = !string.IsNullOrEmpty(apptDoc.Notes) ? apptDoc.Notes : "Xét nghiệm máu";
                string? pkgCode = null;
                if (_packagesCol != null && !string.IsNullOrEmpty(apptDoc.PackageId))
                {
                    var pkg = await _packagesCol.Find(p => p.Id == apptDoc.PackageId || p.Code == apptDoc.PackageId).FirstOrDefaultAsync();
                    if (pkg != null)
                    {
                        pkgName = pkg.Name;
                        pkgCode = pkg.Code;
                    }
                }
                var spec = TubeRules.For(pkgCode, pkgName, null);
                var printCode = TubePrintCode(spec.Color);
                tubeSpecs.Add((printCode, ClsiDrawOrder(printCode), TubeColorFromPrint(printCode), spec.TubeType, pkgCode ?? pkgName, pkgName, pkgCode));
            }

            // Filter out tubes that already have an active (non-Rejected) sample for this order
            var specsToCreate = new List<(string PrintCode, int ClsiOrder, string Color, string Type, string TestCodes, string PackageName, string? FirstCode)>();
            if (_samplesCol != null)
            {
                var existingForOrder = await _samplesCol.Find(s =>
                    s.PatientId == patientId &&
                    s.TestOrderId == targetOrderId &&
                    s.StatusString != SampleStatus.Rejected.ToString()).ToListAsync();

                foreach (var spec in tubeSpecs)
                {
                    var dup = existingForOrder.Any(s =>
                        string.Equals(TubePrintCode(s.TubeColor), spec.PrintCode, StringComparison.OrdinalIgnoreCase));
                    if (!dup) specsToCreate.Add(spec);
                }
            }
            else
            {
                specsToCreate = tubeSpecs;
            }

            if (specsToCreate.Count == 0) continue;

            // Allocate ONE common Accession Root for this patient/order draw (SMP-YYMMDD-XXXX)
            var accessionRoot = await AllocateAccessionRootAsync(localDate, targetOrderId);
            var createdTubeCodes = new List<string>();
            string firstBarcode = string.Empty;
            var tubeSeq = 1;

            foreach (var spec in specsToCreate)
            {
                var barcode = await AllocateTubeBarcodeWithRootAsync(accessionRoot, tubeSeq);
                tubeSeq++;

                if (string.IsNullOrEmpty(firstBarcode)) firstBarcode = barcode;
                createdTubeCodes.Add(spec.PrintCode);

                var sampleEntity = new Sample
                {
                    Id = Guid.NewGuid().ToString(),
                    Barcode = barcode,
                    SmpDay = smpDay,
                    DrawOrder = spec.ClsiOrder,
                    TestOrderId = targetOrderId,
                    PatientId = patientId,
                    PatientName = patientName,
                    PatientGender = patientGender,
                    PatientAge = patientAge,
                    TestPackageName = spec.TestCodes,
                    PackageCode = spec.FirstCode,
                    SampleType = InferSampleType(spec.Color, spec.Type),
                    TubeColor = spec.Color,
                    Anticoagulant = InferAnticoagulant(spec.PrintCode),
                    VolumeMl = spec.PrintCode == "EDTA" ? 2.0 : 4.0,
                    Status = SampleStatus.PendingCollection,
                    Priority = matchedOrder?.Priority ?? SamplePriority.Normal,
                    LabRoom = normalizedRoom,
                    Notes = matchedOrder != null
                        ? $"Từ y lệnh {matchedOrder.OrderNumber} • Đợt {batchCode}"
                        : $"Đợt {batchCode} • Lịch {displayOrderOrApt}",
                    CreatedAt = now,
                    UpdatedAt = now
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
                SpecimenId = firstBarcode,
                AppointmentId = displayOrderOrApt,
                PatientId = patientId,
                PatientName = patientName,
                PackageName = specsToCreate.FirstOrDefault().PackageName ?? "Xét nghiệm máu",
                Tubes = createdTubeCodes,
                CollectorId = collectorId,
                CollectorName = collectorDisplayName,
                Status = "PendingCollection"
            });
        }

        var collectionList = new SampleCollectionList
        {
            BatchCode = batchCode,
            Location = normalizedRoom,
            TargetTime = request.TargetTime,
            ScheduledDate = now.Date,
            CollectorId = collectorId,
            CollectorName = collectorDisplayName,
            Status = SampleCollectionListStatus.Confirmed,
            Items = items,
            CreatedAt = now,
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
        var vnNowStr = (DateTime.UtcNow + TimeSpan.FromHours(7)).ToString("HH:mm dd/MM/yy");

        foreach (var item in batch.Items)
        {
            // Lookup Patient 2nd identifier (BirthYear, Gender, PatientCode) for ISO 15189 §7.2.6.2
            string patientCodeDisplay = item.PatientId;
            string genderVi = string.Empty;
            string birthYear = string.Empty;
            int? patientAge = null;

            if (_patientsCol != null && !string.IsNullOrWhiteSpace(item.PatientId))
            {
                try
                {
                    var pat = await _patientsCol.Find(p => p.Id == item.PatientId || p.PatientCode == item.PatientId).FirstOrDefaultAsync();
                    if (pat != null)
                    {
                        if (!string.IsNullOrWhiteSpace(pat.PatientCode)) patientCodeDisplay = pat.PatientCode;
                        genderVi = FormatGenderVi(pat.GenderString);
                        if (pat.DateOfBirth.HasValue)
                        {
                            birthYear = pat.DateOfBirth.Value.Year.ToString();
                            patientAge = Math.Max(0, DateTime.UtcNow.Year - pat.DateOfBirth.Value.Year);
                        }
                    }
                }
                catch { /* non-fatal */ }
            }

            List<Sample> liveTubes = [];
            if (_samplesCol != null && !string.IsNullOrWhiteSpace(item.PatientId))
            {
                try
                {
                    var filter = Builders<Sample>.Filter.And(
                        Builders<Sample>.Filter.Eq(s => s.PatientId, item.PatientId),
                        Builders<Sample>.Filter.Or(
                            Builders<Sample>.Filter.Eq(s => s.Barcode, item.SpecimenId),
                            Builders<Sample>.Filter.Regex(s => s.Notes, new MongoDB.Bson.BsonRegularExpression(Regex.Escape(batch.BatchCode)))
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
                var orderedTubes = liveTubes
                    .OrderBy(x => x.DrawOrder ?? 99)
                    .ThenBy(x => x.Barcode, StringComparer.Ordinal)
                    .ToList();
                var totalTubes = orderedTubes.Count;

                for (var i = 0; i < totalTubes; i++)
                {
                    var s = orderedTubes[i];
                    var tube = TubePrintCode(s.TubeColor);
                    var ordNo = item.AppointmentId;
                    if (!string.IsNullOrWhiteSpace(s.Notes))
                    {
                        var m = Regex.Match(s.Notes, @"\bORD-\d{8}-\d+\b", RegexOptions.IgnoreCase);
                        if (m.Success) ordNo = m.Value;
                    }

                    labels.Add(new SpecimenBarcodeLabelDto
                    {
                        SpecimenId = s.Barcode,
                        PatientId = patientCodeDisplay,
                        PatientName = item.PatientName,
                        PatientGender = !string.IsNullOrWhiteSpace(genderVi) ? genderVi : FormatGenderVi(s.PatientGender),
                        PatientBirthYear = birthYear,
                        PatientAge = patientAge ?? s.PatientAge,
                        OrderNumber = ordNo,
                        TubeIndex = i + 1,
                        TotalTubes = totalTubes,
                        Tube = tube,
                        TubeLabelVi = TubeLabelVi(tube),
                        TestCodes = !string.IsNullOrWhiteSpace(s.TestPackageName) ? s.TestPackageName : item.PackageName,
                        PackageName = item.PackageName,
                        BarcodeCode128 = s.Barcode,
                        SvgBarcode = GenerateSvgBarcode(s.Barcode),
                        PrintedAt = vnNowStr
                    });
                }
                continue;
            }

            var fallbackTotal = Math.Max(1, item.Tubes.Count);
            for (var i = 0; i < item.Tubes.Count; i++)
            {
                var tube = item.Tubes[i];
                labels.Add(new SpecimenBarcodeLabelDto
                {
                    SpecimenId = item.SpecimenId,
                    PatientId = patientCodeDisplay,
                    PatientName = item.PatientName,
                    PatientGender = genderVi,
                    PatientBirthYear = birthYear,
                    PatientAge = patientAge,
                    OrderNumber = item.AppointmentId,
                    TubeIndex = i + 1,
                    TotalTubes = fallbackTotal,
                    Tube = tube,
                    TubeLabelVi = TubeLabelVi(tube),
                    TestCodes = item.PackageName,
                    PackageName = item.PackageName,
                    BarcodeCode128 = item.SpecimenId,
                    SvgBarcode = GenerateSvgBarcode(item.SpecimenId),
                    PrintedAt = vnNowStr
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

    private static string GenerateSvgBarcode(string code) => code;

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
