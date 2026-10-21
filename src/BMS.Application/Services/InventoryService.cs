using BMS.Application.DTOs.Inventory;
using BMS.Application.Interfaces;
using BMS.Core.Entities;
using BMS.Core.Exceptions;
using BMS.Core.Interfaces;
using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;

namespace BMS.Application.Services;

/// <summary>Derives UC56 warnings from stock and expiry values instead of the potentially stale stored status.</summary>
public sealed class InventoryService(IInventoryRepository repository, TimeProvider clock) : IInventoryService
{
    private DateTime Today => clock.GetUtcNow().ToOffset(TimeSpan.FromHours(7)).Date;

    public async Task<InventoryItemDto> CreateAsync(CreateInventoryDto request, string actorId, string actorName, CancellationToken ct = default)
    {
        ValidateValues(request, actorId);
        var code = request.Code.Trim().ToUpperInvariant();
        var lot = request.LotNumber.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(request.Unit))
            throw new DomainValidationException("INVALID_INVENTORY", "Đơn vị tính không được để trống.");
        // A deterministic ID prevents concurrent creates of the same normalized code/lot without migrating legacy IDs.
        var id = "REA-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code + "\0" + lot)));
        var item = new Reagent { Id = id, Code = code, LotNumber = lot, Unit = request.Unit.Trim(),
            Name = request.Name.Trim(), Manufacturer = request.Manufacturer.Trim(), Quantity = request.Quantity,
            MinimumStock = request.MinimumStock, ExpiryDate = DateTime.SpecifyKind(request.ExpiryDate!.Value.Date, DateTimeKind.Utc) };
        item.Changes.Add(Change("CREATE", actorId, actorName, request.Reason, null, item));
        if (!await repository.CreateAsync(item, ct))
            throw new InventoryConflictException("Mã hóa chất và số lô này đã tồn tại. Vui lòng kiểm tra danh sách kho.");
        return Map(item, Today);
    }

    private static void ValidateValues(InventoryValuesDto request, string actorId)
    {
        if (string.IsNullOrWhiteSpace(actorId)) throw new ForbiddenAccessException();
        var results = new List<ValidationResult>();
        if (!Validator.TryValidateObject(request, new ValidationContext(request), results, true) ||
            string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Manufacturer) ||
            (request.Reason?.Trim().Length ?? 0) < 5 ||
            decimal.Round(request.Quantity, 3) != request.Quantity || decimal.Round(request.MinimumStock, 3) != request.MinimumStock)
            throw new DomainValidationException("INVALID_INVENTORY", "Thông tin không hợp lệ: nhập đủ thông tin, lý do tối thiểu 5 ký tự; số lượng không âm và tối đa 3 chữ số thập phân.");
    }

    public async Task<InventoryItemDto> UpdateAsync(string id, UpdateInventoryDto request, string actorId, string actorName, CancellationToken ct = default)
    {
        ValidateValues(request, actorId);
        if (string.IsNullOrWhiteSpace(id) || id.Length > 100)
            throw new DomainValidationException("INVALID_INVENTORY_ID", "Mã vật tư không hợp lệ.");
        var before = await repository.GetAsync(id, ct) ?? throw new InventoryNotFoundException();
        if (before.Revision != request.ExpectedRevision)
            throw new InventoryConflictException("Lô đã được thay đổi. Vui lòng tải lại dữ liệu mới nhất trước khi lưu.");
        var next = new Reagent { Id = before.Id, Code = before.Code, LotNumber = before.LotNumber, Unit = before.Unit,
            Name = request.Name.Trim(), Manufacturer = request.Manufacturer.Trim(), Quantity = request.Quantity,
            MinimumStock = request.MinimumStock, ExpiryDate = DateTime.SpecifyKind(request.ExpiryDate!.Value.Date, DateTimeKind.Utc) };
        var updated = await repository.UpdateAsync(next, request.ExpectedRevision,
            Change("UPDATE", actorId, actorName, request.Reason, before, next), ct)
            ?? throw new InventoryConflictException("Lô vừa được thay đổi. Vui lòng tải lại trước khi lưu.");
        return Map(updated, Today);
    }

    private InventoryChange Change(string action, string actorId, string actorName, string reason, Reagent? before, Reagent? after) =>
        new() { Action = action, ActorId = actorId, ActorName = actorName, Reason = reason.Trim(), At = clock.GetUtcNow().UtcDateTime,
            Before = Snapshot(before), After = Snapshot(after) };
    private static InventorySnapshot? Snapshot(Reagent? item) => item is null ? null :
        new(item.Name, item.Manufacturer, item.Quantity, item.MinimumStock, item.ExpiryDate);

    public async Task DeleteAsync(string id, DeleteInventoryDto request, string actorId, string actorName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(actorId)) throw new ForbiddenAccessException();
        if (string.IsNullOrWhiteSpace(id) || id.Length > 100 || request.ExpectedRevision < 0 ||
            (request.Reason?.Trim().Length ?? 0) < 5 || request.Reason!.Length > 500)
            throw new DomainValidationException("INVALID_INVENTORY_DELETE", "Nhập lý do xóa từ 5 đến 500 ký tự và phiên bản hợp lệ.");
        var before = await repository.GetAsync(id, ct) ?? throw new InventoryNotFoundException();
        if (before.Revision != request.ExpectedRevision)
            throw new InventoryConflictException("Lô đã được thay đổi. Vui lòng tải lại trước khi xóa.");
        if (before.Quantity != 0)
            throw new InventoryConflictException("Chỉ được xóa lô có tồn kho bằng 0. Vui lòng kiểm kê và điều chỉnh tồn trước.");
        if (!await repository.DeleteAsync(id, request.ExpectedRevision, Change("DELETE", actorId, actorName, request.Reason, before, null), ct))
            throw new InventoryConflictException("Lô vừa được thay đổi. Vui lòng tải lại trước khi xóa.");
    }

    public async Task<InventoryListDto> ListAsync(InventoryFilterDto filter, CancellationToken ct = default)
    {
        if (filter.Page < 1 || filter.Page > 10000 || filter.PageSize < 1 || filter.PageSize > 100 ||
            filter.Search?.Length > 100 ||
            (!string.IsNullOrEmpty(filter.Status) && filter.Status is not ("LOW_STOCK" or "EXPIRING_SOON" or "EXPIRED" or "AVAILABLE" or "UNKNOWN_EXPIRY")) ||
            (filter.ExpiryFrom.HasValue && filter.ExpiryTo.HasValue && filter.ExpiryFrom.Value.Date > filter.ExpiryTo.Value.Date) ||
            filter.ExpiryTo?.Date == DateTime.MaxValue.Date)
            throw new DomainValidationException("INVALID_INVENTORY_FILTER", "Bộ lọc không hợp lệ. Ngày bắt đầu phải trước hoặc bằng ngày kết thúc.");
        var today = Today;
        var result = await repository.ListAsync(new(filter.Search?.Trim(), filter.Status,
            filter.ExpiryFrom?.Date, filter.ExpiryTo?.Date, filter.Page, filter.PageSize), today, ct);
        return new(result.Items.Select(x => Map(x, today)).ToList(),
            new(result.Total, result.LowStock, result.ExpiringSoon, result.Expired),
            filter.Page, filter.PageSize, result.Total, (int)Math.Ceiling((double)result.Total / filter.PageSize), today);
    }

    public async Task<InventoryDetailDto?> GetAsync(string id, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 100)
            throw new DomainValidationException("INVALID_INVENTORY_ID", "Mã vật tư không hợp lệ.");
        var item = await repository.GetAsync(id.Trim(), ct);
        if (item is null) return null;
        var (transactions, total) = await repository.GetTransactionsAsync(item.Code, ct);
        var today = Today;
        return new(Map(item, today), transactions.Select(x => new InventoryTransactionDto(x.Id,
            x.Type, x.Quantity, x.UnitPrice, x.Department, x.Timestamp)).ToList(), total, today,
            item.Changes.OrderByDescending(x => x.At).Select(x => new InventoryChangeDto(x.Action, x.ActorName,
                x.Reason, x.At, x.Before?.Quantity, x.After?.Quantity)).ToList());
    }

    private static InventoryItemDto Map(Reagent item, DateTime today)
    {
        // Printed expiry dates remain valid through that calendar day. BR-WAR-01 warns when fewer than 30 days remain.
        var expiry = item.ExpiryDate?.Date;
        var status = expiry is null ? "UNKNOWN_EXPIRY" : expiry < today ? "EXPIRED"
            : expiry < today.AddDays(30) ? "EXPIRING_SOON" : "AVAILABLE";
        return new(item.Id, item.Code, item.Name, item.Manufacturer, item.LotNumber,
            item.Quantity, item.Unit, item.MinimumStock, expiry, status, item.Quantity <= item.MinimumStock, item.Revision);
    }
}
