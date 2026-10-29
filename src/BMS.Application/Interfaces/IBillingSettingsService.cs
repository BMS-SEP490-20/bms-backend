using BMS.Application.DTOs.Billing;

namespace BMS.Application.Interfaces;

/// <summary>The acting user, always taken from the token by the controller (never from the body).</summary>
public sealed record Actor(string UserId, string? Name, string Role);

/// <summary>
/// Configurable billing data (Quy trình Đợt 3A Q1/Q2): BHYT group-code catalog (Admin or Manager) and the
/// priority-discount parameters (Manager). Every change writes an audit row.
/// </summary>
public interface IBillingSettingsService
{
    /// <summary>All catalog rows.</summary>
    Task<IReadOnlyList<BhytGroupCodeDto>> GetBhytGroupCodesAsync(CancellationToken ct = default);

    /// <summary>Adds or updates one group code; the code is trimmed and upper-cased before it is validated (^[A-Z]{2}$).</summary>
    /// <exception cref="Core.Exceptions.DomainValidationException">400 BHYT_GROUP_CODE_INVALID.</exception>
    Task<BhytGroupCodeDto> UpsertBhytGroupCodeAsync(string code, UpsertBhytGroupCodeRequestDto request, Actor actor, CancellationToken ct = default);

    /// <summary>Current priority-discount parameters (defaults when never set: 0%, no cap).</summary>
    Task<PriorityDiscountSettingsDto> GetPriorityDiscountAsync(CancellationToken ct = default);

    /// <summary>Sets the priority-discount parameters (0 ≤ percent ≤ 100, cap ≥ 0 or null).</summary>
    /// <exception cref="Core.Exceptions.DomainValidationException">400 PRIORITY_DISCOUNT_INVALID.</exception>
    Task<PriorityDiscountSettingsDto> SetPriorityDiscountAsync(PriorityDiscountSettingsDto request, Actor actor, CancellationToken ct = default);
}
