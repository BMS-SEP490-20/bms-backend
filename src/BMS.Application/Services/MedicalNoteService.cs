using BMS.Application.DTOs.Clinic;
using BMS.Application.DTOs.MedicalRecords;
using BMS.Application.Interfaces;
using BMS.Core.Entities;
using BMS.Core.Exceptions;
using BMS.Core.Interfaces;

namespace BMS.Application.Services;

/// <summary>Validates UC51 addenda against existing EMR ownership and server-derived identity.</summary>
public sealed class MedicalNoteService(
    IMedicalRecordService records, IMedicalNoteRepository notes) : IMedicalNoteService
{
    public async Task<MedicalNoteContextDto> GetContextAsync(
        string recordId, string doctorId, int page, int pageSize, CancellationToken ct = default)
    {
        if (page < 1 || page > 10000 || pageSize < 1 || pageSize > 50)
            throw new DomainValidationException("INVALID_PAGE", "Thông tin phân trang không hợp lệ.");
        var record = await GetOwnedRecordAsync(recordId, doctorId, ct);
        var (items, total) = await notes.ListAsync(record.Id, page, pageSize, ct);
        return new(record, items.Select(Map).ToList(), total, page, pageSize);
    }

    public async Task<MedicalNoteDto> AddAsync(
        CreateMedicalNoteDto request, string doctorId, string authorName, CancellationToken ct = default)
    {
        var content = request.Content?.Trim();
        if (string.IsNullOrWhiteSpace(content) || content.Length > 5000 || request.SubmissionId == Guid.Empty)
            throw new DomainValidationException("INVALID_MEDICAL_NOTE", "Ghi chú phải có nội dung từ 1 đến 5.000 ký tự và mã gửi hợp lệ.");
        var record = await GetOwnedRecordAsync(request.RecordId, doctorId, ct);
        var note = new MedicalNote
        {
            Id = request.SubmissionId.ToString("D"), RecordId = record.Id,
            PatientId = record.PatientId, Content = content, AuthorId = doctorId,
            AuthorName = string.IsNullOrWhiteSpace(authorName) ? record.DoctorName : authorName,
            CreatedAt = DateTime.UtcNow
        };
        var stored = await notes.InsertOrGetAsync(note, ct);
        if (stored.RecordId != note.RecordId || stored.AuthorId != doctorId || stored.Content != content)
            throw new MedicalNoteConflictException();
        return Map(stored);
    }

    private async Task<MedicalRecordDto> GetOwnedRecordAsync(string recordId, string doctorId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(doctorId)) throw new ForbiddenAccessException();
        if (string.IsNullOrWhiteSpace(recordId) || recordId.Length > 100)
            throw new DomainValidationException("INVALID_RECORD", "Vui lòng nhập mã bệnh án hợp lệ.");
        var record = await records.GetByIdAsync(recordId.Trim(), doctorId, "Doctor", ct)
            ?? throw new MedicalNoteRecordNotFoundException();
        // Doctor profile IDs equal account IDs; never trust an author supplied by the client.
        if (!string.Equals(record.DoctorId, doctorId, StringComparison.Ordinal))
            throw new ForbiddenAccessException("Chỉ bác sĩ phụ trách bệnh án mới có quyền xem và thêm ghi chú.");
        return record;
    }

    private static MedicalNoteDto Map(MedicalNote note) =>
        new(note.Id, note.RecordId, note.Content, note.AuthorName, note.CreatedAt);
}
