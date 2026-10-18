using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace BMS.Core.Entities;


/// <summary>
/// Entity representing a laboratory test package (Service Catalog item - UC01 / Search Test Packages).
/// Maps to the <c>test_packages</c> collection in MongoDB.
/// </summary>
[BsonIgnoreExtraElements]
public class TestPackage
{
    /// <summary>Unique package identifier (e.g. PKG-CBC-01).</summary>
    [BsonId]
    [BsonRepresentation(BsonType.String)]
    public string Id { get; set; } = string.Empty;

    /// <summary>Short package code used for display and search (e.g. CBC-01).</summary>
    [BsonElement("package_code")]
    public string PackageCode { get; set; } = string.Empty;

    /// <summary>Full name of the test package.</summary>
    [BsonElement("package_name")]
    public string PackageName { get; set; } = string.Empty;

    /// <summary>Category group (Hematology, Biochemistry, Immunology, Coagulation...).</summary>
    [BsonElement("category")]
    public string Category { get; set; } = string.Empty;

    /// <summary>Service price in VND.</summary>
    [BsonElement("price")]
    public decimal Price { get; set; }

    /// <summary>Short description of the package purpose and scope.</summary>
    [BsonElement("description")]
    public string Description { get; set; } = string.Empty;

    /// <summary>Required sample tube type (EDTA-Purple, Serum-Red/Yellow...).</summary>
    [BsonElement("sample_tube")]
    public string SampleTube { get; set; } = string.Empty;

    /// <summary>Minimum blood volume required (ml).</summary>
    [BsonElement("sample_volume_ml")]
    public double SampleVolumeMl { get; set; }

    /// <summary>
    /// Whether the package is currently offered.
    /// Only packages with <c>true</c> are shown in the catalog.
    /// </summary>
    [BsonElement("is_active")]
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// Join document linking a test package to its test criteria in <c>test_package_criteria</c>.
/// </summary>
[BsonIgnoreExtraElements]
public class TestPackageCriterionMap
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    [BsonIgnoreIfDefault]
    public string? Id { get; set; }

    [BsonElement("package_id")]
    public string PackageId { get; set; } = string.Empty;

    [BsonElement("criteria_id")]
    public string CriteriaId { get; set; } = string.Empty;
}