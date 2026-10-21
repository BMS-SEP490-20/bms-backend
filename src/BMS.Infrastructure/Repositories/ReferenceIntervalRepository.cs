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

        // 1. Seed in-memory store
        foreach (var interval in defaultIntervals)
        {
            _memoryIntervals.TryAdd(interval.Id, interval);
        }

        // 2. Seed MongoDB if collection exists
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

        // In-memory fallback
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
            // --- GLUCOSE ---
            new()
            {
                Id = "672401010000000000000001",
                CriterionCode = "GLU",
                CriterionName = "Glucose máu (Đường huyết lúc đói)",
                Unit = "mmol/L",
                Gender = "All",
                AgeMin = 18,
                AgeMax = 120,
                AgeUnit = "Years",
                DemographicLabel = "Người lớn trưởng thành (≥ 18 tuổi)",
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
                Id = "672401010000000000000002",
                CriterionCode = "GLU",
                CriterionName = "Glucose máu (Đường huyết trẻ em)",
                Unit = "mmol/L",
                Gender = "All",
                AgeMin = 1,
                AgeMax = 17,
                AgeUnit = "Years",
                DemographicLabel = "Trẻ em & Vị thành niên (1 - 17 tuổi)",
                NormalMin = 3.3,
                NormalMax = 5.6,
                PanicLow = 2.2,
                PanicHigh = 22.0,
                Condition = "General",
                StandardAuthority = "Quyết định 2429/QĐ-BYT",
                Notes = "Chỉ số đường huyết nhi khoa.",
                IsActive = true,
                CreatedAt = now,
                UpdatedAt = now
            },

            // --- HEMOGLOBIN (HGB) ---
            new()
            {
                Id = "672401010000000000000003",
                CriterionCode = "HGB",
                CriterionName = "Huyết sắc tố (Hemoglobin)",
                Unit = "g/L",
                Gender = "Male",
                AgeMin = 18,
                AgeMax = 120,
                AgeUnit = "Years",
                DemographicLabel = "Nam trưởng thành (≥ 18 tuổi)",
                NormalMin = 130.0,
                NormalMax = 175.0,
                PanicLow = 70.0,
                PanicHigh = 200.0,
                Condition = "General",
                StandardAuthority = "WHO & Quyết định 2429/QĐ-BYT",
                Notes = "Nam giới trưởng thành. Panic Low < 70 g/L (thiếu máu cấp đe dọa sinh mạng).",
                IsActive = true,
                CreatedAt = now,
                UpdatedAt = now
            },
            new()
            {
                Id = "672401010000000000000004",
                CriterionCode = "HGB",
                CriterionName = "Huyết sắc tố (Hemoglobin)",
                Unit = "g/L",
                Gender = "Female",
                AgeMin = 18,
                AgeMax = 120,
                AgeUnit = "Years",
                DemographicLabel = "Nữ trưởng thành (≥ 18 tuổi)",
                NormalMin = 120.0,
                NormalMax = 155.0,
                PanicLow = 70.0,
                PanicHigh = 190.0,
                Condition = "General",
                StandardAuthority = "WHO & Quyết định 2429/QĐ-BYT",
                Notes = "Nữ giới trưởng thành không mang thai.",
                IsActive = true,
                CreatedAt = now,
                UpdatedAt = now
            },
            new()
            {
                Id = "672401010000000000000005",
                CriterionCode = "HGB",
                CriterionName = "Huyết sắc tố (Hemoglobin)",
                Unit = "g/L",
                Gender = "Female",
                AgeMin = 16,
                AgeMax = 49,
                AgeUnit = "Years",
                DemographicLabel = "Phụ nữ mang thai (Thai kỳ)",
                NormalMin = 110.0,
                NormalMax = 140.0,
                PanicLow = 70.0,
                PanicHigh = 180.0,
                Condition = "Pregnancy",
                StandardAuthority = "WHO Guidelines on Maternal Health",
                Notes = "Áp dụng cho thai phụ; sinh lý máu loãng thai kỳ.",
                IsActive = true,
                CreatedAt = now,
                UpdatedAt = now
            },
            new()
            {
                Id = "672401010000000000000006",
                CriterionCode = "HGB",
                CriterionName = "Huyết sắc tố (Hemoglobin)",
                Unit = "g/L",
                Gender = "All",
                AgeMin = 1,
                AgeMax = 12,
                AgeUnit = "Years",
                DemographicLabel = "Trẻ em (1 - 12 tuổi)",
                NormalMin = 110.0,
                NormalMax = 140.0,
                PanicLow = 70.0,
                PanicHigh = 180.0,
                Condition = "Pediatric",
                StandardAuthority = "Quyết định 2429/QĐ-BYT",
                Notes = "Dải tham chiếu huyết sắc tố cho trẻ em.",
                IsActive = true,
                CreatedAt = now,
                UpdatedAt = now
            },

            // --- CREATININE (CRE) ---
            new()
            {
                Id = "672401010000000000000007",
                CriterionCode = "CRE",
                CriterionName = "Creatinine huyết thanh",
                Unit = "µmol/L",
                Gender = "Male",
                AgeMin = 18,
                AgeMax = 120,
                AgeUnit = "Years",
                DemographicLabel = "Nam trưởng thành (≥ 18 tuổi)",
                NormalMin = 62.0,
                NormalMax = 106.0,
                PanicLow = null,
                PanicHigh = 500.0,
                Condition = "General",
                StandardAuthority = "KDIGO & Quyết định 2429/QĐ-BYT",
                Notes = "Đánh giá mức lọc cầu thận nam. Panic High > 500 µmol/L suy thận cấp.",
                IsActive = true,
                CreatedAt = now,
                UpdatedAt = now
            },
            new()
            {
                Id = "672401010000000000000008",
                CriterionCode = "CRE",
                CriterionName = "Creatinine huyết thanh",
                Unit = "µmol/L",
                Gender = "Female",
                AgeMin = 18,
                AgeMax = 120,
                AgeUnit = "Years",
                DemographicLabel = "Nữ trưởng thành (≥ 18 tuổi)",
                NormalMin = 44.0,
                NormalMax = 80.0,
                PanicLow = null,
                PanicHigh = 500.0,
                Condition = "General",
                StandardAuthority = "KDIGO & Quyết định 2429/QĐ-BYT",
                Notes = "Đánh giá mức lọc cầu thận nữ.",
                IsActive = true,
                CreatedAt = now,
                UpdatedAt = now
            },

            // --- PLATELETS (PLT) ---
            new()
            {
                Id = "672401010000000000000009",
                CriterionCode = "PLT",
                CriterionName = "Số lượng tiểu cầu",
                Unit = "G/L",
                Gender = "All",
                AgeMin = 0,
                AgeMax = 120,
                AgeUnit = "Years",
                DemographicLabel = "Mọi độ tuổi & Giới tính",
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

            // --- ALT (GPT) ---
            new()
            {
                Id = "672401010000000000000010",
                CriterionCode = "ALT",
                CriterionName = "Alanine Aminotransferase (Men gan GPT)",
                Unit = "U/L",
                Gender = "Male",
                AgeMin = 18,
                AgeMax = 120,
                AgeUnit = "Years",
                DemographicLabel = "Nam giới (≥ 18 tuổi)",
                NormalMin = 0.0,
                NormalMax = 50.0,
                PanicLow = null,
                PanicHigh = 500.0,
                Condition = "General",
                StandardAuthority = "AASLD & Quyết định 2429/QĐ-BYT",
                Notes = "Đánh giá tổn thương tế bào gan ở nam giới.",
                IsActive = true,
                CreatedAt = now,
                UpdatedAt = now
            },
            new()
            {
                Id = "672401010000000000000011",
                CriterionCode = "ALT",
                CriterionName = "Alanine Aminotransferase (Men gan GPT)",
                Unit = "U/L",
                Gender = "Female",
                AgeMin = 18,
                AgeMax = 120,
                AgeUnit = "Years",
                DemographicLabel = "Nữ giới (≥ 18 tuổi)",
                NormalMin = 0.0,
                NormalMax = 35.0,
                PanicLow = null,
                PanicHigh = 500.0,
                Condition = "General",
                StandardAuthority = "AASLD & Quyết định 2429/QĐ-BYT",
                Notes = "Đánh giá tổn thương tế bào gan ở nữ giới.",
                IsActive = true,
                CreatedAt = now,
                UpdatedAt = now
            },

            // --- AST (GOT) ---
            new()
            {
                Id = "672401010000000000000012",
                CriterionCode = "AST",
                CriterionName = "Aspartate Aminotransferase (Men gan GOT)",
                Unit = "U/L",
                Gender = "Male",
                AgeMin = 18,
                AgeMax = 120,
                AgeUnit = "Years",
                DemographicLabel = "Nam giới (≥ 18 tuổi)",
                NormalMin = 0.0,
                NormalMax = 40.0,
                PanicLow = null,
                PanicHigh = 500.0,
                Condition = "General",
                StandardAuthority = "AASLD & Quyết định 2429/QĐ-BYT",
                Notes = "Men gan AST cho nam giới.",
                IsActive = true,
                CreatedAt = now,
                UpdatedAt = now
            },
            new()
            {
                Id = "672401010000000000000013",
                CriterionCode = "AST",
                CriterionName = "Aspartate Aminotransferase (Men gan GOT)",
                Unit = "U/L",
                Gender = "Female",
                AgeMin = 18,
                AgeMax = 120,
                AgeUnit = "Years",
                DemographicLabel = "Nữ giới (≥ 18 tuổi)",
                NormalMin = 0.0,
                NormalMax = 32.0,
                PanicLow = null,
                PanicHigh = 500.0,
                Condition = "General",
                StandardAuthority = "AASLD & Quyết định 2429/QĐ-BYT",
                Notes = "Men gan AST cho nữ giới.",
                IsActive = true,
                CreatedAt = now,
                UpdatedAt = now
            },

            // --- POTASSIUM (K+) ---
            new()
            {
                Id = "672401010000000000000014",
                CriterionCode = "K",
                CriterionName = "Kali máu (Potassium K+)",
                Unit = "mmol/L",
                Gender = "All",
                AgeMin = 0,
                AgeMax = 120,
                AgeUnit = "Years",
                DemographicLabel = "Mọi độ tuổi & Giới tính",
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

            // --- SODIUM (NA+) ---
            new()
            {
                Id = "672401010000000000000015",
                CriterionCode = "NA",
                CriterionName = "Natri máu (Sodium Na+)",
                Unit = "mmol/L",
                Gender = "All",
                AgeMin = 0,
                AgeMax = 120,
                AgeUnit = "Years",
                DemographicLabel = "Mọi độ tuổi & Giới tính",
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

            // --- TOTAL CHOLESTEROL ---
            new()
            {
                Id = "672401010000000000000016",
                CriterionCode = "CHOL",
                CriterionName = "Cholesterol toàn phần",
                Unit = "mmol/L",
                Gender = "All",
                AgeMin = 18,
                AgeMax = 120,
                AgeUnit = "Years",
                DemographicLabel = "Người lớn (≥ 18 tuổi)",
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

            // --- TRIGLYCERIDES ---
            new()
            {
                Id = "672401010000000000000017",
                CriterionCode = "TRIG",
                CriterionName = "Triglycerides",
                Unit = "mmol/L",
                Gender = "All",
                AgeMin = 18,
                AgeMax = 120,
                AgeUnit = "Years",
                DemographicLabel = "Người lớn (≥ 18 tuổi)",
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
            }
        };
    }
}
