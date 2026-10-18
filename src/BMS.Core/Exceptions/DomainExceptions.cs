namespace BMS.Core.Exceptions;

// =========================================================
// GENERAL AUTHORIZATION / VALIDATION EXCEPTIONS
// =========================================================

public sealed class ForbiddenAccessException(string message = "Bạn không có quyền truy cập tài nguyên này.")
    : AppException(403, "FORBIDDEN", message);

public sealed class DomainValidationException(string code, string message)
    : AppException(400, code, message);

// =========================================================
// PATIENT & GUEST PROFILE EXCEPTIONS
// =========================================================

public sealed class PatientNotFoundException(string? identifier = null)
    : AppException(404, "PATIENT_NOT_FOUND",
        string.IsNullOrWhiteSpace(identifier)
            ? "Không tìm thấy hồ sơ bệnh nhân."
            : $"Không tìm thấy hồ sơ bệnh nhân '{identifier}'.");

public sealed class PatientDuplicateException(string code, string message)
    : AppException(409, code, message);

// =========================================================
// SHIFT MANAGEMENT EXCEPTIONS
// =========================================================

public sealed class ShiftNotFoundException(string? id = null)
    : AppException(404, "SHIFT_NOT_FOUND",
        string.IsNullOrWhiteSpace(id)
            ? "Không tìm thấy ca làm việc."
            : $"Không tìm thấy ca làm việc '{id}'.");

public sealed class ShiftConflictException(string code, string message)
    : AppException(409, code, message);

public sealed class ShiftInUseException(string message)
    : AppException(409, "SHIFT_IN_USE", message);

// =========================================================
// SAMPLE COLLECTION & LIFECYCLE EXCEPTIONS
// =========================================================

public sealed class SampleNotFoundException(string? idOrBarcode = null)
    : AppException(404, "SAMPLE_NOT_FOUND",
        string.IsNullOrWhiteSpace(idOrBarcode)
            ? "Không tìm thấy mẫu xét nghiệm."
            : $"Không tìm thấy mẫu xét nghiệm '{idOrBarcode}'.");

public sealed class InvalidSampleTimestampException(string message)
    : AppException(400, "INVALID_SAMPLE_TIMESTAMP", message);

public sealed class InvalidSampleStatusTransitionException(string currentStatus, string targetStatus, string? detail = null)
    : AppException(409, "INVALID_STATUS_TRANSITION",
        string.IsNullOrWhiteSpace(detail)
            ? $"Không thể chuyển trạng thái mẫu từ '{currentStatus}' sang '{targetStatus}'."
            : $"Không thể chuyển trạng thái mẫu từ '{currentStatus}' sang '{targetStatus}': {detail}");
