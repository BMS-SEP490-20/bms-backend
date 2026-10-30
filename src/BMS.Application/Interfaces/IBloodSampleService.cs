using BMS.Application.DTOs.BloodSamples;

namespace BMS.Application.Interfaces
{
    /// <summary>Blood sample operations behind <c>TestOrder/BloodSample</c> (Print Barcodes #58, Validate Sample #73).</summary>
    public interface IBloodSampleService
    {
        /// <summary>Returns one page of samples and the total count.</summary>
        Task<(IReadOnlyList<BloodSampleReadDto> Items, long Total)> GetPagedAsync(int page, int pageSize, CancellationToken ct = default);
        /// <summary>Returns a sample by id or barcode, or null.</summary>
        Task<BloodSampleReadDto?> GetByIdAsync(string id, CancellationToken ct = default);
        /// <summary>Registers a sample; generates a unique barcode when none is supplied.</summary>
        Task<BloodSampleReadDto> CreateAsync(BloodSampleCreateDto dto, CancellationToken ct = default);
        /// <summary>Applies a partial update; returns false when the sample does not exist.</summary>
        Task<bool> UpdateAsync(string id, BloodSampleUpdateDto dto, CancellationToken ct = default);
        /// <summary>Soft-deletes a sample; returns false when it does not exist.</summary>
        Task<bool> DeleteAsync(string id, CancellationToken ct = default);
        /// <summary>Accepts (<c>validated</c>) or rejects (<c>rejected</c>) a specimen; returns false when not found.</summary>
        Task<bool> ValidateSampleAsync(string id, string status, string notes, string labUserId, CancellationToken ct = default);
        /// <summary>Returns the samples of one appointment / test order.</summary>
        Task<IReadOnlyList<BloodSampleReadDto>> GetBloodSampleByAppointmentIdAsync(string appointmentId, CancellationToken ct = default);
    }
}
