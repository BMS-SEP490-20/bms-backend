using System.Collections.Concurrent;
using BMS.Core.Entities;
using BMS.Core.Interfaces;
using BMS.Infrastructure.Data;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace BMS.Infrastructure.Repositories;

/// <summary>
/// Repository implementing biological reference intervals storage (UC57).
/// Supports both MongoDB and an in-memory thread-safe fallback with standard clinical seed data.
/// </summary>
public sealed class ReferenceIntervalRepository : IReferenceIntervalRepository
{
    private readonly MongoDbContext _context;
    private readonly ILogger<ReferenceIntervalRepository> _logger;

    private static readonly ConcurrentDictionary<string, ReferenceIntervalEntity> _memoryIntervals = new();
    private static bool _seeded;
    private static readonly object _seedLock = new();

    public ReferenceIntervalRepository(MongoDbContext context, ILogger<ReferenceIntervalRepository> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task EnsureDefaultIntervalsSeededAsync(CancellationToken ct = default)
    {
        if (_seeded) return;

        lock (_seedLock)
        {
            if (_seeded) return;
            _seeded = true;
        }

        var defaultIntervals = GetStandardSeedIntervals();

        foreach (var interval in defaultIntervals)
        {
            _memoryIntervals.TryAdd(interval.Id, interval);
        }

        if (_context.ReferenceIntervals != null)
        {
            try
            {
                var count = await _context.ReferenceIntervals.CountDocumentsAsync(
                    Builders<ReferenceIntervalEntity>.Filter.Eq(x => x.IsDeleted, false),
                    cancellationToken: ct);

                if (count < 10)
                {
                    foreach (var interval in defaultIntervals)
                    {
                        var existsInDb = await _context.ReferenceIntervals.Find(
                            Builders<ReferenceIntervalEntity>.Filter.Eq(x => x.CriterionCode, interval.CriterionCode) &
                            Builders<ReferenceIntervalEntity>.Filter.Eq(x => x.Gender, interval.Gender) &
                            Builders<ReferenceIntervalEntity>.Filter.Eq(x => x.AgeMin, interval.AgeMin) &
                            Builders<ReferenceIntervalEntity>.Filter.Eq(x => x.AgeMax, interval.AgeMax)
                        ).AnyAsync(ct);

                        if (!existsInDb)
                        {
                            var clone = new ReferenceIntervalEntity
                            {
                                Id = MongoDB.Bson.ObjectId.GenerateNewId().ToString(),
                                CriterionId = interval.CriterionId,
                                CriterionCode = interval.CriterionCode,
                                CriterionName = interval.CriterionName,
                                Unit = interval.Unit,
                                Gender = interval.Gender,
                                AgeMin = interval.AgeMin,
                                AgeMax = interval.AgeMax,
                                AgeUnit = interval.AgeUnit,
                                DemographicLabel = interval.DemographicLabel,
                                NormalMin = interval.NormalMin,
                                NormalMax = interval.NormalMax,
                                NormalText = interval.NormalText,
                                PanicLow = interval.PanicLow,
                                PanicHigh = interval.PanicHigh,
                                Condition = interval.Condition,
                                StandardAuthority = interval.StandardAuthority,
                                Notes = interval.Notes,
                                IsActive = interval.IsActive,
                                CreatedAt = DateTime.UtcNow,
                                UpdatedAt = DateTime.UtcNow
                            };
                            await _context.ReferenceIntervals.InsertOneAsync(clone, cancellationToken: ct);
                        }
                    }
                    _logger.LogInformation("[ReferenceIntervalRepository] Seeded default clinical reference intervals into MongoDB.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[ReferenceIntervalRepository] Could not seed reference intervals to MongoDB. Running on in-memory store.");
            }
        }
    }

    public async Task<(List<ReferenceIntervalEntity> Items, long TotalCount)> GetPagedAsync(
        int page,
        int pageSize,
        string? criterionCode = null,
        string? gender = null,
        bool? isActive = null,
        CancellationToken ct = default)
    {
        await EnsureDefaultIntervalsSeededAsync(ct);

        if (_context.ReferenceIntervals != null)
        {
            try
            {
                var filterBuilder = Builders<ReferenceIntervalEntity>.Filter;
                var filter = filterBuilder.Eq(x => x.IsDeleted, false);

                if (!string.IsNullOrWhiteSpace(criterionCode))
                {
                    filter &= filterBuilder.Regex(x => x.CriterionCode, new MongoDB.Bson.BsonRegularExpression(criterionCode.Trim(), "i"));
                }

                if (!string.IsNullOrWhiteSpace(gender) && !gender.Equals("ALL", StringComparison.OrdinalIgnoreCase))
                {
                    filter &= filterBuilder.Eq(x => x.Gender, gender.Trim());
                }

                if (isActive.HasValue)
                {
                    filter &= filterBuilder.Eq(x => x.IsActive, isActive.Value);
                }

                var total = await _context.ReferenceIntervals.CountDocumentsAsync(filter, cancellationToken: ct);
                var items = await _context.ReferenceIntervals
                    .Find(filter)
                    .SortBy(x => x.CriterionCode)
                    .ThenBy(x => x.Gender)
                    .ThenBy(x => x.AgeMin)
                    .Skip((page - 1) * pageSize)
                    .Limit(pageSize)
                    .ToListAsync(ct);

                return (items, total);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[ReferenceIntervalRepository] MongoDB query failed. Fallback to in-memory.");
            }
        }

        var query = _memoryIntervals.Values.Where(x => !x.IsDeleted);

        if (!string.IsNullOrWhiteSpace(criterionCode))
        {
            query = query.Where(x => x.CriterionCode.Contains(criterionCode.Trim(), StringComparison.OrdinalIgnoreCase)
                                  || x.CriterionName.Contains(criterionCode.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(gender) && !gender.Equals("ALL", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(x => x.Gender.Equals(gender.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        if (isActive.HasValue)
        {
            query = query.Where(x => x.IsActive == isActive.Value);
        }

        var totalCount = query.LongCount();
        var pagedItems = query
            .OrderBy(x => x.CriterionCode)
            .ThenBy(x => x.Gender)
            .ThenBy(x => x.AgeMin)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        return (pagedItems, totalCount);
    }

    public async Task<List<ReferenceIntervalEntity>> GetByCriterionCodeAsync(
        string criterionCode,
        bool activeOnly = true,
        CancellationToken ct = default)
    {
        await EnsureDefaultIntervalsSeededAsync(ct);
        var normalizedCode = criterionCode.Trim().ToUpperInvariant();

        if (_context.ReferenceIntervals != null)
        {
            try
            {
                var filterBuilder = Builders<ReferenceIntervalEntity>.Filter;
                var filter = filterBuilder.Eq(x => x.CriterionCode, normalizedCode) &
                             filterBuilder.Eq(x => x.IsDeleted, false);

                if (activeOnly)
                {
                    filter &= filterBuilder.Eq(x => x.IsActive, true);
                }

                return await _context.ReferenceIntervals
                    .Find(filter)
                    .SortBy(x => x.Gender)
                    .ThenBy(x => x.AgeMin)
                    .ToListAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[ReferenceIntervalRepository] MongoDB query failed. Fallback to in-memory.");
            }
        }

        var results = _memoryIntervals.Values
            .Where(x => !x.IsDeleted && x.CriterionCode.Equals(normalizedCode, StringComparison.OrdinalIgnoreCase));

        if (activeOnly)
        {
            results = results.Where(x => x.IsActive);
        }

        return results
            .OrderBy(x => x.Gender)
            .ThenBy(x => x.AgeMin)
            .ToList();
    }

    public async Task<List<ReferenceIntervalEntity>> GetByCriterionIdAsync(
        string criterionId,
        bool activeOnly = true,
        CancellationToken ct = default)
    {
        await EnsureDefaultIntervalsSeededAsync(ct);

        if (_context.ReferenceIntervals != null)
        {
            try
            {
                var filterBuilder = Builders<ReferenceIntervalEntity>.Filter;
                var filter = filterBuilder.Eq(x => x.CriterionId, criterionId) &
                             filterBuilder.Eq(x => x.IsDeleted, false);

                if (activeOnly)
                {
                    filter &= filterBuilder.Eq(x => x.IsActive, true);
                }

                return await _context.ReferenceIntervals
                    .Find(filter)
                    .SortBy(x => x.Gender)
                    .ThenBy(x => x.AgeMin)
                    .ToListAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[ReferenceIntervalRepository] MongoDB query failed. Fallback to in-memory.");
            }
        }

        var results = _memoryIntervals.Values
            .Where(x => !x.IsDeleted && x.CriterionId == criterionId);

        if (activeOnly)
        {
            results = results.Where(x => x.IsActive);
        }

        return results
            .OrderBy(x => x.Gender)
            .ThenBy(x => x.AgeMin)
            .ToList();
    }

    public async Task<ReferenceIntervalEntity?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        await EnsureDefaultIntervalsSeededAsync(ct);

        if (_context.ReferenceIntervals != null)
        {
            try
            {
                var filter = Builders<ReferenceIntervalEntity>.Filter.Eq(x => x.Id, id) &
                             Builders<ReferenceIntervalEntity>.Filter.Eq(x => x.IsDeleted, false);

                return await _context.ReferenceIntervals.Find(filter).FirstOrDefaultAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[ReferenceIntervalRepository] MongoDB query failed. Fallback to in-memory.");
            }
        }

        return _memoryIntervals.TryGetValue(id, out var entity) && !entity.IsDeleted ? entity : null;
    }

    public async Task<ReferenceIntervalEntity> CreateAsync(ReferenceIntervalEntity entity, CancellationToken ct = default)
    {
        await EnsureDefaultIntervalsSeededAsync(ct);

        if (string.IsNullOrWhiteSpace(entity.Id))
        {
            entity.Id = MongoDB.Bson.ObjectId.GenerateNewId().ToString();
        }

        entity.CreatedAt = DateTime.UtcNow;
        entity.UpdatedAt = DateTime.UtcNow;

        if (_context.ReferenceIntervals != null)
        {
            try
            {
                await _context.ReferenceIntervals.InsertOneAsync(entity, cancellationToken: ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[ReferenceIntervalRepository] MongoDB insert failed. Saving to in-memory.");
            }
        }

        _memoryIntervals[entity.Id] = entity;
        return entity;
    }

    public async Task<ReferenceIntervalEntity?> UpdateAsync(ReferenceIntervalEntity entity, CancellationToken ct = default)
    {
        await EnsureDefaultIntervalsSeededAsync(ct);

        entity.UpdatedAt = DateTime.UtcNow;

        if (_context.ReferenceIntervals != null)
        {
            try
            {
                var filter = Builders<ReferenceIntervalEntity>.Filter.Eq(x => x.Id, entity.Id);
                await _context.ReferenceIntervals.ReplaceOneAsync(filter, entity, cancellationToken: ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[ReferenceIntervalRepository] MongoDB replace failed. Fallback to in-memory.");
            }
        }

        _memoryIntervals[entity.Id] = entity;
        return entity;
    }

    public async Task<bool> DeactivateAsync(string id, CancellationToken ct = default)
    {
        await EnsureDefaultIntervalsSeededAsync(ct);

        if (_context.ReferenceIntervals != null)
        {
            try
            {
                var filter = Builders<ReferenceIntervalEntity>.Filter.Eq(x => x.Id, id);
                var update = Builders<ReferenceIntervalEntity>.Update
                    .Set(x => x.IsActive, false)
                    .Set(x => x.UpdatedAt, DateTime.UtcNow);

                var res = await _context.ReferenceIntervals.UpdateOneAsync(filter, update, cancellationToken: ct);
                if (res.ModifiedCount > 0)
                {
                    if (_memoryIntervals.TryGetValue(id, out var memEntity))
                    {
                        memEntity.IsActive = false;
                        memEntity.UpdatedAt = DateTime.UtcNow;
                    }
                    return true;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[ReferenceIntervalRepository] MongoDB deactivate failed. Fallback to in-memory.");
            }
        }

        if (_memoryIntervals.TryGetValue(id, out var entity))
        {
            entity.IsActive = false;
            entity.UpdatedAt = DateTime.UtcNow;
            return true;
        }

        return false;
    }

    private static List<ReferenceIntervalEntity> GetStandardSeedIntervals()
    {
        var now = DateTime.UtcNow;
        return new List<ReferenceIntervalEntity>
        {
            new()
            {
                Id = "672401010000000000000001",
                CriterionId = "CRT-GLU",
                CriterionCode = "GLU",
                CriterionName = "Glucose máu (Đường huyết lúc đói)",
                Unit = "mmol/L",
                Gender = "All",
                AgeMin = 0,
                AgeMax = 120,
                AgeUnit = "Years",
                DemographicLabel = "Tiêu chuẩn chung (Người trưởng thành)",
                NormalMin = 3.9,
                NormalMax = 6.4,
                PanicLow = 2.2,
                PanicHigh = 25.0,
                Condition = "Fasting ≥ 8h",
                StandardAuthority = "Quyết định 2429/QĐ-BYT & ADA 2024",
                Notes = "Đo lúc nhịn ăn ít nhất 8 tiếng. Ngưỡng Panic < 2.2 hoặc > 25.0 mmol/L kích hoạt báo động đỏ.",
                IsActive = true,
                CreatedAt = now,
                UpdatedAt = now
            },

            new()
            {
                Id = "672401010000000000000003",
                CriterionId = "CRT-HGB",
                CriterionCode = "HGB",
                CriterionName = "Huyết sắc tố (Hemoglobin)",
                Unit = "g/L",
                Gender = "All",
                AgeMin = 0,
                AgeMax = 120,
                AgeUnit = "Years",
                DemographicLabel = "Tiêu chuẩn chung (Người trưởng thành)",
                NormalMin = 120.0,
                NormalMax = 165.0,
                PanicLow = 70.0,
                PanicHigh = 200.0,
                Condition = "General",
                StandardAuthority = "WHO & Quyết định 2429/QĐ-BYT",
                Notes = "Tiêu chuẩn huyết sắc tố chung. Panic Low < 70 g/L (thiếu máu cấp đe dọa sinh mạng).",
                IsActive = true,
                CreatedAt = now,
                UpdatedAt = now
            },

            new()
            {
                Id = "672401010000000000000007",
                CriterionId = "CRT-CRE",
                CriterionCode = "CRE",
                CriterionName = "Creatinine huyết thanh",
                Unit = "µmol/L",
                Gender = "All",
                AgeMin = 0,
                AgeMax = 120,
                AgeUnit = "Years",
                DemographicLabel = "Tiêu chuẩn chung (Người trưởng thành)",
                NormalMin = 53.0,
                NormalMax = 106.0,
                PanicLow = null,
                PanicHigh = 500.0,
                Condition = "General",
                StandardAuthority = "KDIGO & Quyết định 2429/QĐ-BYT",
                Notes = "Đánh giá mức lọc cầu thận. Panic High > 500 µmol/L suy thận cấp.",
                IsActive = true,
                CreatedAt = now,
                UpdatedAt = now
            },

            new()
            {
                Id = "672401010000000000000009",
                CriterionId = "CRT-PLT",
                CriterionCode = "PLT",
                CriterionName = "Số lượng tiểu cầu",
                Unit = "G/L",
                Gender = "All",
                AgeMin = 0,
                AgeMax = 120,
                AgeUnit = "Years",
                DemographicLabel = "Tiêu chuẩn chung (Người trưởng thành)",
                NormalMin = 150.0,
                NormalMax = 450.0,
                PanicLow = 20.0,
                PanicHigh = 1000.0,
                Condition = "General",
                StandardAuthority = "CLSI & Quyết định 2429/QĐ-BYT",
                Notes = "Nguy cơ xuất huyết tự phát nghiêm trọng khi PLT < 20 G/L (BR-LAB-03).",
                IsActive = true,
                CreatedAt = now,
                UpdatedAt = now
            },

            new()
            {
                Id = "672401010000000000000010",
                CriterionId = "CRT-ALT",
                CriterionCode = "ALT",
                CriterionName = "Alanine Aminotransferase (Men gan GPT)",
                Unit = "U/L",
                Gender = "All",
                AgeMin = 0,
                AgeMax = 120,
                AgeUnit = "Years",
                DemographicLabel = "Tiêu chuẩn chung (Người trưởng thành)",
                NormalMin = 0.0,
                NormalMax = 40.0,
                PanicLow = null,
                PanicHigh = 500.0,
                Condition = "General",
                StandardAuthority = "AASLD & Quyết định 2429/QĐ-BYT",
                Notes = "Đánh giá tổn thương tế bào gan.",
                IsActive = true,
                CreatedAt = now,
                UpdatedAt = now
            },

            new()
            {
                Id = "672401010000000000000012",
                CriterionId = "CRT-AST",
                CriterionCode = "AST",
                CriterionName = "Aspartate Aminotransferase (Men gan GOT)",
                Unit = "U/L",
                Gender = "All",
                AgeMin = 0,
                AgeMax = 120,
                AgeUnit = "Years",
                DemographicLabel = "Tiêu chuẩn chung (Người trưởng thành)",
                NormalMin = 0.0,
                NormalMax = 40.0,
                PanicLow = null,
                PanicHigh = 500.0,
                Condition = "General",
                StandardAuthority = "AASLD & Quyết định 2429/QĐ-BYT",
                Notes = "Men gan AST tiêu chuẩn chung.",
                IsActive = true,
                CreatedAt = now,
                UpdatedAt = now
            },

            new()
            {
                Id = "672401010000000000000014",
                CriterionId = "CRT-K",
                CriterionCode = "K",
                CriterionName = "Kali máu (Potassium K+)",
                Unit = "mmol/L",
                Gender = "All",
                AgeMin = 0,
                AgeMax = 120,
                AgeUnit = "Years",
                DemographicLabel = "Tiêu chuẩn chung (Người trưởng thành)",
                NormalMin = 3.5,
                NormalMax = 5.0,
                PanicLow = 2.5,
                PanicHigh = 6.5,
                Condition = "General",
                StandardAuthority = "CLSI & Quyết định 2429/QĐ-BYT (BR-LAB-03)",
                Notes = "Điện giải đồ K+. Panic < 2.5 hoặc > 6.5 mmol/L gây loạn nhịp tim ngừng tim cấp.",
                IsActive = true,
                CreatedAt = now,
                UpdatedAt = now
            },

            new()
            {
                Id = "672401010000000000000015",
                CriterionId = "CRT-NA",
                CriterionCode = "NA",
                CriterionName = "Natri máu (Sodium Na+)",
                Unit = "mmol/L",
                Gender = "All",
                AgeMin = 0,
                AgeMax = 120,
                AgeUnit = "Years",
                DemographicLabel = "Tiêu chuẩn chung (Người trưởng thành)",
                NormalMin = 135.0,
                NormalMax = 145.0,
                PanicLow = 120.0,
                PanicHigh = 160.0,
                Condition = "General",
                StandardAuthority = "CLSI & Quyết định 2429/QĐ-BYT (BR-LAB-03)",
                Notes = "Điện giải đồ Na+. Panic < 120 hoặc > 160 mmol/L gây co giật phù não hôn mê.",
                IsActive = true,
                CreatedAt = now,
                UpdatedAt = now
            },

            new()
            {
                Id = "672401010000000000000016",
                CriterionId = "CRT-CHO",
                CriterionCode = "CHOL",
                CriterionName = "Cholesterol toàn phần",
                Unit = "mmol/L",
                Gender = "All",
                AgeMin = 0,
                AgeMax = 120,
                AgeUnit = "Years",
                DemographicLabel = "Tiêu chuẩn chung (Người trưởng thành)",
                NormalMin = 0.0,
                NormalMax = 5.2,
                PanicLow = null,
                PanicHigh = 10.0,
                Condition = "Fasting",
                StandardAuthority = "NCEP ATP III & Quyết định 2429/QĐ-BYT",
                Notes = "Mức lý tưởng < 5.2 mmol/L (< 200 mg/dL).",
                IsActive = true,
                CreatedAt = now,
                UpdatedAt = now
            },

            new()
            {
                Id = "672401010000000000000017",
                CriterionId = "CRT-TRI",
                CriterionCode = "TRIG",
                CriterionName = "Triglycerides",
                Unit = "mmol/L",
                Gender = "All",
                AgeMin = 0,
                AgeMax = 120,
                AgeUnit = "Years",
                DemographicLabel = "Tiêu chuẩn chung (Người trưởng thành)",
                NormalMin = 0.0,
                NormalMax = 1.7,
                PanicLow = null,
                PanicHigh = 10.0,
                Condition = "Fasting",
                StandardAuthority = "NCEP ATP III & Quyết định 2429/QĐ-BYT",
                Notes = "Mức bình thường < 1.7 mmol/L (< 150 mg/dL). Ngưỡng > 10.0 nguy cơ viêm tụy cấp.",
                IsActive = true,
                CreatedAt = now,
                UpdatedAt = now
            },

            new()
            {
                Id = "672401010000000000000018",
                CriterionId = "CRT-UREA",
                CriterionCode = "UREA",
                CriterionName = "Ure máu (BUN)",
                Unit = "mmol/L",
                Gender = "All",
                AgeMin = 0,
                AgeMax = 120,
                AgeUnit = "Years",
                DemographicLabel = "Tiêu chuẩn chung (Người trưởng thành)",
                NormalMin = 2.8,
                NormalMax = 7.8,
                PanicLow = null,
                PanicHigh = 35.0,
                Condition = "General",
                StandardAuthority = "Quyết định 2429/QĐ-BYT",
                Notes = "Đánh giá chức năng thận.",
                IsActive = true,
                CreatedAt = now,
                UpdatedAt = now
            },

            new()
            {
                Id = "672401010000000000000019",
                CriterionId = "CRT-RBC",
                CriterionCode = "RBC",
                CriterionName = "Hồng cầu (Red Blood Cells)",
                Unit = "T/L",
                Gender = "All",
                AgeMin = 0,
                AgeMax = 120,
                AgeUnit = "Years",
                DemographicLabel = "Tiêu chuẩn chung (Người trưởng thành)",
                NormalMin = 4.0,
                NormalMax = 5.8,
                PanicLow = 2.2,
                PanicHigh = 6.8,
                Condition = "General",
                StandardAuthority = "WHO & Quyết định 2429/QĐ-BYT",
                Notes = "Số lượng hồng cầu tiêu chuẩn chung.",
                IsActive = true,
                CreatedAt = now,
                UpdatedAt = now
            },

            new()
            {
                Id = "672401010000000000000020",
                CriterionId = "CRT-WBC",
                CriterionCode = "WBC",
                CriterionName = "Bạch cầu (White Blood Cells)",
                Unit = "G/L",
                Gender = "All",
                AgeMin = 0,
                AgeMax = 120,
                AgeUnit = "Years",
                DemographicLabel = "Tiêu chuẩn chung (Người trưởng thành)",
                NormalMin = 4.0,
                NormalMax = 10.0,
                PanicLow = 1.5,
                PanicHigh = 25.0,
                Condition = "General",
                StandardAuthority = "CLSI & Quyết định 2429/QĐ-BYT",
                Notes = "Số lượng bạch cầu tiêu chuẩn chung.",
                IsActive = true,
                CreatedAt = now,
                UpdatedAt = now
            }
        };
    }
}
