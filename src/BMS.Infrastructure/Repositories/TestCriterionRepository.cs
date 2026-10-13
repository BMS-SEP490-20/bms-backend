using System.Collections.Concurrent;
using BMS.Core.Entities;
using BMS.Core.Interfaces;
using BMS.Infrastructure.Data;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace BMS.Infrastructure.Repositories;

/// <summary>
/// Implements repository for laboratory test criteria interacting with MongoDB collection "test_criteria".
/// Conforms to: ISO 15189 Quality Standards & Repository Pattern.
/// </summary>
public class TestCriterionRepository : ITestCriterionRepository
{
    private readonly MongoDbContext _context;
    private readonly ILogger<TestCriterionRepository> _logger;

    private static readonly ConcurrentDictionary<string, TestCriterionEntity> _memoryCriteria = new();
    private static bool _seeded = false;

    public TestCriterionRepository(MongoDbContext context, ILogger<TestCriterionRepository> logger)
    {
        _context = context;
        _logger = logger;
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
        var upperCode = code.Trim().ToUpperInvariant();

        if (_context.TestCriteria != null)
        {
            try
            {
                var filter = Builders<TestCriterionEntity>.Filter.And(
                    Builders<TestCriterionEntity>.Filter.Eq(c => c.CriterionCode, upperCode),
                    Builders<TestCriterionEntity>.Filter.Eq(c => c.IsDeleted, false)
                );
                return await _context.TestCriteria.Find(filter).FirstOrDefaultAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to query criterion by code from MongoDB. Falling back to memory store.");
            }
        }

        return _memoryCriteria.Values.FirstOrDefault(c =>
            !c.IsDeleted && c.CriterionCode.Equals(upperCode, StringComparison.OrdinalIgnoreCase));
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
            }
        };

        foreach (var c in defaultCriteria)
        {
            var existing = await GetByCodeAsync(c.CriterionCode, ct);
            if (existing == null)
            {
                await CreateAsync(c, ct);
            }
        }
    }
}
