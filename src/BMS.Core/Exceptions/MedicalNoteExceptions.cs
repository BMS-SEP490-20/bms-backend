namespace BMS.Core.Exceptions;

/// <summary>The source EMR does not exist or has been soft-deleted.</summary>
public sealed class MedicalNoteRecordNotFoundException()
    : AppException(404, "MEDICAL_RECORD_NOT_FOUND", "Không tìm thấy bệnh án. Vui lòng kiểm tra mã bệnh án.");

/// <summary>A submission identifier was reused for different content or another record.</summary>
public sealed class MedicalNoteConflictException()
    : AppException(409, "MEDICAL_NOTE_CONFLICT", "Mã gửi đã được sử dụng. Vui lòng tải lại lịch sử trước khi tạo ghi chú mới.");
