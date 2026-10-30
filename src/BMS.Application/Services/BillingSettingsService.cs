using System.Text.RegularExpressions;
using BMS.Application.DTOs.AuditLogs;
using BMS.Application.DTOs.Billing;
using BMS.Application.Interfaces;
using BMS.Application.Services.Billing;
using BMS.Core.Entities;
using BMS.Core.Exceptions;
using BMS.Core.Interfaces;

namespace BMS.Application.Services;

/// <inheritdoc cref="IBillingSettingsService" />
/// <remarks>Also the production <see cref="IFeePolicyProvider"/>: the fee formula reads the same rows the screens edit.</remarks>
public sealed partial class BillingSettingsService(
    IBillingSettingsRepository settings,
    IAuditLogService audit,
    TimeProvider clock) : IBillingSettingsService, IFeePolicyProvider
{
    [GeneratedRegex("^[A-Z]{2}$")]
    private static partial Regex GroupCodeFormat();

    /// <inheritdoc />
    public async Task<FeePolicy> GetAsync(CancellationToken ct = default)
    {
        var codes = await settings.GetActiveBhytGroupCodesAsync(ct);
        var fee = await settings.GetFeePolicyAsync(ct);
        return FeePolicy.From(codes, fee?.PriorityDiscountPercent ?? 0m, fee?.PriorityDiscountMaxAmount);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<BhytGroupCodeDto>> GetBhytGroupCodesAsync(CancellationToken ct = default) =>
        (await settings.GetBhytGroupCodesAsync(ct)).Select(Map).ToList();

    /// <inheritdoc />
    public async Task<BhytGroupCodeDto> UpsertBhytGroupCodeAsync(string code, UpsertBhytGroupCodeRequestDto request, Actor actor,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        // Normalize first (trim + upper), then validate (Quy trình Đợt 3A Q1).
        var normalized = BhytCard.NormalizeGroupCode(code);
        if (!GroupCodeFormat().IsMatch(normalized))
            throw new DomainValidationException("BHYT_GROUP_CODE_INVALID", "Mã nhóm đối tượng BHYT phải gồm đúng 2 chữ cái (A–Z).");
        var description = request.Description?.Trim();
        if (description is { Length: > 200 })
            throw new DomainValidationException("BHYT_GROUP_CODE_INVALID", "Mô tả tối đa 200 ký tự.");

        var before = await settings.GetBhytGroupCodeAsync(normalized, ct);
        var saved = await settings.UpsertBhytGroupCodeAsync(normalized, description, request.IsActive, actor.UserId,
            clock.GetUtcNow().UtcDateTime, ct);
        await audit.LogAsync(new CreateAuditLogDto
        {
            UserId = actor.UserId, UserName = actor.Name, UserRole = actor.Role,
            Action = before == null ? "BHYT_GROUP_CODE_CREATED" : "BHYT_GROUP_CODE_UPDATED",
            Category = "DATA_CHANGE", TargetEntity = "BhytGroupCode", EntityId = normalized,
            Details = $"{normalized}: active {(before == null ? "-" : before.IsActive.ToString())} -> {saved.IsActive}"
        }, ct);
        return Map(saved);
    }

    /// <inheritdoc />
    public async Task<PriorityDiscountSettingsDto> GetPriorityDiscountAsync(CancellationToken ct = default)
    {
        var s = await settings.GetFeePolicyAsync(ct);
        return new PriorityDiscountSettingsDto
        {
            Percent = s?.PriorityDiscountPercent ?? 0m,
            MaxAmount = s?.PriorityDiscountMaxAmount,
            UpdatedAt = s?.UpdatedAt,
            UpdatedBy = s?.UpdatedBy
        };
    }

    /// <inheritdoc />
    public async Task<PriorityDiscountSettingsDto> SetPriorityDiscountAsync(PriorityDiscountSettingsDto request, Actor actor,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Percent is < 0m or > 100m)
            throw new DomainValidationException("PRIORITY_DISCOUNT_INVALID", "Tỷ lệ giảm phải từ 0 đến 100%.");
        if (request.MaxAmount is < 0m)
            throw new DomainValidationException("PRIORITY_DISCOUNT_INVALID", "Mức giảm tối đa không được âm.");
        var before = await settings.GetFeePolicyAsync(ct);
        var saved = await settings.SaveFeePolicyAsync(request.Percent, request.MaxAmount, actor.UserId, clock.GetUtcNow().UtcDateTime, ct);
        await audit.LogAsync(new CreateAuditLogDto
        {
            UserId = actor.UserId, UserName = actor.Name, UserRole = actor.Role,
            Action = "PRIORITY_DISCOUNT_UPDATED", Category = "DATA_CHANGE", Severity = "WARNING",
            TargetEntity = "FeePolicy", EntityId = FeePolicySettings.DocumentId,
            Details = $"percent {before?.PriorityDiscountPercent ?? 0m} -> {saved.PriorityDiscountPercent}; " +
                      $"max {before?.PriorityDiscountMaxAmount?.ToString() ?? "none"} -> {saved.PriorityDiscountMaxAmount?.ToString() ?? "none"}"
        }, ct);
        return await GetPriorityDiscountAsync(ct);
    }

    private static BhytGroupCodeDto Map(BhytGroupCode c) => new()
    {
        Code = c.Code, Description = c.Description, IsActive = c.IsActive, Seeded = c.Seeded, UpdatedAt = c.UpdatedAt, UpdatedBy = c.UpdatedBy
    };
}
