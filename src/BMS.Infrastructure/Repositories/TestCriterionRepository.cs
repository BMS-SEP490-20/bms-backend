
using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using BMS.Core.Entities;
using MongoDB.Bson;
using BMS.Core.Interfaces;
using BMS.Infrastructure.Data;
using BMS.Infrastructure.Mongo;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

using ApplicationTestCriterionRepository =
    BMS.Application.Interfaces.ITestCriterionRepository;

using CoreTestCriterionRepository =
    BMS.Core.Interfaces.ITestCriterionRepository;

namespace BMS.Infrastructure.Repositories;

/// </summary>>
/// Repository quản lý tiêu chí xét nghiệm.
/// Hỗ trợ cả API quản lý tiêu chí xét nghiệm và API quản lý gói dịch vụ.
/// </summary>
public sealed class TestCriterionRepository :
    CoreTestCriterionRepository,
    ApplicationTestCriterionRepository
{
    private readonly MongoDbContext _context;
    private readonly ILogger<TestCriterionRepository> _logger;

    // Collection tương thích với entity của nhánh ManageServicePackages.
    private readonly IMongoCollection<TestCriterion> _legacyCriteria;

    private static readonly ConcurrentDictionary<string, TestCriterionEntity>
        _memoryCriteria = new();

    private static bool _seeded;

    public TestCriterionRepository(
        MongoDbContext context,
        ILogger<TestCriterionRepository> logger,
        IMongoDatabase database,
        MongoCollections collections)
    {
        _context = context;
        _logger = logger;

        _legacyCriteria = database.GetCollection<TestCriterion>(
            collections.TestCriteria);
    }

    public async Task<(List<TestCriterionEntity> Items, long TotalCount)> GetPagedAsync(
        int page,
        int pageSize,
        string? search = null,
        string? category = null,
        bool? isActive = null,
        CancellationToken ct = default)
    {
        if (_context.TestCriteria != null)
        {
            try
            {
                var builder = Builders<TestCriterionEntity>.Filter;
                var filter = builder.Eq(c => c.IsDeleted, false);

                if (!string.IsNullOrWhiteSpace(search))
                {
                    var regex = new MongoDB.Bson.BsonRegularExpression(search.Trim(), "i");
                    filter &= (builder.Regex(c => c.CriterionCode, regex) |
                               builder.Regex(c => c.CriterionName, regex) |
                               builder.Regex(c => c.EnglishName, regex));
                }

                if (!string.IsNullOrWhiteSpace(category))
                {
                    filter &= builder.Eq(c => c.Category, category.Trim());
                }

                if (isActive.HasValue)
                {
                    filter &= builder.Eq(c => c.IsActive, isActive.Value);
                }

                var total = await _context.TestCriteria.CountDocumentsAsync(filter, cancellationToken: ct);
                var items = await _context.TestCriteria
                    .Find(filter)
                    .SortBy(c => c.CriterionCode)
                    .Skip((page - 1) * pageSize)
                    .Limit(pageSize)
                    .ToListAsync(ct);

                return (items, total);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to query test criteria from MongoDB. Falling back to memory store.");
            }
        }

        var query = _memoryCriteria.Values.Where(c => !c.IsDeleted);
        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(c =>
                c.CriterionCode.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                c.CriterionName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                c.EnglishName.Contains(search, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(category))
        {
            query = query.Where(c => c.Category.Equals(category, StringComparison.OrdinalIgnoreCase));
        }

        if (isActive.HasValue)
        {
            query = query.Where(c => c.IsActive == isActive.Value);
        }

        var list = query.OrderBy(c => c.CriterionCode).ToList();
        var paged = list.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        return (paged, list.Count);
    }

    public async Task<TestCriterionEntity?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        if (_context.TestCriteria != null)
        {
            try
            {
                var filter = Builders<TestCriterionEntity>.Filter.And(
                    Builders<TestCriterionEntity>.Filter.Eq(c => c.Id, id),
                    Builders<TestCriterionEntity>.Filter.Eq(c => c.IsDeleted, false)
                );
                return await _context.TestCriteria.Find(filter).FirstOrDefaultAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to query criterion by Id from MongoDB. Falling back to memory store.");
            }
        }

        _memoryCriteria.TryGetValue(id, out var item);
        return item is { IsDeleted: false } ? item : null;
    }

    public async Task<TestCriterionEntity?> GetByCodeAsync(string code, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        var upperCode = code.Trim().ToUpperInvariant();

        var found = await FindCriterionByCodeCoreAsync(upperCode, ct);
        if (found != null) return found;

        // Ensure core WHO/ISO 15189 criteria (including HCT, MCV, K, NA) are seeded on first miss.
        if (!_seeded)
        {
            await EnsureDefaultCriteriaSeededAsync(ct);
            found = await FindCriterionByCodeCoreAsync(upperCode, ct);
            if (found != null) return found;
        }

        return _memoryCriteria.Values.FirstOrDefault(c =>
            !c.IsDeleted && c.CriterionCode.Equals(upperCode, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<TestCriterionEntity?> FindCriterionByCodeCoreAsync(string upperCode, CancellationToken ct)
    {
        if (_context.TestCriteria != null)
        {
            try
            {
                var codeRegex = new BsonRegularExpression($"^{Regex.Escape(upperCode)}$", "i");
                var fb = Builders<TestCriterionEntity>.Filter;
                var filter = fb.And(
                    fb.Regex(c => c.CriterionCode, codeRegex),
                    fb.Ne(c => c.IsDeleted, true)
                );
                var matches = await _context.TestCriteria.Find(filter).ToListAsync(ct);
                var canonical = matches.FirstOrDefault(c => c.IsActive) ?? matches.FirstOrDefault();
                if (canonical != null) return canonical;

                // Fallback to legacy snake_case / string-_id documents (e.g. { _id: "CRT-HCT", code: "HCT" }).
                if (_context.Database != null)
                {
                    var rawCol = _context.Database.GetCollection<BsonDocument>("test_criteria");
                    var rf = Builders<BsonDocument>.Filter;
                    var rawDoc = await rawCol.Find(rf.And(
                        rf.Or(
                            rf.Regex("criterionCode", codeRegex),
                            rf.Regex("criterion_code", codeRegex),
                            rf.Regex("code", codeRegex)),
                        rf.Ne("isDeleted", true),
                        rf.Ne("is_deleted", true))).FirstOrDefaultAsync(ct);

                    if (rawDoc != null)
                    {
                        var isActive = rawDoc.TryGetValue("isActive", out var actVal) && actVal.IsBoolean
                            ? actVal.AsBoolean
                            : (rawDoc.TryGetValue("is_active", out var actSnake) && actSnake.IsBoolean ? actSnake.AsBoolean : true);
                        return new TestCriterionEntity
                        {
                            Id = rawDoc.GetValue("_id", BsonNull.Value).ToString() ?? upperCode,
                            CriterionCode = upperCode,
                            CriterionName = GetRawString(rawDoc, "criterionName", "criterion_name", "name") ?? upperCode,
                            EnglishName = GetRawString(rawDoc, "englishName", "english_name") ?? string.Empty,
                            Category = GetRawString(rawDoc, "category") ?? "Biochemistry",
                            Unit = GetRawString(rawDoc, "unit") ?? string.Empty,
                            ValueType = GetRawString(rawDoc, "valueType", "value_type") ?? "Numeric",
                            NormalMin = GetRawDouble(rawDoc, "normalMin", "normal_min"),
                            NormalMax = GetRawDouble(rawDoc, "normalMax", "normal_max"),
                            NormalText = GetRawString(rawDoc, "normalText", "normal_text"),
                            PanicLow = GetRawDouble(rawDoc, "panicLow", "panic_low"),
                            PanicHigh = GetRawDouble(rawDoc, "panicHigh", "panic_high"),
                            IsActive = isActive,
                            IsDeleted = false
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to query criterion by code {Code} from MongoDB. Falling back to memory store.", upperCode);
            }
        }

        return null;
    }

    private static string? GetRawString(BsonDocument doc, params string[] names)
    {
        foreach (var n in names)
        {
            if (doc.TryGetValue(n, out var v) && !v.IsBsonNull && v.IsString && !string.IsNullOrWhiteSpace(v.AsString))
                return v.AsString.Trim();
        }
        return null;
    }

    private static double? GetRawDouble(BsonDocument doc, params string[] names)
    {
        foreach (var n in names)
        {
            if (doc.TryGetValue(n, out var v) && !v.IsBsonNull && v.IsNumeric)
                return v.ToDouble();
        }
        return null;
    }

    public async Task CreateAsync(TestCriterionEntity entity, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(entity.Id))
        {
            entity.Id = MongoDB.Bson.ObjectId.GenerateNewId().ToString();
        }

        if (_context.TestCriteria != null)
        {
            try
            {
                await _context.TestCriteria.InsertOneAsync(entity, cancellationToken: ct);
                _memoryCriteria[entity.Id] = entity;
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to insert criterion to MongoDB. Falling back to memory store.");
            }
        }

        _memoryCriteria[entity.Id] = entity;
    }

    public async Task UpdateAsync(TestCriterionEntity entity, CancellationToken ct = default)
    {
        entity.UpdatedAt = DateTime.UtcNow;

        if (_context.TestCriteria != null)
        {
            try
            {
                var filter = Builders<TestCriterionEntity>.Filter.Eq(c => c.Id, entity.Id);
                await _context.TestCriteria.ReplaceOneAsync(filter, entity, cancellationToken: ct);
                _memoryCriteria[entity.Id] = entity;
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to update criterion in MongoDB. Falling back to memory store.");
            }
        }

        _memoryCriteria[entity.Id] = entity;
    }

    public async Task DeactivateAsync(string id, CancellationToken ct = default)
    {
        var entity = await GetByIdAsync(id, ct);
        if (entity == null) return;

        // Soft deactivation: preserves medical audit trail per ISO 15189
        entity.IsActive = false;
        entity.UpdatedAt = DateTime.UtcNow;

        await UpdateAsync(entity, ct);
    }

    /// <summary>
    /// Seeds core WHO/ADA clinical parameters for biochemistry and hematology testing.
    /// </summary>
    public async Task EnsureDefaultCriteriaSeededAsync(CancellationToken ct = default)
    {
        if (_seeded) return;
        _seeded = true;

        var defaultCriteria = new List<TestCriterionEntity>
        {
            new()
            {
                CriterionCode = "GLU",
                CriterionName = "Glucose máu lúc đói",
                EnglishName = "Fasting Blood Glucose",
                Category = "Biochemistry",
                Unit = "mmol/L",
                ValueType = "Numeric",
                NormalMin = 3.9,
                NormalMax = 6.4,
                PanicLow = 2.2,
                PanicHigh = 25.0,
                IsActive = true
            },
            new()
            {
                CriterionCode = "HBA1C",
                CriterionName = "Hemoglobin A1c (Kiểm soát đường huyết)",
                EnglishName = "Glycated Hemoglobin",
                Category = "Biochemistry",
                Unit = "%",
                ValueType = "Numeric",
                NormalMin = 4.0,
                NormalMax = 5.6,
                PanicLow = null,
                PanicHigh = 10.0,
                IsActive = true
            },
            new()
            {
                CriterionCode = "WBC",
                CriterionName = "Số lượng bạch cầu",
                EnglishName = "White Blood Cell Count",
                Category = "Hematology",
                Unit = "G/L",
                ValueType = "Numeric",
                NormalMin = 4.0,
                NormalMax = 10.0,
                PanicLow = 1.0,
                PanicHigh = 30.0,
                IsActive = true
            },
            new()
            {
                CriterionCode = "RBC",
                CriterionName = "Số lượng hồng cầu",
                EnglishName = "Red Blood Cell Count",
                Category = "Hematology",
                Unit = "T/L",
                ValueType = "Numeric",
                NormalMin = 3.8,
                NormalMax = 5.3,
                PanicLow = 2.0,
                PanicHigh = 7.0,
                IsActive = true
            },
            new()
            {
                CriterionCode = "ALT",
                CriterionName = "Alanine Aminotransferase (Men gan ALT)",
                EnglishName = "Serum Glutamic Pyruvic Transaminase",
                Category = "Biochemistry",
                Unit = "U/L",
                ValueType = "Numeric",
                NormalMin = 0.0,
                NormalMax = 41.0,
                PanicLow = null,
                PanicHigh = 500.0,
                IsActive = true
            },
            new()
            {
                CriterionCode = "AST",
                CriterionName = "Aspartate Aminotransferase (Men gan AST)",
                EnglishName = "Serum Glutamic Oxaloacetic Transaminase",
                Category = "Biochemistry",
                Unit = "U/L",
                ValueType = "Numeric",
                NormalMin = 0.0,
                NormalMax = 40.0,
                PanicLow = null,
                PanicHigh = 500.0,
                IsActive = true
            },
            new()
            {
                CriterionCode = "HGB",
                CriterionName = "Huyết sắc tố (Hemoglobin)",
                EnglishName = "Hemoglobin Concentration",
                Category = "Hematology",
                Unit = "g/L",
                ValueType = "Numeric",
                NormalMin = 120.0,
                NormalMax = 165.0,
                PanicLow = 70.0,
                PanicHigh = 200.0,
                IsActive = true
            },
            new()
            {
                CriterionCode = "PLT",
                CriterionName = "Số lượng tiểu cầu",
                EnglishName = "Platelet Count",
                Category = "Hematology",
                Unit = "G/L",
                ValueType = "Numeric",
                NormalMin = 150.0,
                NormalMax = 450.0,
                PanicLow = 50.0,
                PanicHigh = 1000.0,
                IsActive = true
            },
            new()
            {
                CriterionCode = "CRE",
                CriterionName = "Creatinine huyết thanh (Chức năng thận)",
                EnglishName = "Serum Creatinine",
                Category = "Biochemistry",
                Unit = "µmol/L",
                ValueType = "Numeric",
                NormalMin = 53.0,
                NormalMax = 106.0,
                PanicLow = null,
                PanicHigh = 500.0,
                IsActive = true
            },
            new()
            {
                CriterionCode = "CHOL",
                CriterionName = "Cholesterol toàn phần",
                EnglishName = "Total Cholesterol",
                Category = "Biochemistry",
                Unit = "mmol/L",
                ValueType = "Numeric",
                NormalMin = 3.4,
                NormalMax = 5.2,
                PanicLow = null,
                PanicHigh = 10.0,
                IsActive = true
            },
            new()
            {
                CriterionCode = "HCT",
                CriterionName = "Dung tích hồng cầu (Hematocrit)",
                EnglishName = "Hematocrit",
                Category = "Hematology",
                Unit = "%",
                ValueType = "Numeric",
                NormalMin = 35.0,
                NormalMax = 50.0,
                PanicLow = 20.0,
                PanicHigh = 60.0,
                IsActive = true
            },
            new()
            {
                CriterionCode = "MCV",
                CriterionName = "Thể tích trung bình hồng cầu (MCV)",
                EnglishName = "Mean Corpuscular Volume",
                Category = "Hematology",
                Unit = "fL",
                ValueType = "Numeric",
                NormalMin = 80.0,
                NormalMax = 100.0,
                PanicLow = 60.0,
                PanicHigh = 120.0,
                IsActive = true
            },
            new()
            {
                CriterionCode = "K",
                CriterionName = "Kali máu (Serum Potassium - K+)",
                EnglishName = "Serum Potassium (K+)",
                Category = "BloodGas",
                Unit = "mmol/L",
                ValueType = "Numeric",
                NormalMin = 3.5,
                NormalMax = 5.0,
                PanicLow = 2.5,
                PanicHigh = 6.5,
                IsActive = true
            },
            new()
            {
                CriterionCode = "NA",
                CriterionName = "Natri máu (Serum Sodium - Na+)",
                EnglishName = "Serum Sodium (Na+)",
                Category = "BloodGas",
                Unit = "mmol/L",
                ValueType = "Numeric",
                NormalMin = 135.0,
                NormalMax = 145.0,
                PanicLow = 120.0,
                PanicHigh = 160.0,
                IsActive = true
            }
        };

        foreach (var c in defaultCriteria)
        {
            var existing = await FindCriterionByCodeCoreAsync(c.CriterionCode, ct);
            if (existing == null)
            {
                await CreateAsync(c, ct);
            }
        }
    }

    public async Task<List<TestCriterion>> GetAllAsync(CancellationToken ct = default)
    {
        await EnsureDefaultCriteriaSeededAsync(ct);
        var (items, _) = await GetPagedAsync(1, 1000, null, null, true, ct);
        return items.Select(x => new TestCriterion
        {
            Id = x.Id,
            Code = x.CriterionCode,
            Name = x.CriterionName,
            Unit = x.Unit,
            Category = x.Category
        }).ToList();
    }
}
