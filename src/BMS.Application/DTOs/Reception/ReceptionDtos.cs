using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace BMS.Application.DTOs.Reception;

public class CallNextPatientRequest
{
    [Required(ErrorMessage = "Mã phòng khám (RoomId) là bắt buộc.")]
    public string RoomId { get; set; } = string.Empty;

    [Required(ErrorMessage = "Mã bác sĩ (DoctorId) là bắt buộc.")]
    public string DoctorId { get; set; } = string.Empty;
}

public class RecallPatientRequest
{
    [Required(ErrorMessage = "Mã cuộc hẹn (AppointmentId) là bắt buộc.")]
    public string AppointmentId { get; set; } = string.Empty;

    [Required(ErrorMessage = "Mã phòng khám (RoomId) là bắt buộc.")]
    public string RoomId { get; set; } = string.Empty;
}

public class SkipPatientRequest
{
    [Required(ErrorMessage = "Mã cuộc hẹn (AppointmentId) là bắt buộc.")]
    public string AppointmentId { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Reason { get; set; }
}

public class CompletePatientRequest
{
    [Required(ErrorMessage = "Mã cuộc hẹn (AppointmentId) là bắt buộc.")]
    public string AppointmentId { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string? Notes { get; set; }
}

public class QueueItemDto
{
    public string AppointmentId { get; set; } = string.Empty;
    public string AppointmentCode { get; set; } = string.Empty;
    public int? QueueNumber { get; set; }
    public string QueueNumberFormatted => QueueNumber.HasValue ? $"Q-{QueueNumber.Value:D3}" : "N/A";
    public string PatientId { get; set; } = string.Empty;
    public string PatientName { get; set; } = string.Empty;
    public string? PatientPhone { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? RoomId { get; set; }
    public string? DoctorId { get; set; }
    public string? DoctorName { get; set; }
    public int RecallCount { get; set; }
    public DateTime? ConsultationStartTime { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class QueueStatusResponse
{
    public string? RoomId { get; set; }
    public int WaitingCount { get; set; }
    public int InProgressCount { get; set; }
    public QueueItemDto? CurrentServing { get; set; }
    public List<QueueItemDto> WaitingList { get; set; } = new();
}
