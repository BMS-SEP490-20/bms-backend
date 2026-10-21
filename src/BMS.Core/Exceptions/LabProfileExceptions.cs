namespace BMS.Core.Exceptions;

/// <summary>Duplicate identity, stale revision or active assignment prevents the requested change.</summary>
public sealed class LabProfileConflictException(string message) : AppException(409, "LAB_PROFILE_CONFLICT", message);
/// <summary>The requested active professional profile no longer exists.</summary>
public sealed class LabProfileNotFoundException() : AppException(404, "LAB_PROFILE_NOT_FOUND", "Không tìm thấy hồ sơ Lab.");
