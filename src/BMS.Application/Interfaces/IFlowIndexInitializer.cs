namespace BMS.Application.Interfaces;

/// <summary>
/// Creates (idempotently) the MongoDB indexes the main lab flow relies on: unique codes (Q-, PAT-, SMP-,
/// order numbers) and the search fields of INV-5 (Status, PatientId, OrderNumber, CreatedDate).
/// </summary>
public interface IFlowIndexInitializer
{
    /// <summary>Creates missing indexes; existing ones with the same definition are left untouched.</summary>
    Task EnsureAsync(CancellationToken ct);
}
