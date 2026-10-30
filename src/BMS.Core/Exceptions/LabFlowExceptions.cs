namespace BMS.Core.Exceptions;

// Exceptions for the Đợt 3 main lab flow (reception → order → fee → sampling → result).
// GlobalExceptionHandler maps every AppException to ProblemDetails { status, title, code } — never ex.Message
// of an infrastructure exception.

/// <summary>The resource exists but is in a state that forbids the operation (TIEU-CHI C10: 409).</summary>
public sealed class ConflictStateException(string code, string message) : AppException(409, code, message);

/// <summary>Daily queue numbers Q-001..Q-999 are used up (BA:L33 — never issue Q-1000).</summary>
public sealed class QueueExhaustedException()
    : AppException(409, "MSG-ERR-REC-02", "Đã hết số thứ tự trong ngày (Q-999). Vui lòng chờ sang ngày mới.");

/// <summary>A daily code sequence (PAT-/SMP-) exceeded its 4-digit range.</summary>
public sealed class SequenceExhaustedException(string prefix)
    : AppException(409, "SEQUENCE_EXHAUSTED", $"Đã hết dải mã {prefix} trong ngày.");
