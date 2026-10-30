using BMS.Application.DTOs.Specimens;

namespace BMS.Application.Interfaces;

/// <summary>
/// Specimen collection (UC37/UC39/UC40): payment gate, 2-factor patient check, one SMP- barcode per tube in
/// CLSI order of draw, order → SampleCollected exactly once.
/// </summary>
public interface ISpecimenCollectionService
{
    /// <summary>Collects; <paramref name="labUserId"/> comes from the token.</summary>
    Task<CollectSpecimenResultDto> CollectAsync(CollectSpecimenRequestDto request, string labUserId, CancellationToken ct = default);
}
