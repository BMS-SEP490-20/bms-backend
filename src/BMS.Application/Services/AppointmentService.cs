using BMS.Application.DTOs.Appointment;
using BMS.Application.Interfaces;
using BMS.Core.Entities;
using BMS.Core.Interfaces;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BMS.Application.Services
{
    public class AppointmentService : IAppointmentService
    {
        private const int DefaultSlotDurationMinutes = 30;
        private const int DefaultMaxPatientsPerSlot = 1;

        private readonly IAppointmentRepository _repository;
        private readonly ISampleRepository _sampleRepository;
        private readonly ILogger<AppointmentService> _logger;

        public AppointmentService(
            IAppointmentRepository repository,
            ISampleRepository sampleRepository,
            ILogger<AppointmentService> logger)
        {
            _repository = repository;
            _sampleRepository = sampleRepository;
            _logger = logger;
        }

        /// <summary>
        /// UC21: Books using BMS.Core.Entities.Appointment (StartTime/EndTime, status Pending).
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

            var type = ParseType(request.Type);

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
            _logger.LogInformation("✅ Appointment booked successfully: {Code}", appointment.AppointmentCode);
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

        // UC13: Manage Appointments
        public async Task<IEnumerable<AppointmentReadDto>> GetAllAsync(CancellationToken ct = default)
        {
            var list = await _repository.GetAllAsync(ct);
            var doctorIds = list.Where(a => !string.IsNullOrEmpty(a.DoctorId)).Select(a => a.DoctorId!).Distinct();
            var doctors = (await _repository.GetDoctorsByIdsAsync(doctorIds, ct)).ToDictionary(d => d.Id);

            return list.Select(a =>
            {
                doctors.TryGetValue(a.DoctorId ?? string.Empty, out var doc);
                return MapToReadDto(a, doc);
            });
        }

        public async Task<AppointmentReadDto?> GetDetailByIdAsync(string id, CancellationToken ct = default)
        {
            var a = await _repository.GetByIdAsync(id, ct);
            if (a == null) return null;
            Doctor? doctor = null;
            if (!string.IsNullOrEmpty(a.DoctorId))
                doctor = await _repository.GetDoctorAsync(a.DoctorId, ct);
            return MapToReadDto(a, doctor);
        }

        public async Task<AppointmentReadDto> CreateAsync(AppointmentCreateDto dto, CancellationToken ct = default)
        {
            var appointmentDate = dto.AppointmentDate ?? dto.AppointmentTime?.Date ?? DateTime.UtcNow.Date;
            var startTime = TimeSpan.Zero;
            if (!string.IsNullOrWhiteSpace(dto.SlotTime) && TryParseSlot(dto.SlotTime, out var parsedSlot))
            {
                startTime = parsedSlot;
            }
            else if (dto.AppointmentTime.HasValue)
            {
                startTime = dto.AppointmentTime.Value.TimeOfDay;
            }

            var endTime = startTime.Add(TimeSpan.FromMinutes(DefaultSlotDurationMinutes));
            var code = await _repository.GenerateNextCodeAsync(appointmentDate, ct);

            var entity = new Appointment
            {
                AppointmentCode = code,
                PatientId = !string.IsNullOrWhiteSpace(dto.PatientId) ? dto.PatientId : dto.CreatedBy,
                DoctorId = dto.DoctorId ?? dto.DoctorShiftId,
                PackageId = dto.PackageId ?? dto.TestPackageId,
                AppointmentDate = appointmentDate,
                StartTime = startTime,
                EndTime = endTime,
                Type = ParseType(dto.AppointmentType),
                Status = ParseStatus(dto.Status),
                Notes = dto.Notes,
                CollectionAddress = dto.CollectionAddress,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            await _repository.InsertAsync(entity, ct);
            _logger.LogInformation("✅ Created Appointment {Id} ({Code})", entity.Id, entity.AppointmentCode);

            return MapToReadDto(entity);
        }

        public async Task<bool> UpdateAsync(string id, AppointmentUpdateDto dto, CancellationToken ct = default)
        {
            var entity = await _repository.GetByIdAsync(id, ct);
            if (entity == null) return false;

            var oldStatus = entity.Status;

            if (!string.IsNullOrWhiteSpace(dto.PackageId) || !string.IsNullOrWhiteSpace(dto.TestPackageId))
                entity.PackageId = dto.PackageId ?? dto.TestPackageId;

            if (!string.IsNullOrWhiteSpace(dto.DoctorId) || !string.IsNullOrWhiteSpace(dto.DoctorShiftId))
                entity.DoctorId = dto.DoctorId ?? dto.DoctorShiftId;

            if (!string.IsNullOrWhiteSpace(dto.Status))
                entity.Status = ParseStatus(dto.Status);

            if (dto.AppointmentDate.HasValue)
                entity.AppointmentDate = dto.AppointmentDate.Value.Date;
            else if (dto.AppointmentTime.HasValue)
                entity.AppointmentDate = dto.AppointmentTime.Value.Date;

            if (!string.IsNullOrWhiteSpace(dto.SlotTime) && TryParseSlot(dto.SlotTime, out var st))
            {
                entity.StartTime = st;
                entity.EndTime = st.Add(TimeSpan.FromMinutes(DefaultSlotDurationMinutes));
            }
            else if (dto.AppointmentTime.HasValue)
            {
                entity.StartTime = dto.AppointmentTime.Value.TimeOfDay;
                entity.EndTime = entity.StartTime.Add(TimeSpan.FromMinutes(DefaultSlotDurationMinutes));
            }

            if (!string.IsNullOrWhiteSpace(dto.AppointmentType))
                entity.Type = ParseType(dto.AppointmentType);

            if (!string.IsNullOrWhiteSpace(dto.Notes) || !string.IsNullOrWhiteSpace(dto.ReasonForRescheduling))
                entity.Notes = dto.Notes ?? dto.ReasonForRescheduling;

            if (!string.IsNullOrWhiteSpace(dto.CollectionAddress))
                entity.CollectionAddress = dto.CollectionAddress;

            entity.UpdatedAt = DateTime.UtcNow;
            await _repository.UpdateAsync(entity, ct);

            // Business logic: if transitioned to CheckedIn, auto create sample
            if (oldStatus != AppointmentStatus.CheckedIn && entity.Status == AppointmentStatus.CheckedIn)
            {
                try
                {
                    var random = new Random();
                    var sample = new Sample
                    {
                        Id = Guid.NewGuid().ToString(),
                        Barcode = $"SA{random.Next(0, 999999):D6}",
                        TestOrderId = entity.Id,
                        PatientId = entity.PatientId,
                        Status = SampleStatus.Received,
                        CollectedAt = DateTime.UtcNow,
                        ReceivedAt = DateTime.UtcNow,
                        Notes = "Auto-generated after patient checked in"
                    };

                    await _sampleRepository.CreateAsync(sample, ct);
                    _logger.LogInformation("✅ Auto-generated sample {Barcode} for checked-in appointment {Id}", sample.Barcode, entity.Id);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ Failed to create auto sample on check-in for appointment {Id}", entity.Id);
                }
            }

            return true;
        }

        public async Task<bool> DeleteAsync(string id, CancellationToken ct = default)
        {
            var entity = await _repository.GetByIdAsync(id, ct);
            if (entity == null) return false;

            return await _repository.DeleteAsync(entity.Id, ct);
        }

        public async Task<IEnumerable<AppointmentReadDto>> GetByPatientIdAsync(string patientId, CancellationToken ct = default)
        {
            var list = await _repository.GetByPatientIdAsync(patientId, ct);
            var doctorIds = list.Where(a => !string.IsNullOrEmpty(a.DoctorId)).Select(a => a.DoctorId!).Distinct();
            var doctors = (await _repository.GetDoctorsByIdsAsync(doctorIds, ct)).ToDictionary(d => d.Id);

            return list.Select(a =>
            {
                doctors.TryGetValue(a.DoctorId ?? string.Empty, out var doc);
                return MapToReadDto(a, doc);
            });
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

        private static AppointmentStatus ParseStatus(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return AppointmentStatus.Pending;
            var norm = raw.Trim().ToLowerInvariant();
            return norm switch
            {
                "checked_in" or "checkedin" => AppointmentStatus.CheckedIn,
                "confirmed" => AppointmentStatus.Confirmed,
                "inprogress" or "in_progress" => AppointmentStatus.InProgress,
                "completed" => AppointmentStatus.Completed,
                "cancelled" => AppointmentStatus.Cancelled,
                "noshow" or "no_show" => AppointmentStatus.NoShow,
                _ => Enum.TryParse<AppointmentStatus>(raw, true, out var s) ? s : AppointmentStatus.Pending
            };
        }

        private static AppointmentType ParseType(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return AppointmentType.ClinicVisit;
            var norm = raw.Trim().ToLowerInvariant();
            return norm switch
            {
                "homephlebotomy" or "home_phlebotomy" or "home" => AppointmentType.HomePhlebotomy,
                _ => AppointmentType.ClinicVisit
            };
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

        private static AppointmentReadDto MapToReadDto(Appointment a, Doctor? doctor = null) => new()
        {
            AppointmentId = a.Id,
            AppointmentCode = a.AppointmentCode,
            TestPackageId = a.PackageId,
            PackageId = a.PackageId,
            DoctorShiftId = null,
            DoctorId = a.DoctorId,
            DoctorName = doctor?.FullName ?? string.Empty,
            Specialty = doctor?.Specialty ?? string.Empty,
            Status = a.Status.ToString(),
            CreatedBy = a.PatientId,
            PatientId = a.PatientId,
            CreatedAt = a.CreatedAt,
            AppointmentDate = a.AppointmentDate,
            AppointmentTime = a.AppointmentDate.Date.Add(a.StartTime),
            SlotTime = FormatTime(a.StartTime),
            EndTime = FormatTime(a.EndTime),
            AppointmentType = a.Type.ToString(),
            Notes = a.Notes,
            CollectionAddress = a.CollectionAddress
        };
    }
}
