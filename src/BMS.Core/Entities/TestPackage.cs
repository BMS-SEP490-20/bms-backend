using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace BMS.Core.Entities;


/// Entity representing a laboratory test package (Service Catalog item).
/// Maps to the <c>test_packages</c> collection in MongoDB.

public class TestPackage
{
    /// Unique package identifier (e.g. PKG-CBC-01).
    [BsonId]
    [BsonRepresentation(BsonType.String)]
    public string Id { get; set; } = string.Empty;

    /// Short package code used for display and search (e.g. CBC-01).   
    [BsonElement("package_code")]
    public string PackageCode { get; set; } = string.Empty;

    /// Full name of the test package.
    [BsonElement("package_name")]
    public string PackageName { get; set; } = string.Empty;

    /// Category group (Hematology, Biochemistry, Immunology, Coagulation...).
    [BsonElement("category")]
    public string Category { get; set; } = string.Empty;

    /// Service price in VND.
    [BsonElement("price")]
    public decimal Price { get; set; }

    /// Short description of the package purpose and scope.
    [BsonElement("description")]
    public string Description { get; set; } = string.Empty;

    /// Required sample tube type (EDTA-Purple, Serum-Red/Yellow...).
    [BsonElement("sample_tube")]
    public string SampleTube { get; set; } = string.Empty;

    /// Minimum blood volume required (ml).
    [BsonElement("sample_volume_ml")]
    public double SampleVolumeMl { get; set; }

    /// Whether the package is currently offered.
    /// Only packages with <c>true</c> are shown in the catalog.
    [BsonElement("is_active")]
    public bool IsActive { get; set; } = true;
}