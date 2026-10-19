using BMS.Application.DTOs.Appointment;
using BMS.Application.Interfaces;
using BMS.Core.Entities;
using BMS.Core.Interfaces;
using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace BMS.Application.Services
{
    public class AppointmentService : IAppointmentService
    {
        private const int DefaultSlotDurationMinutes = 30;
        private const int DefaultMaxPatientsPerSlot = 1;

        private readonly IAppointmentRepository _repository;

        public AppointmentService(IAppointmentRepository repository)
        {
            _repository = repository;
        }

        /// <summary>
        /// Books using BMS.Core.Entities.Appointment (StartTime/EndTime, status Pending).
        /// Capacity from doctor_shifts (shift_start/shift_end or shift_type defaults).
        /// </summary>
        public async Task<BookAppointmentResponse> BookAsync(
            BookAppointmentRequest request,
            string currentUserId,
            CancellationToken ct = default)
        {
            if (!TryParseSlot(request.SlotTime, out var startTime))
                throw new InvalidOperationException("MSG-ERR-APPT-04: SlotTime không hợp lệ (HH:mm).");

            var endTime = startTime.Add(TimeSpan.FromMinutes(DefaultSlotDurationMinutes));
            var appointmentDateTime = request.AppointmentDate.Date.Add(startTime);

            if (appointmentDateTime <= DateTime.UtcNow.AddMinutes(-5))
                throw new InvalidOperationException("MSG-ERR-APPT-01: Không thể đặt lịch trong quá khứ.");

            if (request.AppointmentDate.Date > DateTime.UtcNow.Date.AddDays(30))
                throw new InvalidOperationException("MSG-ERR-APPT-02: Chỉ được đặt lịch trong vòng 30 ngày tới.");

            var shifts = await _repository.GetDoctorShiftsAsync(request.DoctorId, request.AppointmentDate, ct);
            if (shifts.Count == 0)
                throw new InvalidOperationException("MSG-ERR-APPT-03: Bác sĩ không có lịch làm việc vào ngày này.");

            var windows = BuildTimeWindowsFromShifts(shifts);
            if (!IsSlotInsideWindows(request.SlotTime, windows, DefaultSlotDurationMinutes))
                throw new InvalidOperationException("MSG-ERR-APPT-04: Khung giờ đã chọn không nằm trong giờ làm việc của bác sĩ.");

            var slotCount = await _repository.CountByDoctorDateStartAsync(
                request.DoctorId, request.AppointmentDate, startTime, ct);
            if (slotCount >= DefaultMaxPatientsPerSlot)
                throw new InvalidOperationException("MSG-ERR-APPT-05: Khung giờ này đã đầy. Vui lòng chọn khung giờ khác.");

            var dayMax = shifts.Sum(s => s.MaxPatients);
            var dayCount = await _repository.CountByDoctorAndDateAsync(request.DoctorId, request.AppointmentDate, ct);
            if (dayCount >= dayMax)
                throw new InvalidOperationException("MSG-ERR-APPT-05: Ca làm việc của bác sĩ đã đầy. Vui lòng chọn ngày khác.");

            var doctor = await _repository.GetDoctorAsync(request.DoctorId, ct);
            var code = await _repository.GenerateNextCodeAsync(request.AppointmentDate, ct);

            var type = Enum.TryParse<AppointmentType>(request.Type, true, out var t)
                ? t
                : AppointmentType.ClinicVisit;

            // Entity only has singular packageId — take the first selected package.
            // Extra package ids (if UI sends many) can be noted until multi-package is modeled.
            var primaryPackage = request.PackageIds.FirstOrDefault();
            var notes = request.Notes;
            if (request.PackageIds.Count > 1)
            {
                var extra = string.Join(", ", request.PackageIds.Skip(1));
                notes = string.IsNullOrWhiteSpace(notes)
                    ? $"Additional packages: {extra}"
                    : $"{notes}\nAdditional packages: {extra}";
            }

            var appointment = new Appointment
            {
                AppointmentCode = code,
                PatientId = request.PatientId,
                DoctorId = request.DoctorId,
                PackageId = primaryPackage,
                AppointmentDate = request.AppointmentDate.Date,
                StartTime = startTime,
                EndTime = endTime,
                Type = type,
                Status = AppointmentStatus.Pending,
                Notes = notes,
                CollectionAddress = type == AppointmentType.HomePhlebotomy ? request.CollectionAddress : null,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            await _repository.InsertAsync(appointment, ct);
            return MapToResponse(appointment, doctor);
        }

        public async Task<AvailableSlotsResponse> GetAvailableSlotsAsync(
            string doctorId, DateTime date, CancellationToken ct = default)
        {
            var shifts = await _repository.GetDoctorShiftsAsync(doctorId, date, ct);
            var doctor = await _repository.GetDoctorAsync(doctorId, ct);

            if (shifts.Count == 0)
            {
                return new AvailableSlotsResponse
                {
                    DoctorId = doctorId,
                    DoctorName = doctor?.FullName ?? string.Empty,
                    Date = date.Date,
                    Slots = new List<AvailableSlotDto>()
                };
            }

            var windows = BuildTimeWindowsFromShifts(shifts);
            var booked = await _repository.GetByDoctorAndDateAsync(doctorId, date, ct);
            var bookedLookup = booked
                .GroupBy(x => FormatTime(x.StartTime))
                .ToDictionary(g => g.Key, g => g.Count());

            var dayMax = shifts.Sum(s => s.MaxPatients);
            var dayStillOpen = booked.Count < dayMax;

            var slots = new List<AvailableSlotDto>();
            foreach (var window in windows)
            {
                foreach (var slot in ExpandWindowToSlots(window, DefaultSlotDurationMinutes))
                {
                    var used = bookedLookup.GetValueOrDefault(slot, 0);
                    var remaining = DefaultMaxPatientsPerSlot - used;
                    if (!dayStillOpen) remaining = 0;
                    slots.Add(new AvailableSlotDto
                    {
                        SlotTime = slot,
                        RemainingCapacity = Math.Max(0, remaining),
                        IsAvailable = remaining > 0
                    });
                }
            }

            return new AvailableSlotsResponse
            {
                DoctorId = doctorId,
                DoctorName = doctor?.FullName ?? string.Empty,
                Date = date.Date,
                Slots = slots
            };
        }

        public async Task<List<DoctorOptionDto>> GetAvailableDoctorsAsync(
            DateTime date, string? specialty = null, CancellationToken ct = default)
        {
            var shifts = await _repository.GetShiftsByDateAsync(date, ct);
            if (shifts.Count == 0) return new List<DoctorOptionDto>();

            var doctorIds = shifts.Select(s => s.DoctorId).Distinct().ToList();
            var doctors = await _repository.GetDoctorsByIdsAsync(doctorIds, ct);

            var query = doctors.AsEnumerable();
            if (!string.IsNullOrWhiteSpace(specialty))
                query = query.Where(d => d.Specialty.Contains(specialty, StringComparison.OrdinalIgnoreCase));

            return query
                .Select(d => new DoctorOptionDto
                {
                    DoctorId = d.Id,
                    DoctorName = d.FullName,
                    Specialty = d.Specialty
                })
                .OrderBy(d => d.DoctorName)
                .ToList();
        }

        public async Task<BookAppointmentResponse?> GetByIdAsync(string appointmentId, CancellationToken ct = default)
        {
            var entity = await _repository.GetByIdAsync(appointmentId, ct);
            if (entity == null) return null;
            Doctor? doctor = null;
            if (!string.IsNullOrEmpty(entity.DoctorId))
                doctor = await _repository.GetDoctorAsync(entity.DoctorId, ct);
            return MapToResponse(entity, doctor);
        }

        private static List<string> BuildTimeWindowsFromShifts(IEnumerable<DoctorShift> shifts)
        {
            var windows = new HashSet<string>(StringComparer.Ordinal);
            foreach (var shift in shifts)
            {
                if (!string.IsNullOrWhiteSpace(shift.ShiftStart) && !string.IsNullOrWhiteSpace(shift.ShiftEnd))
                {
                    windows.Add($"{shift.ShiftStart}-{shift.ShiftEnd}");
                    continue;
                }

                switch (shift.ShiftType?.Trim().ToLowerInvariant())
                {
                    case "morning":
                        windows.Add("08:00-12:00");
                        break;
                    case "afternoon":
                        windows.Add("13:30-17:00");
                        break;
                    case "evening":
                        windows.Add("17:00-20:00");
                        break;
                    case "night":
                        windows.Add("20:00-23:00");
                        break;
                    case "fullday":
                    case "full_day":
                    case "full-day":
                        windows.Add("08:00-12:00");
                        windows.Add("13:30-17:00");
                        break;
                    default:
                        windows.Add("08:00-12:00");
                        break;
                }
            }
            return windows.OrderBy(w => w).ToList();
        }

        private static bool TryParseSlot(string slotTime, out TimeSpan time)
        {
            time = default;
            var parts = slotTime.Split(':');
            if (parts.Length != 2) return false;
            if (!int.TryParse(parts[0], out var h) || !int.TryParse(parts[1], out var m)) return false;
            if (h is < 0 or > 23 || m is < 0 or > 59) return false;
            time = new TimeSpan(h, m, 0);
            return true;
        }

        private static string FormatTime(TimeSpan t) => $"{(int)t.TotalHours:D2}:{t.Minutes:D2}";

        private static bool IsSlotInsideWindows(string slotTime, List<string> windows, int durationMinutes)
        {
            foreach (var window in windows)
            {
                if (ExpandWindowToSlots(window, durationMinutes).Contains(slotTime))
                    return true;
            }
            return false;
        }

        private static List<string> ExpandWindowToSlots(string window, int durationMinutes)
        {
            var parts = window.Split('-');
            if (parts.Length != 2) return new List<string>();
            if (!TimeSpan.TryParse(parts[0], out var start) || !TimeSpan.TryParse(parts[1], out var end))
                return new List<string>();

            var result = new List<string>();
            var current = start;
            while (current.Add(TimeSpan.FromMinutes(durationMinutes)) <= end)
            {
                result.Add(FormatTime(current));
                current = current.Add(TimeSpan.FromMinutes(durationMinutes));
            }
            return result;
        }

        private static BookAppointmentResponse MapToResponse(Appointment a, Doctor? doctor) => new()
        {
            AppointmentId = a.Id,
            AppointmentCode = a.AppointmentCode,
            PatientId = a.PatientId,
            DoctorId = a.DoctorId,
            DoctorName = doctor?.FullName ?? string.Empty,
            Specialty = doctor?.Specialty ?? string.Empty,
            AppointmentDate = a.AppointmentDate,
            SlotTime = FormatTime(a.StartTime),
            EndTime = FormatTime(a.EndTime),
            Status = a.Status.ToString(),
            Type = a.Type.ToString(),
            PackageId = a.PackageId
        };
    }
}
