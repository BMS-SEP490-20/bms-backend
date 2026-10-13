namespace BMS.Core.Exceptions;

public sealed class ServicePackageNotFoundException()
    : AppException(404, "SERVICE_PACKAGE_NOT_FOUND", "Không tìm thấy gói dịch vụ.");

public sealed class ServicePackageCodeAlreadyExistsException(string code)
    : AppException(409, "SERVICE_PACKAGE_CODE_EXISTS", $"Mã gói dịch vụ '{code}' đã tồn tại.");
