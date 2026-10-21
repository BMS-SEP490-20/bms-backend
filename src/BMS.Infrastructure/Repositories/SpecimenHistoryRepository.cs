using System.Collections.Concurrent;
using BMS.Core.Entities;
using BMS.Core.Interfaces;
using BMS.Infrastructure.Data;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace BMS.Infrastructure.Repositories;

/// <summary>
/// Repository implementing specimen history retrieval, event log tracking and cold storage archive (UC70).
/// Supports MongoDB and provides an in-memory thread-safe fallback with standard clinical seed data.
/// </summary>
public sealed class SpecimenHistoryRepository : ISpecimenHistoryRepository
{
    private readonly MongoDbContext _context;
    private readonly ILogger<SpecimenHistoryRepository> _logger;

    private static readonly ConcurrentDictionary<string, SpecimenHistoryRecord> _memoryStore = new();
    private static bool _seeded;
    private static readonly object _seedLock = new();

    public SpecimenHistoryRepository(MongoDbContext context, ILogger<SpecimenHistoryRepository> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task EnsureDefaultSpecimensSeededAsync(CancellationToken ct = default)
    {
        if (_seeded) return;

        lock (_seedLock)
        {
            if (_seeded) return;
            _seeded = true;
        }

        var defaultRecords = GetStandardSeedRecords();
        foreach (var rec in defaultRecords)
        {
            _memoryStore.TryAdd(rec.Barcode, rec);
        }

        if (_context.SpecimenHistories != null)
        {
            try
            {
                var count = await _context.SpecimenHistories.CountDocumentsAsync(
                    Builders<SpecimenHistoryRecord>.Filter.Empty,
                    cancellationToken: ct);

                if (count < 3)
                {
                    foreach (var rec in defaultRecords)
                    {
                        var exists = await _context.SpecimenHistories.Find(
                            Builders<SpecimenHistoryRecord>.Filter.Eq(x => x.Barcode, rec.Barcode)
                        ).AnyAsync(ct);

                        if (!exists)
                        {
                            var clone = new SpecimenHistoryRecord
                            {
                                Id = MongoDB.Bson.ObjectId.GenerateNewId().ToString(),
                                Barcode = rec.Barcode,
                                RfidTag = rec.RfidTag,
                                SampleType = rec.SampleType,
                                TubeColor = rec.TubeColor,
                                Anticoagulant = rec.Anticoagulant,
                                VolumeMl = rec.VolumeMl,
                                MinimumRequiredVolumeMl = rec.MinimumRequiredVolumeMl,
                                PatientId = rec.PatientId,
                                PatientName = rec.PatientName,
                                PatientGender = rec.PatientGender,
                                PatientAge = rec.PatientAge,
                                PatientPhone = rec.PatientPhone,
                                OrderId = rec.OrderId,
                                AppointmentId = rec.AppointmentId,
                                OrderDate = rec.OrderDate,
                                DoctorId = rec.DoctorId,
                                DoctorName = rec.DoctorName,
                                Department = rec.Department,
                                TestPackageName = rec.TestPackageName,
                                TestCriteriaCodes = rec.TestCriteriaCodes,
                                Status = rec.Status,
                                Priority = rec.Priority,
                                CollectionTime = rec.CollectionTime,
                                CollectorId = rec.CollectorId,
                                CollectorName = rec.CollectorName,
                                CollectionSite = rec.CollectionSite,
                                Timeline = rec.Timeline,
                                Storage = rec.Storage,
                                Notes = rec.Notes,
                                IsActive = rec.IsActive,
                                CreatedAt = rec.CreatedAt,
                                UpdatedAt = rec.UpdatedAt
                            };
                            await _context.SpecimenHistories.InsertOneAsync(clone, cancellationToken: ct);
                        }
                    }
                    _logger.LogInformation("[SpecimenHistoryRepository] Seeded default clinical specimen history records into MongoDB.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[SpecimenHistoryRepository] Could not seed specimen histories to MongoDB. Running on in-memory store.");
            }
        }
    }

    public async Task<(List<SpecimenHistoryRecord> Items, long TotalCount)> GetPagedAsync(
        int page,
        int pageSize,
        string? keyword = null,
        string? sampleType = null,
        string? status = null,
        DateTime? fromDate = null,
        DateTime? toDate = null,
        CancellationToken ct = default)
    {
        await EnsureDefaultSpecimensSeededAsync(ct);

        if (_context.SpecimenHistories != null)
        {
            try
            {
                var filterBuilder = Builders<SpecimenHistoryRecord>.Filter;
                var filter = filterBuilder.Eq(x => x.IsActive, true);

                if (!string.IsNullOrWhiteSpace(keyword))
                {
                    var kw = keyword.Trim();
                    var regex = new MongoDB.Bson.BsonRegularExpression(kw, "i");
                    filter &= filterBuilder.Or(
                        filterBuilder.Regex(x => x.Barcode, regex),
                        filterBuilder.Regex(x => x.PatientId, regex),
                        filterBuilder.Regex(x => x.PatientName, regex),
                        filterBuilder.Regex(x => x.OrderId, regex),
                        filterBuilder.Regex(x => x.RfidTag, regex)
                    );
                }

                if (!string.IsNullOrWhiteSpace(sampleType) && !sampleType.Equals("ALL", StringComparison.OrdinalIgnoreCase))
                {
                    filter &= filterBuilder.Eq(x => x.SampleType, sampleType.Trim());
                }

                if (!string.IsNullOrWhiteSpace(status) && !status.Equals("ALL", StringComparison.OrdinalIgnoreCase))
                {
                    filter &= filterBuilder.Eq(x => x.Status, status.Trim());
                }

                if (fromDate.HasValue)
                {
                    filter &= filterBuilder.Gte(x => x.CollectionTime, fromDate.Value);
                }

                if (toDate.HasValue)
                {
                    filter &= filterBuilder.Lte(x => x.CollectionTime, toDate.Value);
                }

                var total = await _context.SpecimenHistories.CountDocumentsAsync(filter, cancellationToken: ct);
                var items = await _context.SpecimenHistories
                    .Find(filter)
                    .SortByDescending(x => x.CollectionTime)
                    .Skip((page - 1) * pageSize)
                    .Limit(pageSize)
                    .ToListAsync(ct);

                return (items, total);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[SpecimenHistoryRepository] MongoDB query failed. Fallback to in-memory store.");
            }
        }

        // In-memory fallback
        var query = _memoryStore.Values.Where(x => x.IsActive);

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var kw = keyword.Trim();
            query = query.Where(x =>
                x.Barcode.Contains(kw, StringComparison.OrdinalIgnoreCase) ||
                x.PatientId.Contains(kw, StringComparison.OrdinalIgnoreCase) ||
                x.PatientName.Contains(kw, StringComparison.OrdinalIgnoreCase) ||
                x.OrderId.Contains(kw, StringComparison.OrdinalIgnoreCase) ||
                (x.RfidTag != null && x.RfidTag.Contains(kw, StringComparison.OrdinalIgnoreCase)));
        }

        if (!string.IsNullOrWhiteSpace(sampleType) && !sampleType.Equals("ALL", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(x => x.SampleType.Equals(sampleType.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(status) && !status.Equals("ALL", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(x => x.Status.Equals(status.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        if (fromDate.HasValue)
        {
            query = query.Where(x => x.CollectionTime >= fromDate.Value);
        }

        if (toDate.HasValue)
        {
            query = query.Where(x => x.CollectionTime <= toDate.Value);
        }

        var totalCount = query.LongCount();
        var pagedItems = query
            .OrderByDescending(x => x.CollectionTime)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        return (pagedItems, totalCount);
    }

    public async Task<SpecimenHistoryRecord?> GetByBarcodeAsync(string barcode, CancellationToken ct = default)
    {
        await EnsureDefaultSpecimensSeededAsync(ct);
        var norm = barcode.Trim().ToUpperInvariant();

        if (_context.SpecimenHistories != null)
        {
            try
            {
                var filter = Builders<SpecimenHistoryRecord>.Filter.Or(
                    Builders<SpecimenHistoryRecord>.Filter.Eq(x => x.Barcode, norm),
                    Builders<SpecimenHistoryRecord>.Filter.Eq(x => x.Id, barcode.Trim()),
                    Builders<SpecimenHistoryRecord>.Filter.Eq(x => x.RfidTag, norm)
                );
                var found = await _context.SpecimenHistories.Find(filter).FirstOrDefaultAsync(ct);
                if (found != null) return found;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[SpecimenHistoryRepository] MongoDB find by barcode failed. Fallback to in-memory.");
            }
        }

        return _memoryStore.Values.FirstOrDefault(x =>
            x.Barcode.Equals(norm, StringComparison.OrdinalIgnoreCase) ||
            x.Id.Equals(barcode.Trim(), StringComparison.OrdinalIgnoreCase) ||
            (x.RfidTag != null && x.RfidTag.Equals(norm, StringComparison.OrdinalIgnoreCase)));
    }

    public async Task<SpecimenHistoryRecord?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        return await GetByBarcodeAsync(id, ct);
    }

    public async Task<List<SpecimenHistoryRecord>> GetByPatientIdAsync(string patientId, CancellationToken ct = default)
    {
        await EnsureDefaultSpecimensSeededAsync(ct);
        var pid = patientId.Trim();

        if (_context.SpecimenHistories != null)
        {
            try
            {
                var filter = Builders<SpecimenHistoryRecord>.Filter.Eq(x => x.PatientId, pid) &
                             Builders<SpecimenHistoryRecord>.Filter.Eq(x => x.IsActive, true);

                return await _context.SpecimenHistories
                    .Find(filter)
                    .SortByDescending(x => x.CollectionTime)
                    .ToListAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[SpecimenHistoryRepository] MongoDB find by patient failed. Fallback to in-memory.");
            }
        }

        return _memoryStore.Values
            .Where(x => x.IsActive && x.PatientId.Equals(pid, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.CollectionTime)
            .ToList();
    }

    public async Task<SpecimenHistoryRecord> CreateAsync(SpecimenHistoryRecord record, CancellationToken ct = default)
    {
        await EnsureDefaultSpecimensSeededAsync(ct);

        if (string.IsNullOrWhiteSpace(record.Id))
        {
            record.Id = MongoDB.Bson.ObjectId.GenerateNewId().ToString();
        }

        record.CreatedAt = DateTime.UtcNow;
        record.UpdatedAt = DateTime.UtcNow;

        if (_context.SpecimenHistories != null)
        {
            try
            {
                await _context.SpecimenHistories.InsertOneAsync(record, cancellationToken: ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[SpecimenHistoryRepository] MongoDB insert failed. Saving to in-memory store.");
            }
        }

        _memoryStore[record.Barcode] = record;
        return record;
    }

    public async Task<SpecimenHistoryRecord?> UpdateAsync(SpecimenHistoryRecord record, CancellationToken ct = default)
    {
        await EnsureDefaultSpecimensSeededAsync(ct);

        record.UpdatedAt = DateTime.UtcNow;

        if (_context.SpecimenHistories != null)
        {
            try
            {
                var filter = Builders<SpecimenHistoryRecord>.Filter.Eq(x => x.Barcode, record.Barcode);
                await _context.SpecimenHistories.ReplaceOneAsync(filter, record, cancellationToken: ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[SpecimenHistoryRepository] MongoDB replace failed. Fallback to in-memory.");
            }
        }

        _memoryStore[record.Barcode] = record;
        return record;
    }

    public async Task<bool> AddTimelineEventAsync(string barcodeOrId, SpecimenTimelineEvent eventItem, CancellationToken ct = default)
    {
        var existing = await GetByBarcodeAsync(barcodeOrId, ct);
        if (existing == null) return false;

        existing.Timeline.Add(eventItem);
        if (!string.IsNullOrWhiteSpace(eventItem.Status))
        {
            existing.Status = eventItem.Status;
        }
        existing.UpdatedAt = DateTime.UtcNow;

        await UpdateAsync(existing, ct);
        return true;
    }

    public async Task<bool> UpdateStorageAsync(string barcodeOrId, SpecimenStorageInfo storageInfo, CancellationToken ct = default)
    {
        var existing = await GetByBarcodeAsync(barcodeOrId, ct);
        if (existing == null) return false;

        existing.Storage = storageInfo;
        existing.UpdatedAt = DateTime.UtcNow;

        // Auto append storage update event to timeline
        existing.Timeline.Add(new SpecimenTimelineEvent
        {
            EventId = Guid.NewGuid().ToString("N"),
            EventType = "StorageArchived",
            Title = "Cập Nhật Vị Trí Lưu Trữ Mẫu",
            Description = $"Chuyển lưu trữ tại {storageInfo.StorageUnit} ({storageInfo.RackId} - {storageInfo.SlotPosition}). Nhiệt độ giám sát: {storageInfo.CurrentTemperature:F1}°C.",
            Timestamp = DateTime.UtcNow,
            OperatorId = "CURRENT_USER",
            OperatorName = storageInfo.StoredBy,
            OperatorRole = "LabTech",
            Location = storageInfo.StorageUnit,
            Status = existing.Status,
            TemperatureC = storageInfo.CurrentTemperature,
            HumidityPercent = storageInfo.CurrentHumidity,
            Notes = "Cập nhật vị trí lưu mẫu và thời hạn bảo quản."
        });

        await UpdateAsync(existing, ct);
        return true;
    }

    private static List<SpecimenHistoryRecord> GetStandardSeedRecords()
    {
        var now = DateTime.UtcNow;
        return new List<SpecimenHistoryRecord>
        {
            // MẪU 1: Ống Tím EDTA - Tổng phân tích tế bào máu CBC (Đã hoàn tất xét nghiệm & Lưu tủ mát)
            new()
            {
                Id = "672501010000000000000001",
                Barcode = "SMP-261021-001",
                RfidTag = "RFID-TRAY-01-SLOT-04",
                SampleType = "Blood",
                TubeColor = "Tím (EDTA)",
                Anticoagulant = "EDTA K2 2ml",
                VolumeMl = 2.0,
                MinimumRequiredVolumeMl = 1.0,
                PatientId = "USR-PAT-01",
                PatientName = "Nguyễn Văn An",
                PatientGender = "Nam",
                PatientAge = 32,
                PatientPhone = "0909123456",
                OrderId = "ORD-261021-001",
                AppointmentId = "APP-261021-001",
                OrderDate = now.AddHours(-4),
                DoctorId = "DOC-001",
                DoctorName = "BS. Trần Văn Minh",
                Department = "Khoa Khám Bệnh - Phòng 101",
                TestPackageName = "Tổng Phân Tích Tế Bào Máu Ngoại Vi (CBC 24 Thông Số)",
                TestCriteriaCodes = ["HGB", "PLT", "WBC", "RBC"],
                Status = "Stored",
                Priority = "Normal",
                CollectionTime = now.AddHours(-3),
                CollectorId = "NURSE-01",
                CollectorName = "ĐD. Lê Thị Hồng",
                CollectionSite = "Bàn Lấy Mẫu 02 - Khoa Xét Nghiệm",
                Notes = "Mẫu đạt chuẩn chất lượng theo ISO 15189, không đông dây, không huyết tán.",
                IsActive = true,
                CreatedAt = now.AddHours(-4),
                UpdatedAt = now.AddMinutes(-30),
                Storage = new SpecimenStorageInfo
                {
                    StorageStatus = "Stored",
                    StorageUnit = "Tủ Mát Bảo Quản Mẫu Lab #02 (2°C - 8°C)",
                    RackId = "RACK-HEM-01",
                    SlotPosition = "Khay A - Ô 04",
                    TargetTemperatureMin = 2.0,
                    TargetTemperatureMax = 8.0,
                    CurrentTemperature = 4.2,
                    CurrentHumidity = 45.0,
                    IsTemperatureCompliant = true,
                    PreservationMethod = "Máu toàn phần EDTA bảo quản lạnh 2-8°C",
                    StoredAt = now.AddHours(-1),
                    StoredBy = "KTV. Phạm Quốc Hùng",
                    RetentionPeriodDays = 7,
                    ExpirationDate = now.AddDays(7),
                    DisposalDueDate = now.AddDays(7),
                    DisposalStatus = "Retained"
                },
                Timeline =
                [
                    new()
                    {
                        EventId = "EVT-001-1",
                        EventType = "OrderCreated",
                        Title = "Tạo Chỉ Định Xét Nghiệm",
                        Description = "Bác sĩ chỉ định gói Tổng phân tích tế bào máu ngoại vi.",
                        Timestamp = now.AddHours(-4),
                        OperatorId = "DOC-001",
                        OperatorName = "BS. Trần Văn Minh",
                        OperatorRole = "Doctor",
                        Location = "Phòng Khám Nội 101",
                        Status = "PendingCollection"
                    },
                    new()
                    {
                        EventId = "EVT-001-2",
                        EventType = "PhlebotomyCollection",
                        Title = "Thu Thập Mẫu Bệnh Phẩm",
                        Description = "Lấy 2.0ml máu tĩnh mạch vào ống nắp tím chống đông EDTA K2 vô trùng.",
                        Timestamp = now.AddHours(-3),
                        OperatorId = "NURSE-01",
                        OperatorName = "ĐD. Lê Thị Hồng",
                        OperatorRole = "Nurse",
                        Location = "Bàn Lấy Mẫu 02",
                        Status = "Collected",
                        TemperatureC = 24.5
                    },
                    new()
                    {
                        EventId = "EVT-001-3",
                        EventType = "Barcoded",
                        Title = "In & Dán Tem Mã Vạch Barcode 128",
                        Description = "Dán tem mã vạch SMP-261021-001 và đặt vào khay có gắn thẻ RFID.",
                        Timestamp = now.AddHours(-3).AddMinutes(5),
                        OperatorId = "NURSE-01",
                        OperatorName = "ĐD. Lê Thị Hồng",
                        OperatorRole = "Nurse",
                        Location = "Bàn Lấy Mẫu 02",
                        Status = "Collected"
                    },
                    new()
                    {
                        EventId = "EVT-001-4",
                        EventType = "InTransit",
                        Title = "Bàn Giao & Vận Chuyển Mẫu",
                        Description = "Khay mẫu được vận chuyển bằng hộp cách nhiệt chuyên dụng sang phòng phân tích.",
                        Timestamp = now.AddHours(-2).AddMinutes(-40),
                        OperatorId = "TRANS-01",
                        OperatorName = "NV. Hoàng Văn Tuấn",
                        OperatorRole = "Courier",
                        Location = "Hành Lang Chuyển Mẫu Khối Xét Nghiệm",
                        Status = "InTransit",
                        TemperatureC = 5.0
                    },
                    new()
                    {
                        EventId = "EVT-001-5",
                        EventType = "LabReceived",
                        Title = "Tiếp Nhận Mẫu Tại Phòng Lab",
                        Description = "Quét mã vạch tiếp nhận mẫu. Kiểm tra thể tích đủ 2.0ml, không phát hiện huyết tán.",
                        Timestamp = now.AddHours(-2).AddMinutes(-20),
                        OperatorId = "TECH-01",
                        OperatorName = "KTV. Phạm Quốc Hùng",
                        OperatorRole = "LabTech",
                        Location = "Bàn Tiếp Nhận Phòng Xét Nghiệm",
                        Status = "Received"
                    },
                    new()
                    {
                        EventId = "EVT-001-6",
                        EventType = "MachineAnalysis",
                        Title = "Phân Tích Máy Huyết Học Tự Động",
                        Description = "Chạy mẫu trên máy phân tích huyết học Sysmex XN-1000. Dữ liệu chỉ số truyền tự động.",
                        Timestamp = now.AddHours(-2),
                        OperatorId = "TECH-01",
                        OperatorName = "KTV. Phạm Quốc Hùng",
                        OperatorRole = "LabTech",
                        Location = "Phòng Phân Tích Huyết Học",
                        Status = "Completed"
                    },
                    new()
                    {
                        EventId = "EVT-001-7",
                        EventType = "StorageArchived",
                        Title = "Lưu Trữ Mẫu Vào Tủ Mát Bảo Quản",
                        Description = "Chuyển ống máu vào Tủ Mát Lab #02, vị trí RACK-HEM-01 Ô A-04 theo quy định lưu mẫu 7 ngày.",
                        Timestamp = now.AddHours(-1),
                        OperatorId = "TECH-01",
                        OperatorName = "KTV. Phạm Quốc Hùng",
                        OperatorRole = "LabTech",
                        Location = "Tủ Mát Bảo Quản Mẫu Lab #02",
                        Status = "Stored",
                        TemperatureC = 4.2,
                        HumidityPercent = 45.0
                    }
                ]
            },

            // MẪU 2: Ống Vàng Serum Gel Clot - Sinh Hóa Máu (Đang phân tích máy)
            new()
            {
                Id = "672501010000000000000002",
                Barcode = "SMP-261021-002",
                RfidTag = "RFID-TRAY-01-SLOT-05",
                SampleType = "Serum",
                TubeColor = "Vàng (Serum Gel Clot)",
                Anticoagulant = "Clot Activator & Gel",
                VolumeMl = 4.0,
                MinimumRequiredVolumeMl = 2.0,
                PatientId = "USR-PAT-02",
                PatientName = "Trần Thị Hoa",
                PatientGender = "Nữ",
                PatientAge = 45,
                PatientPhone = "0912345678",
                OrderId = "ORD-261021-002",
                AppointmentId = "APP-261021-002",
                OrderDate = now.AddHours(-2),
                DoctorId = "DOC-002",
                DoctorName = "BS. Hoàng Ngọc Dung",
                Department = "Khoa Nội Tiết - Tiểu Đường",
                TestPackageName = "Kiểm Tra Đường Huyết & Chức Năng Gan Thận Toàn Diện",
                TestCriteriaCodes = ["GLU", "CRE", "ALT", "AST", "CHOL", "TRIG", "UREA"],
                Status = "Processing",
                Priority = "Normal",
                CollectionTime = now.AddHours(-1).AddMinutes(-40),
                CollectorId = "NURSE-02",
                CollectorName = "ĐD. Vũ Bích Thảo",
                CollectionSite = "Bàn Lấy Mẫu 01 - Khoa Khám",
                Notes = "Bệnh nhân nhịn ăn trên 8 tiếng. Mẫu đã ly tâm tách huyết thanh trong suốt.",
                IsActive = true,
                CreatedAt = now.AddHours(-2),
                UpdatedAt = now.AddMinutes(-10),
                Storage = new SpecimenStorageInfo
                {
                    StorageStatus = "InAnalysis",
                    StorageUnit = "Bàn Máy Sinh Hóa Tự Động Beckman AU480",
                    RackId = "TRAY-CHM-02",
                    SlotPosition = "Vị trí Cup 14",
                    TargetTemperatureMin = 2.0,
                    TargetTemperatureMax = 8.0,
                    CurrentTemperature = 4.0,
                    CurrentHumidity = 48.0,
                    IsTemperatureCompliant = true,
                    PreservationMethod = "Huyết thanh tách chiết sau ly tâm 3500 vòng/phút",
                    StoredAt = now.AddMinutes(-30),
                    StoredBy = "KTV. Đỗ Thành Đạt",
                    RetentionPeriodDays = 7,
                    ExpirationDate = now.AddDays(7),
                    DisposalDueDate = now.AddDays(7),
                    DisposalStatus = "Retained"
                },
                Timeline =
                [
                    new()
                    {
                        EventId = "EVT-002-1",
                        EventType = "OrderCreated",
                        Title = "Tạo Chỉ Định Xét Nghiệm Sinh Hóa",
                        Description = "Bác sĩ Nội tiết chỉ định xét nghiệm đường huyết và men gan thận.",
                        Timestamp = now.AddHours(-2),
                        OperatorId = "DOC-002",
                        OperatorName = "BS. Hoàng Ngọc Dung",
                        OperatorRole = "Doctor",
                        Location = "Phòng Khám Nội Tiết 204",
                        Status = "PendingCollection"
                    },
                    new()
                    {
                        EventId = "EVT-002-2",
                        EventType = "PhlebotomyCollection",
                        Title = "Lấy Máu Ống Nắp Vàng Serum Gel",
                        Description = "Thu 4.0ml máu vào ống Serum có hạt hoạt hóa đông máu và gel tách huyết thanh.",
                        Timestamp = now.AddHours(-1).AddMinutes(-40),
                        OperatorId = "NURSE-02",
                        OperatorName = "ĐD. Vũ Bích Thảo",
                        OperatorRole = "Nurse",
                        Location = "Bàn Lấy Mẫu 01",
                        Status = "Collected"
                    },
                    new()
                    {
                        EventId = "EVT-002-3",
                        EventType = "LabReceived",
                        Title = "Tiếp Nhận & Đưa Vào Máy Ly Tâm",
                        Description = "Tiếp nhận mẫu tại phòng Lab. Đưa vào máy ly tâm lạnh 3500 vòng/phút trong 10 phút.",
                        Timestamp = now.AddHours(-1).AddMinutes(-10),
                        OperatorId = "TECH-02",
                        OperatorName = "KTV. Đỗ Thành Đạt",
                        OperatorRole = "LabTech",
                        Location = "Khu Vực Ly Tâm Mẫu",
                        Status = "Processing",
                        TemperatureC = 4.0
                    },
                    new()
                    {
                        EventId = "EVT-002-4",
                        EventType = "MachineAnalysis",
                        Title = "Nạp Mẫu Lên Máy Phân Tích Sinh Hóa",
                        Description = "Huyết thanh được nạp vào khay mẫu máy Beckman Coulter AU480.",
                        Timestamp = now.AddMinutes(-20),
                        OperatorId = "TECH-02",
                        OperatorName = "KTV. Đỗ Thành Đạt",
                        OperatorRole = "LabTech",
                        Location = "Máy Sinh Hóa Tự Động AU480",
                        Status = "Processing"
                    }
                ]
            },

            // MẪU 3: Ống Xanh Dương Sodium Citrate - Đông Máu Cấp Cứu STAT (Cấp cứu 1 giờ)
            new()
            {
                Id = "672501010000000000000003",
                Barcode = "SMP-261021-003",
                RfidTag = "RFID-STAT-03-SLOT-01",
                SampleType = "Plasma",
                TubeColor = "Xanh dương (Sodium Citrate)",
                Anticoagulant = "Sodium Citrate 3.2% (1:9)",
                VolumeMl = 2.7,
                MinimumRequiredVolumeMl = 2.5,
                PatientId = "USR-PAT-03",
                PatientName = "Lê Hoàng Nam",
                PatientGender = "Nam",
                PatientAge = 58,
                PatientPhone = "0987654321",
                OrderId = "ORD-261021-003",
                AppointmentId = "APP-261021-003",
                OrderDate = now.AddMinutes(-50),
                DoctorId = "DOC-003",
                DoctorName = "BS. Nguyễn Văn Hùng",
                Department = "Khoa Cấp Cứu & Hồi Sức Tích Cực",
                TestPackageName = "Đông Máu Toàn Bộ (PT, aPTT, Fibrinogen - Cấp Cứu STAT)",
                TestCriteriaCodes = ["PT", "APTT", "FIB"],
                Status = "Completed",
                Priority = "Stat",
                CollectionTime = now.AddMinutes(-40),
                CollectorId = "NURSE-03",
                CollectorName = "ĐD. Nguyễn Thị Thúy",
                CollectionSite = "Giường Cấp Cứu #04",
                Notes = "Mẫu cấp cứu STAT đạt chuẩn thời gian TAT SLA dưới 1 giờ (BR-LAB-01). Tỷ lệ máu/chất chống đông chuẩn 9:1.",
                IsActive = true,
                CreatedAt = now.AddMinutes(-50),
                UpdatedAt = now.AddMinutes(-5),
                Storage = new SpecimenStorageInfo
                {
                    StorageStatus = "Stored",
                    StorageUnit = "Tủ Âm Sâu Bảo Quản Huyết Tương Lab #01 (-20°C)",
                    RackId = "RACK-FRZ-01",
                    SlotPosition = "Khay C - Ô 08",
                    TargetTemperatureMin = -25.0,
                    TargetTemperatureMax = -18.0,
                    CurrentTemperature = -21.4,
                    CurrentHumidity = 30.0,
                    IsTemperatureCompliant = true,
                    PreservationMethod = "Huyết tương nghèo tiểu cầu đông lạnh sâu -20°C",
                    StoredAt = now.AddMinutes(-10),
                    StoredBy = "KTV. Đỗ Thành Đạt",
                    RetentionPeriodDays = 14,
                    ExpirationDate = now.AddDays(14),
                    DisposalDueDate = now.AddDays(14),
                    DisposalStatus = "Retained"
                },
                Timeline =
                [
                    new()
                    {
                        EventId = "EVT-003-1",
                        EventType = "OrderCreated",
                        Title = "Y Lệnh Khẩn STAT Đông Máu",
                        Description = "Bác sĩ Cấp cứu ra y lệnh khẩn trước phẫu thuật.",
                        Timestamp = now.AddMinutes(-50),
                        OperatorId = "DOC-003",
                        OperatorName = "BS. Nguyễn Văn Hùng",
                        OperatorRole = "Doctor",
                        Location = "Khoa Cấp Cứu",
                        Status = "PendingCollection"
                    },
                    new()
                    {
                        EventId = "EVT-003-2",
                        EventType = "PhlebotomyCollection",
                        Title = "Lấy Máu Cấp Cứu Đúng Tỷ Lệ 9:1",
                        Description = "Lấy chính xác 2.7ml máu vào ống Sodium Citrate 3.2% nắp xanh dương.",
                        Timestamp = now.AddMinutes(-40),
                        OperatorId = "NURSE-03",
                        OperatorName = "ĐD. Nguyễn Thị Thúy",
                        OperatorRole = "Nurse",
                        Location = "Giường Cấp Cứu #04",
                        Status = "Collected"
                    },
                    new()
                    {
                        EventId = "EVT-003-3",
                        EventType = "InTransit",
                        Title = "Chuyển Khẩn Cấp Bằng Hộp STAT",
                        Description = "Chuyển phát nhanh trực tiếp bằng tay lên phòng Lab trong 5 phút.",
                        Timestamp = now.AddMinutes(-35),
                        OperatorId = "NURSE-03",
                        OperatorName = "ĐD. Nguyễn Thị Thúy",
                        OperatorRole = "Nurse",
                        Location = "Hành Lang Cấp Cứu",
                        Status = "InTransit"
                    },
                    new()
                    {
                        EventId = "EVT-003-4",
                        EventType = "MachineAnalysis",
                        Title = "Phân Tích Máy Đông Máu Stago",
                        Description = "Ly tâm nhanh và chạy trên máy đông máu tự động Stago Compact Max.",
                        Timestamp = now.AddMinutes(-20),
                        OperatorId = "TECH-02",
                        OperatorName = "KTV. Đỗ Thành Đạt",
                        OperatorRole = "LabTech",
                        Location = "Phòng Máy Đông Máu",
                        Status = "Completed"
                    },
                    new()
                    {
                        EventId = "EVT-003-5",
                        EventType = "StorageArchived",
                        Title = "Lưu Huyết Tương Vào Tủ Âm Sâu -20°C",
                        Description = "Tách huyết tương lưu trữ tại Tủ Âm Sâu #01 theo quy định lưu mẫu đông máu 14 ngày.",
                        Timestamp = now.AddMinutes(-10),
                        OperatorId = "TECH-02",
                        OperatorName = "KTV. Đỗ Thành Đạt",
                        OperatorRole = "LabTech",
                        Location = "Tủ Âm Sâu Lab #01",
                        Status = "Stored",
                        TemperatureC = -21.4,
                        HumidityPercent = 30.0
                    }
                ]
            }
        };
    }
}
