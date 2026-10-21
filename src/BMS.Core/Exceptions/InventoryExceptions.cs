namespace BMS.Core.Exceptions;

/// <summary>A duplicate identity or stale revision prevents an inventory mutation.</summary>
public sealed class InventoryConflictException(string message)
    : AppException(409, "INVENTORY_CONFLICT", message);

/// <summary>The requested active inventory lot no longer exists.</summary>
public sealed class InventoryNotFoundException()
    : AppException(404, "INVENTORY_NOT_FOUND", "Không tìm thấy lô hóa chất.");
