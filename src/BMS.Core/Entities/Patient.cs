using System.ComponentModel;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Attributes;

namespace BMS.Core.Entities;

/// <summary>
/// Biological gender of the patient for laboratory reference interval determination.
/// </summary>
public enum BiologicalGender
{
    Unspecified = 0,
    Male = 1,
    Female = 2,
    Other = 3
}

/// <summary>
/// Embedded vital signs document in <c>patients</c> collection.
/// </summary>
[BsonIgnoreExtraElements]
public class PatientVitals
{
    [BsonElement("height_cm")]
    public double? HeightCm { get; set; }

    [BsonElement("weight_kg")]
    public double? WeightKg { get; set; }

    [BsonElement("blood_pressure")]
    public string? BloodPressure { get; set; }

    [BsonElement("heart_rate_bpm")]
    public int? HeartRateBpm { get; set; }

    [BsonElement("temperature_c")]
    public double? TemperatureC { get; set; }

    [BsonElement("spo2_percent")]
    public int? Spo2Percent { get; set; }
}

/// <summary>
/// Embedded family dependent record in <c>patients</c> collection.
/// </summary>
[BsonIgnoreExtraElements]
public class PatientDependent
{
    [BsonElement("dependent_id")]
    public string DependentId { get; set; } = string.Empty;

    [BsonElement("fullname")]
    public string FullName { get; set; } = string.Empty;

    [BsonElement("dob")]
    public DateTime? DateOfBirth { get; set; }

    [BsonElement("gender")]
    public string Gender { get; set; } = "Unspecified";

    [BsonElement("relationship")]
    public string Relationship { get; set; } = string.Empty;

    [BsonElement("identity_number")]
    public string? IdentityNumber { get; set; }
}

/// <summary>
/// Domain entity representing a clinical or guest patient record (Create Guest Profile / View Patient Information).
/// Maps directly to the <c>patients</c> collection in <c>BMS_Database</c>.
/// </summary>
[BsonIgnoreExtraElements]
public class Patient : ISupportInitialize
{
    [BsonId]
    [BsonRepresentation(BsonType.String)]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Linked account ObjectId (null for walk-in / guest patient profiles).
    /// </summary>
    [BsonElement("AccountId")]
    [BsonIgnoreIfNull]
    [BsonSerializer(typeof(FlexibleNullableStringObjectIdSerializer))]
    public string? AccountId { get; set; }

    [BsonIgnore]
    public string? UserId
    {
        get => AccountId;
        set => AccountId = value;
    }

    [BsonElement("patient_code")]
    [BsonIgnoreIfNull]
    public string? PatientCodeField { get; set; }

    [BsonIgnore]
    public string PatientCode
    {
        get => !string.IsNullOrWhiteSpace(PatientCodeField) ? PatientCodeField : Id;
        set => PatientCodeField = value;
    }

    [BsonElement("fullname")]
    public string FullName { get; set; } = string.Empty;

    [BsonElement("dob")]
    public DateTime? DateOfBirth { get; set; }

    [BsonElement("gender")]
    public string GenderString { get; set; } = "Unspecified";

    [BsonIgnore]
    public BiologicalGender Gender
    {
        get => Enum.TryParse<BiologicalGender>(GenderString, true, out var g) ? g : BiologicalGender.Unspecified;
        set => GenderString = value.ToString();
    }

    [BsonElement("phone")]
    [BsonIgnoreIfNull]
    public string? PhoneNumber { get; set; }

    [BsonElement("email")]
    [BsonIgnoreIfNull]
    public string? Email { get; set; }

    [BsonElement("identity_number")]
    [BsonIgnoreIfNull]
    public string? IdentityCardNumber { get; set; }

    [BsonElement("address")]
    [BsonIgnoreIfNull]
    public string? Address { get; set; }

    [BsonElement("blood_type")]
    [BsonIgnoreIfNull]
    public string? BloodType { get; set; }

    [BsonElement("emergency_contact_name")]
    [BsonIgnoreIfNull]
    public string? EmergencyContactName { get; set; }

    [BsonElement("emergency_contact_phone")]
    [BsonIgnoreIfNull]
    public string? EmergencyContactPhone { get; set; }

    [BsonElement("medical_history")]
    [BsonIgnoreIfNull]
    public string? MedicalHistory { get; set; }

    [BsonElement("allergies")]
    [BsonIgnoreIfNull]
    public List<string> Allergies { get; set; } = [];

    [BsonElement("vitals")]
    [BsonIgnoreIfNull]
    public PatientVitals? Vitals { get; set; }

    [BsonElement("dependents")]
    public List<PatientDependent> Dependents { get; set; } = [];

    [BsonElement("is_guest")]
    public bool IsGuest { get; set; }

    [BsonElement("Createby")]
    [BsonIgnoreIfNull]
    [BsonSerializer(typeof(FlexibleNullableStringObjectIdSerializer))]
    public string? CreatedBy { get; set; }

    [BsonElement("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [BsonElement("updated_at")]
    public DateTime? UpdatedAt { get; set; } = DateTime.UtcNow;

    [BsonExtraElements]
    public BsonDocument? ExtraElements { get; set; }

    public void BeginInit()
    {
    }

    public void EndInit()
    {
        if (ExtraElements != null)
        {
            if (string.IsNullOrWhiteSpace(AccountId) && ExtraElements.TryGetValue("userId", out var uid) && !uid.IsBsonNull)
                AccountId = uid.ToString();

            if (string.IsNullOrWhiteSpace(CreatedBy) && ExtraElements.TryGetValue("created_by", out var cb2) && !cb2.IsBsonNull)
                CreatedBy = cb2.ToString();

            if (string.IsNullOrWhiteSpace(FullName) && ExtraElements.TryGetValue("fullName", out var fn) && fn.IsString)
                FullName = fn.AsString;

            if (DateOfBirth == null && ExtraElements.TryGetValue("dateOfBirth", out var dob) && dob.IsValidDateTime)
                DateOfBirth = dob.ToUniversalTime();

            if (string.IsNullOrWhiteSpace(PhoneNumber) && ExtraElements.TryGetValue("phoneNumber", out var ph) && ph.IsString)
                PhoneNumber = ph.AsString;

            if (string.IsNullOrWhiteSpace(IdentityCardNumber) && ExtraElements.TryGetValue("identityCardNumber", out var icn) && icn.IsString)
                IdentityCardNumber = icn.AsString;

            if (string.IsNullOrWhiteSpace(BloodType) && ExtraElements.TryGetValue("bloodType", out var bt) && bt.IsString)
                BloodType = bt.AsString;

            if (string.IsNullOrWhiteSpace(MedicalHistory) && ExtraElements.TryGetValue("medicalHistory", out var mh) && mh.IsString)
                MedicalHistory = mh.AsString;

            if (!ExtraElements.Contains("is_guest"))
                IsGuest = IsGuest || string.IsNullOrWhiteSpace(AccountId);

            ExtraElements = null;
        }
        else
        {
            IsGuest = IsGuest || string.IsNullOrWhiteSpace(AccountId);
        }
    }
}

/// <summary>
/// BSON serializer supporting nullable ObjectId or String representation seamlessly.
/// </summary>
public sealed class FlexibleNullableStringObjectIdSerializer : MongoDB.Bson.Serialization.Serializers.SerializerBase<string?>
{
    public override string? Deserialize(BsonDeserializationContext context, BsonDeserializationArgs args)
    {
        var type = context.Reader.GetCurrentBsonType();
        if (type == BsonType.Null)
        {
            context.Reader.ReadNull();
            return null;
        }

        return type switch
        {
            BsonType.ObjectId => context.Reader.ReadObjectId().ToString(),
            BsonType.String => context.Reader.ReadString(),
            BsonType.Int32 => context.Reader.ReadInt32().ToString(),
            BsonType.Int64 => context.Reader.ReadInt64().ToString(),
            _ => context.Reader.ReadString()
        };
    }

    public override void Serialize(BsonSerializationContext context, BsonSerializationArgs args, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            context.Writer.WriteNull();
            return;
        }

        if (ObjectId.TryParse(value, out var oid))
        {
            context.Writer.WriteObjectId(oid);
        }
        else
        {
            context.Writer.WriteString(value);
        }
    }
}
