using System;
using System.ComponentModel;
using System.Linq;
using System.Text.Json.Serialization;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Bson.Serialization.Serializers;

namespace BMS.Core.Entities;

/// <summary>
/// Status lifecycle of a patient clinical appointment.
/// </summary>
public enum AppointmentStatus
{
    Pending = 0,
    Confirmed = 1,
    CheckedIn = 2,
    InProgress = 3,
    Completed = 4,
    Cancelled = 5,
    NoShow = 6
}

/// <summary>
/// Type of appointment booking: At clinic or home phlebotomy collection.
/// </summary>
public enum AppointmentType
{
    ClinicVisit = 1,
    HomePhlebotomy = 2
}

/// <summary>
/// Flexible serializer for AppointmentStatus supporting Int32, Int64, and legacy string values
/// (e.g., 'Completed', 'SampleCollected', 'Scheduled', 'Cancelled').
/// </summary>
public sealed class FlexibleAppointmentStatusSerializer : SerializerBase<AppointmentStatus>
{
    public override AppointmentStatus Deserialize(BsonDeserializationContext context, BsonDeserializationArgs args)
    {
        var type = context.Reader.GetCurrentBsonType();
        switch (type)
        {
            case BsonType.Int32:
                return (AppointmentStatus)context.Reader.ReadInt32();
            case BsonType.Int64:
                return (AppointmentStatus)(int)context.Reader.ReadInt64();
            case BsonType.String:
                var str = context.Reader.ReadString();
                if (string.IsNullOrWhiteSpace(str)) return AppointmentStatus.Pending;
                if (Enum.TryParse<AppointmentStatus>(str, true, out var parsed)) return parsed;
                var lower = str.Trim().ToLowerInvariant();
                return lower switch
                {
                    "scheduled" or "pending" => AppointmentStatus.Pending,
                    "confirmed" => AppointmentStatus.Confirmed,
                    "checkedin" or "checked_in" or "samplecollected" or "sample_collected" => AppointmentStatus.CheckedIn,
                    "inprogress" or "in_progress" or "processing" => AppointmentStatus.InProgress,
                    "completed" => AppointmentStatus.Completed,
                    "cancelled" => AppointmentStatus.Cancelled,
                    "noshow" or "no_show" => AppointmentStatus.NoShow,
                    _ => AppointmentStatus.Pending
                };
            case BsonType.Null:
                context.Reader.ReadNull();
                return AppointmentStatus.Pending;
            default:
                context.Reader.SkipValue();
                return AppointmentStatus.Pending;
        }
    }

    public override void Serialize(BsonSerializationContext context, BsonSerializationArgs args, AppointmentStatus value)
    {
        context.Writer.WriteString(value.ToString());
    }
}

/// <summary>
/// Flexible serializer for AppointmentType supporting Int32, Int64, and string values.
/// </summary>
public sealed class FlexibleAppointmentTypeSerializer : SerializerBase<AppointmentType>
{
    public override AppointmentType Deserialize(BsonDeserializationContext context, BsonDeserializationArgs args)
    {
        var type = context.Reader.GetCurrentBsonType();
        switch (type)
        {
            case BsonType.Int32:
                return (AppointmentType)context.Reader.ReadInt32();
            case BsonType.Int64:
                return (AppointmentType)(int)context.Reader.ReadInt64();
            case BsonType.String:
                var str = context.Reader.ReadString();
                if (Enum.TryParse<AppointmentType>(str, true, out var parsed)) return parsed;
                return AppointmentType.ClinicVisit;
            case BsonType.Null:
                context.Reader.ReadNull();
                return AppointmentType.ClinicVisit;
            default:
                context.Reader.SkipValue();
                return AppointmentType.ClinicVisit;
        }
    }

    public override void Serialize(BsonSerializationContext context, BsonSerializationArgs args, AppointmentType value)
    {
        context.Writer.WriteString(value.ToString());
    }
}

/// <summary>
/// Represents a scheduled clinical consultation or specimen collection appointment (UC21).
/// </summary>
[BsonIgnoreExtraElements]
public class Appointment : ISupportInitialize
{
    [BsonExtraElements]
    [JsonIgnore]
    public BsonDocument LegacyFields { get; set; } = new();

    public void BeginInit() { }

    public void EndInit()
    {
        string Text(string key) => LegacyFields.TryGetValue(key, out var v) && !v.IsBsonNull
            ? v.IsString ? v.AsString : v.ToString() ?? string.Empty : string.Empty;
        DateTime? Date(string key) => LegacyFields.TryGetValue(key, out var v) && v.IsBsonDateTime ? v.ToUniversalTime() : null;

        if (string.IsNullOrEmpty(PatientId)) PatientId = Text("patient_id");
        if (string.IsNullOrEmpty(AppointmentCode))
        {
            var code = Text("appointment_code");
            AppointmentCode = !string.IsNullOrEmpty(code) ? code : Id;
        }
        if (string.IsNullOrEmpty(DoctorId)) DoctorId = Text("doctor_id");
        if (string.IsNullOrEmpty(PackageId))
        {
            PackageId = Text("package_id");
            if (string.IsNullOrEmpty(PackageId) && LegacyFields.TryGetValue("package_ids", out var pkgArr) && pkgArr.IsBsonArray && pkgArr.AsBsonArray.Count > 0)
            {
                PackageId = pkgArr.AsBsonArray[0].ToString();
            }
        }
        if (!QueueNumber.HasValue && LegacyFields.TryGetValue("queue_number", out var qv) && !qv.IsBsonNull)
        {
            var rawStr = qv.ToString()?.Trim();
            if (!string.IsNullOrEmpty(rawStr))
            {
                var digits = new string(rawStr.Where(char.IsDigit).ToArray());
                if (int.TryParse(digits, out var parsedQ))
                {
                    QueueNumber = parsedQ;
                }
            }
        }
        if (AppointmentDate == default) AppointmentDate = Date("appointment_date") ?? default;
        if (CreatedAt == default) CreatedAt = Date("created_at") ?? (AppointmentDate != default ? AppointmentDate : DateTime.UtcNow);
        UpdatedAt ??= Date("updated_at");
        if (string.IsNullOrEmpty(Notes)) Notes = Text("notes");
        if (string.IsNullOrEmpty(CollectionAddress)) CollectionAddress = Text("collection_address");
        if (string.IsNullOrEmpty(RoomId)) RoomId = Text("room_id");
        if (string.IsNullOrEmpty(SepayTransactionId)) SepayTransactionId = Text("sepay_transaction_id");
    }

    [BsonId]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [BsonElement("appointmentCode")]
    public string AppointmentCode { get; set; } = string.Empty;

    [BsonElement("patientId")]
    public string PatientId { get; set; } = string.Empty;

    [BsonElement("doctorId")]
    [BsonSerializer(typeof(FlexibleNullableStringObjectIdSerializer))]
    public string? DoctorId { get; set; }

    [BsonElement("packageId")]
    public string? PackageId { get; set; }

    [BsonElement("appointmentDate")]
    public DateTime AppointmentDate { get; set; }

    [BsonElement("startTime")]
    public TimeSpan StartTime { get; set; }

    [BsonElement("endTime")]
    public TimeSpan EndTime { get; set; }

    [BsonElement("type")]
    [BsonSerializer(typeof(FlexibleAppointmentTypeSerializer))]
    public AppointmentType Type { get; set; } = AppointmentType.ClinicVisit;

    [BsonElement("status")]
    [BsonSerializer(typeof(FlexibleAppointmentStatusSerializer))]
    public AppointmentStatus Status { get; set; } = AppointmentStatus.Pending;

    [BsonElement("notes")]
    public string? Notes { get; set; }

    [BsonElement("collectionAddress")]
    public string? CollectionAddress { get; set; }

    [BsonElement("queueNumber")]
    public int? QueueNumber { get; set; }

    [BsonElement("roomId")]
    public string? RoomId { get; set; }

    [BsonElement("recallCount")]
    public int RecallCount { get; set; } = 0;

    [BsonElement("consultationStartTime")]
    public DateTime? ConsultationStartTime { get; set; }

    [BsonElement("consultationEndTime")]
    public DateTime? ConsultationEndTime { get; set; }

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [BsonElement("updatedAt")]
    public DateTime? UpdatedAt { get; set; }

    [BsonElement("sepayTransactionId")]
    [BsonIgnoreIfNull]
    public string? SepayTransactionId { get; set; }

    [BsonElement("paidAmount")]
    [BsonIgnoreIfNull]
    public decimal? PaidAmount { get; set; }

    [BsonElement("paidAt")]
    [BsonIgnoreIfNull]
    public DateTime? PaidAt { get; set; }
}
