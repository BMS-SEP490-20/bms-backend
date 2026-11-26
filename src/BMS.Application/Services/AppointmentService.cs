using BMS.Application.DTOs.Appointment;
using BMS.Application.DTOs.Common;
using BMS.Application.Interfaces;
using BMS.Core;
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
        private const int DefaultMaxPatientsPerSlot = 5;

        private readonly IAppointmentRepository _repository;
        private readonly ISampleRepository _sampleRepository;
        private readonly IInvoiceRepository _invoiceRepository;
        private readonly ITestPackageRepository _packageRepository;
        private readonly IPatientRepository _patientRepository;
        private readonly ILogger<AppointmentService> _logger;

        public AppointmentService(
            IAppointmentRepository repository,
            ISampleRepository sampleRepository,
            IInvoiceRepository invoiceRepository,
            ITestPackageRepository packageRepository,
            IPatientRepository patientRepository,
            ILogger<AppointmentService> logger)
        {
            _repository = repository;
            _sampleRepository = sampleRepository;
            _invoiceRepository = invoiceRepository;
            _packageRepository = packageRepository;
            _patientRepository = patientRepository;
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
            var vnNow = DateTime.UtcNow.AddHours(7);

            if (appointmentDateTime <= vnNow.AddMinutes(-5))
                throw new InvalidOperationException("MSG-ERR-APPT-01: Không thể đặt lịch trong quá khứ.");

            if (request.AppointmentDate.Date > vnNow.Date.AddDays(30))
                throw new InvalidOperationException("MSG-ERR-APPT-02: Chỉ được đặt lịch trong vòng 30 ngày tới.");

            var shifts = await _repository.GetDoctorShiftsAsync(request.DoctorId, request.AppointmentDate, ct);
            if (shifts.Count == 0)
                throw new InvalidOperationException("MSG-ERR-APPT-04: Bác sĩ không có ca trực vào ngày đã chọn.");

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

            var duty = shifts.FirstOrDefault(s => ShiftCovers(startTime, s)) ?? shifts[0];
            var clinicDate = ClinicClock.CalendarDate(request.AppointmentDate);
            var doctor = await _repository.GetDoctorAsync(request.DoctorId, ct);
            var code = await _repository.GenerateNextCodeAsync(clinicDate, ct);

            var type = ParseType(request.Type);

            var primaryPackage = request.PackageIds.FirstOrDefault();
            var resolvedPackage = string.IsNullOrWhiteSpace(primaryPackage)
                ? null
                : await _packageRepository.GetByIdOrCodeAsync(primaryPackage, includeInactive: true, ct);
            var canonicalPackageCode = !string.IsNullOrWhiteSpace(resolvedPackage?.PackageCode)
                ? resolvedPackage.PackageCode.Trim().ToUpperInvariant()
                : primaryPackage;

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
                PackageId = canonicalPackageCode,
                PackageCode = canonicalPackageCode,
                AppointmentDate = clinicDate,
                RoomId = string.IsNullOrWhiteSpace(duty.RoomId) ? null : duty.RoomId,
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

            // Auto-create initial unpaid billing invoice for the booked package (allows Patient & Receptionist to track payment)
            try
            {
                var pkg = resolvedPackage;
                var patient = await _patientRepository.GetByIdOrCodeAsync(appointment.PatientId, ct);
                var pkgPrice = pkg?.Price ?? 0m;
                var pkgName = pkg?.PackageName ?? "Gói xét nghiệm cận lâm sàng";

                var invoice = new Invoice
                {
                    InvoiceCode = $"INV-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..6].ToUpper()}",
                    PatientId = appointment.PatientId,
                    PatientName = patient?.FullName ?? "Bệnh nhân",
                    PatientPhone = patient?.PhoneNumber ?? string.Empty,
                    AppointmentId = appointment.Id,
                    Items = new List<InvoiceItem>
                    {
                        new()
                        {
                            ItemCode = pkg?.PackageCode ?? primaryPackage ?? "SRV-PKG",
                            ItemName = pkgName,
                            Quantity = 1,
                            UnitPrice = pkgPrice
                        }
                    },
                    SubTotal = pkgPrice,
                    DiscountAmount = 0m,
                    TotalAmount = pkgPrice,
                    Status = InvoiceStatus.Unpaid,
                    PaymentMethod = PaymentMethod.BankingQR,
                    CreatedAt = DateTime.UtcNow
                };

                await _invoiceRepository.CreateAsync(invoice, ct);
                _logger.LogInformation("🧾 Initial invoice created for appointment {Code}: {InvCode}", appointment.AppointmentCode, invoice.InvoiceCode);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to create auto-invoice for appointment {Code}", appointment.AppointmentCode);
            }

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

            var vnNow = DateTime.UtcNow.AddHours(7);
            var isTodayVn = date.Date == vnNow.Date;
            var isPastDayVn = date.Date < vnNow.Date;

            var slots = new List<AvailableSlotDto>();
            foreach (var window in windows)
            {
                foreach (var slot in ExpandWindowToSlots(window, DefaultSlotDurationMinutes))
                {
                    var used = bookedLookup.GetValueOrDefault(slot, 0);
                    var remaining = DefaultMaxPatientsPerSlot - used;
                    if (!dayStillOpen || isPastDayVn) remaining = 0;
                    if (isTodayVn && TryParseSlot(slot, out var slotTs) && date.Date.Add(slotTs) <= vnNow.AddMinutes(-5))
                    {
                        remaining = 0;
                    }
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
            if (shifts.Count > 0)
            {
                // Only consider clinical doctor shifts (exclude Lab Technicians, Receptionists, Cashiers)
                var doctorShifts = shifts.Where(s =>
                    string.IsNullOrWhiteSpace(s.EmployeeRole) ||
                    string.Equals(s.EmployeeRole, "Doctor", StringComparison.OrdinalIgnoreCase)
                ).ToList();

                var doctorIds = doctorShifts.Select(s => s.DoctorId).Distinct().ToList();
                var doctors = (await _repository.GetDoctorsByIdsAsync(doctorIds, ct)).ToDictionary(d => d.Id);

                var list = new List<DoctorOptionDto>();
                foreach (var s in doctorShifts)
                {
                    doctors.TryGetValue(s.DoctorId, out var doc);
                    var effectiveSpec = FirstText(doc?.Specialty, s.Department);

                    if (!string.IsNullOrWhiteSpace(specialty))
                    {
                        var specMatch = !string.IsNullOrWhiteSpace(effectiveSpec) &&
                            (effectiveSpec.Contains(specialty, StringComparison.OrdinalIgnoreCase) ||
                             specialty.Contains(effectiveSpec, StringComparison.OrdinalIgnoreCase) ||
                             effectiveSpec.Contains("Tổng quát", StringComparison.OrdinalIgnoreCase));

                        if (!specMatch) continue;
                    }

                    list.Add(MapShiftOption(s, doc));
                }
                return list;
            }

            return new List<DoctorOptionDto>();
        }

        private static DoctorOptionDto MapShiftOption(DoctorShift shift, Doctor? doctor)
        {
            var shiftType = string.IsNullOrWhiteSpace(shift.ShiftType) ? "Morning" : shift.ShiftType;
            var (defaultStart, defaultEnd) = shiftType.Trim().ToLowerInvariant() switch
            {
                "afternoon" => ("13:00", "17:00"),
                "evening" => ("17:30", "20:30"),
                "fullday" or "full_day" or "full-day" => ("07:00", "17:00"),
                _ => ("07:00", "11:30")
            };

            return new DoctorOptionDto
            {
                DoctorId = shift.DoctorId ?? string.Empty,
                DoctorName = FirstText(doctor?.FullName, shift.EmployeeName),
                Specialty = FirstText(doctor?.Specialty, shift.Department),
                ShiftType = shiftType,
                ShiftStart = FirstText(shift.ShiftStart, defaultStart),
                ShiftEnd = FirstText(shift.ShiftEnd, defaultEnd),
                RoomId = shift.RoomId ?? string.Empty,
                RoomName = shift.RoomName ?? string.Empty,
                RoomNumber = RoomNumberFromId(shift.RoomId),
                Floor = string.Empty
            };
        }

        private static string FirstText(params string?[] values)
        {
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value)) return value.Trim();
            }
            return string.Empty;
        }

        private static string RoomNumberFromId(string? roomId)
        {
            if (string.IsNullOrWhiteSpace(roomId)) return string.Empty;
            var parts = roomId.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return parts.Length > 1 ? parts[^1] : string.Empty;
        }

        public async Task<BookAppointmentResponse?> GetByIdAsync(string appointmentId, CancellationToken ct = default)
        {
            var entity = await _repository.GetByIdAsync(appointmentId, ct);
            if (entity == null) return null;
            Doctor? doctor = null;
            if (!string.IsNullOrEmpty(entity.DoctorId))
                doctor = await _repository.GetDoctorAsync(entity.DoctorId, ct);
            var res = MapToResponse(entity, doctor);
            if (!string.IsNullOrEmpty(entity.PatientId))
            {
                var pat = await _patientRepository.GetByIdOrCodeAsync(entity.PatientId, ct)
                       ?? await _patientRepository.GetByAccountIdAsync(entity.PatientId, ct);
                if (pat != null && !string.IsNullOrEmpty(pat.FullName))
                {
                    res.PatientName = pat.FullName;
                }
            }
            return res;
        }

        // UC23: View Appointments (Filter & Paged)
        public async Task<PagedResult<AppointmentReadDto>> GetFilteredAsync(
            AppointmentFilterRequest filter,
            string? currentUserId,
            string? currentUserRole,
            CancellationToken ct = default)
        {
            var effectivePatientId = filter.PatientId;
            if (string.Equals(currentUserRole, "Patient", StringComparison.OrdinalIgnoreCase))
            {
                var candidateIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (!string.IsNullOrWhiteSpace(currentUserId)) candidateIds.Add(currentUserId);
                if (!string.IsNullOrWhiteSpace(filter.PatientId)) candidateIds.Add(filter.PatientId);

                if (!string.IsNullOrWhiteSpace(currentUserId))
                {
                    var pat = await _patientRepository.GetByAccountIdAsync(currentUserId, ct)
                           ?? await _patientRepository.GetByIdOrCodeAsync(currentUserId, ct);
                    if (pat != null)
                    {
                        if (!string.IsNullOrWhiteSpace(pat.Id)) candidateIds.Add(pat.Id);
                        if (!string.IsNullOrWhiteSpace(pat.PatientCode)) candidateIds.Add(pat.PatientCode);
                        if (!string.IsNullOrWhiteSpace(pat.IdentityCardNumber)) candidateIds.Add(pat.IdentityCardNumber);
                    }
                }
                effectivePatientId = candidateIds.Count > 0 ? string.Join(",", candidateIds) : currentUserId;
            }
            else if (!string.IsNullOrWhiteSpace(filter.PatientId))
            {
                var candidateIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { filter.PatientId.Trim() };
                var pat = await _patientRepository.GetByIdOrCodeAsync(filter.PatientId.Trim(), ct)
                       ?? await _patientRepository.GetByAccountIdAsync(filter.PatientId.Trim(), ct);
                if (pat != null)
                {
                    if (!string.IsNullOrWhiteSpace(pat.Id)) candidateIds.Add(pat.Id);
                    if (!string.IsNullOrWhiteSpace(pat.PatientCode)) candidateIds.Add(pat.PatientCode);
                    if (!string.IsNullOrWhiteSpace(pat.AccountId)) candidateIds.Add(pat.AccountId);
                }
                effectivePatientId = string.Join(",", candidateIds);
            }

            AppointmentStatus? status = null;
            if (!string.IsNullOrWhiteSpace(filter.Status))
            {
                status = ParseStatus(filter.Status);
            }

            var (items, total) = await _repository.GetFilteredAsync(
                filter.FromDate,
                filter.ToDate,
                filter.DoctorId,
                status,
                filter.Keyword,
                effectivePatientId,
                filter.Page,
                filter.PageSize,
                ct);

            var doctorIds = items.Where(a => !string.IsNullOrEmpty(a.DoctorId)).Select(a => a.DoctorId!).Distinct();
            var doctors = (await _repository.GetDoctorsByIdsAsync(doctorIds, ct)).ToDictionary(d => d.Id);

            var patientIds = items.Where(a => !string.IsNullOrEmpty(a.PatientId)).Select(a => a.PatientId!).Distinct();
            var patientsDict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pid in patientIds)
            {
                var pat = await _patientRepository.GetByIdOrCodeAsync(pid, ct)
                       ?? await _patientRepository.GetByAccountIdAsync(pid, ct);
                if (pat != null && !string.IsNullOrEmpty(pat.FullName))
                {
                    patientsDict[pid] = pat.FullName;
                }
            }

            var packageNamesDict = await ResolvePackageNamesAsync(items.Select(a => a.PackageId), ct);

            var dtos = new List<AppointmentReadDto>();
            foreach (var a in items)
            {
                doctors.TryGetValue(a.DoctorId ?? string.Empty, out var doc);
                var dto = MapToReadDto(a, doc);
                if (!string.IsNullOrEmpty(a.PatientId) && patientsDict.TryGetValue(a.PatientId, out var pName))
                {
                    dto.PatientName = pName;
                }
                if (!string.IsNullOrWhiteSpace(a.PackageId) && packageNamesDict.TryGetValue(a.PackageId, out var pkgName))
                {
                    dto.TestPackageName = pkgName;
                }
                await FillDutyFromShiftAsync(a, dto, ct);
                dtos.Add(dto);
            }

            return new PagedResult<AppointmentReadDto>(dtos, filter.Page, filter.PageSize, total);
        }

        // UC13: Manage Appointments
        public async Task<IEnumerable<AppointmentReadDto>> GetAllAsync(CancellationToken ct = default)
        {
            var list = (await _repository.GetAllAsync(ct)).ToList();
            var doctorIds = list.Where(a => !string.IsNullOrEmpty(a.DoctorId)).Select(a => a.DoctorId!).Distinct();
            var doctors = (await _repository.GetDoctorsByIdsAsync(doctorIds, ct)).ToDictionary(d => d.Id);

            var patientIds = list.Where(a => !string.IsNullOrEmpty(a.PatientId)).Select(a => a.PatientId!).Distinct();
            var patientsDict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pid in patientIds)
            {
                var pat = await _patientRepository.GetByIdOrCodeAsync(pid, ct)
                       ?? await _patientRepository.GetByAccountIdAsync(pid, ct);
                if (pat != null && !string.IsNullOrEmpty(pat.FullName))
                {
                    patientsDict[pid] = pat.FullName;
                }
            }

            var packageNamesDict = await ResolvePackageNamesAsync(list.Select(a => a.PackageId), ct);

            return list.Select(a =>
            {
                doctors.TryGetValue(a.DoctorId ?? string.Empty, out var doc);
                var dto = MapToReadDto(a, doc);
                if (!string.IsNullOrEmpty(a.PatientId) && patientsDict.TryGetValue(a.PatientId, out var pName))
                {
                    dto.PatientName = pName;
                }
                if (!string.IsNullOrWhiteSpace(a.PackageId) && packageNamesDict.TryGetValue(a.PackageId, out var pkgName))
                {
                    dto.TestPackageName = pkgName;
                }
                return dto;
            });
        }

        public async Task<AppointmentReadDto?> GetDetailByIdAsync(string id, CancellationToken ct = default)
        {
            var a = await _repository.GetByIdAsync(id, ct);
            if (a == null) return null;
            Doctor? doctor = null;
            if (!string.IsNullOrEmpty(a.DoctorId))
                doctor = await _repository.GetDoctorAsync(a.DoctorId, ct);
            var dto = MapToReadDto(a, doctor);
            if (!string.IsNullOrEmpty(a.PatientId))
            {
                var pat = await _patientRepository.GetByIdOrCodeAsync(a.PatientId, ct)
                       ?? await _patientRepository.GetByAccountIdAsync(a.PatientId, ct);
                if (pat != null && !string.IsNullOrEmpty(pat.FullName))
                {
                    dto.PatientName = pat.FullName;
                }
            }
            if (!string.IsNullOrWhiteSpace(a.PackageId))
            {
                var pkg = await _packageRepository.GetByIdOrCodeAsync(a.PackageId, includeInactive: true, ct);
                if (pkg != null && !string.IsNullOrWhiteSpace(pkg.PackageName))
                {
                    dto.TestPackageName = pkg.PackageName;
                }
            }
            await FillDutyFromShiftAsync(a, dto, ct);
            return dto;
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

            // NOTE: Auto-creating samples on CheckedIn is intentionally REMOVED.
            // ISO 15189 workflow requires:
            //   Bước 1: KTV lấy mẫu & in Barcode (status = PendingCollection → Collected)
            //   Bước 2: Quét tiếp nhận ống mẫu (status = Collected/InTransit → Received)
            //   Bước 3: Hàng đợi mẫu bệnh phẩm (chỉ hiện khi status = Received+)
            // Mẫu chỉ được tạo tại Bước 1 bởi KTV Lab, KHÔNG tự động sinh khi bác sĩ ra y lệnh.
            // ⚠️ Code cũ dưới đây đã bị xoá để tránh lỗi logic nhảy cóc 2 bước.
            // Nếu cần tạo mẫu dự kiến (tube placeholder) → thực hiện tại SampleListService với status = PendingCollection.

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
                var sType = shift.ShiftType?.Trim().ToLowerInvariant();
                var isFullDay = sType is "fullday" or "full_day" or "full-day";

                // Nếu là ca FullDay, chia thành 2 khoảng làm việc rõ ràng tránh giờ nghỉ trưa: Sáng 07:00-11:30 & Chiều 13:00-17:00
                if (isFullDay)
                {
                    windows.Add("07:00-11:30");
                    windows.Add("13:00-17:00");
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(shift.ShiftStart) && !string.IsNullOrWhiteSpace(shift.ShiftEnd))
                {
                    windows.Add($"{shift.ShiftStart}-{shift.ShiftEnd}");
                    continue;
                }

                switch (sType)
                {
                    case "morning":
                        windows.Add("07:00-11:30");
                        break;
                    case "afternoon":
                        windows.Add("13:00-17:00");
                        break;
                    case "evening":
                        windows.Add("17:30-20:30");
                        break;
                    case "night":
                        windows.Add("20:00-23:00");
                        break;
                    default:
                        windows.Add("07:00-11:30");
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
            var norm = raw.Trim().ToLowerInvariant().Replace("_", "").Replace("-", "");
            return norm switch
            {
                "pending" or "0" => AppointmentStatus.Pending,
                "confirmed" or "scheduled" or "paid" or "1" => AppointmentStatus.Confirmed,
                "checkedin" or "checked_in" or "2" => AppointmentStatus.CheckedIn,
                "inprogress" or "in_progress" or "3" => AppointmentStatus.InProgress,
                "completed" or "4" => AppointmentStatus.Completed,
                "cancelled" or "5" => AppointmentStatus.Cancelled,
                "noshow" or "no_show" or "6" => AppointmentStatus.NoShow,
                _ => Enum.TryParse<AppointmentStatus>(raw, true, out var s) ? s : AppointmentStatus.Pending
            };
        }

        private static (string Name, string Specialty) ResolveKnownDoctor(string? doctorId)
        {
            return (doctorId ?? string.Empty) switch
            {
                "670560000000000000000002" => ("BS. CKI Nguyễn Văn Minh", "Huyết học - Truyền máu"),
                "670560000000000000000003" => ("ThS. BS Lê Văn Thành", "Nội tiết - Sinh hóa"),
                "670560000000000000000021" => ("BS. CKI Hoàng Thị Thu Trang", "Miễn dịch lâm sàng"),
                "670560000000000000000022" => ("BS. CKI Trần Đức Huy", "Đông máu & Huyết khối"),
                "670560000000000000000023" => ("BSCKII. Phạm Văn Phúc", "Đa khoa Tổng quát"),
                _ => ("BS. CKI Nguyễn Văn Minh", "Huyết học - Truyền máu")
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

        private static BookAppointmentResponse MapToResponse(Appointment a, Doctor? doctor)
        {
            var docName = doctor?.FullName;
            var spec = doctor?.Specialty;
            if (string.IsNullOrWhiteSpace(docName) || docName.StartsWith("ID:", StringComparison.OrdinalIgnoreCase))
            {
                var known = ResolveKnownDoctor(a.DoctorId);
                docName = known.Name;
                spec = known.Specialty;
            }

            return new BookAppointmentResponse
            {
                AppointmentId = a.Id,
                AppointmentCode = a.AppointmentCode,
                PatientId = a.PatientId,
                PatientName = string.Empty,
                DoctorId = a.DoctorId,
                DoctorName = docName,
                Specialty = spec ?? "Huyết học - Truyền máu",
                AppointmentDate = a.AppointmentDate,
                SlotTime = FormatTime(a.StartTime),
                EndTime = FormatTime(a.EndTime),
                Status = a.Status.ToString(),
                Type = a.Type.ToString(),
                PackageId = a.PackageId
            };
        }

        private static AppointmentReadDto MapToReadDto(Appointment a, Doctor? doctor = null)
        {
            var docName = doctor?.FullName;
            var spec = doctor?.Specialty;
            if (string.IsNullOrWhiteSpace(docName) || docName.StartsWith("ID:", StringComparison.OrdinalIgnoreCase))
            {
                var known = ResolveKnownDoctor(a.DoctorId);
                docName = known.Name;
                spec = known.Specialty;
            }

            return new AppointmentReadDto
            {
                AppointmentId = a.Id,
                AppointmentCode = a.AppointmentCode,
                TestPackageId = a.PackageId,
                PackageId = a.PackageId,
                DoctorShiftId = null,
                DoctorId = a.DoctorId,
                DoctorName = docName,
                Specialty = spec ?? "Huyết học - Truyền máu",
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
                CollectionAddress = a.CollectionAddress,
                RoomName = a.RoomId,
                QueueNumber = a.QueueNumber
            };
        }

        private async Task<Dictionary<string, string>> ResolvePackageNamesAsync(IEnumerable<string?> packageIds, CancellationToken ct)
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var rawId in packageIds.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!.Trim()).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    var pkg = await _packageRepository.GetByIdOrCodeAsync(rawId, includeInactive: true, ct);
                    if (pkg != null && !string.IsNullOrWhiteSpace(pkg.PackageName))
                    {
                        dict[rawId] = pkg.PackageName;
                    }
                }
                catch { /* non-fatal */ }
            }
            return dict;
        }

        private async Task FillDutyFromShiftAsync(Appointment appointment, AppointmentReadDto dto, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(appointment.DoctorId)) return;
            var shifts = await _repository.GetDoctorShiftsAsync(appointment.DoctorId, appointment.AppointmentDate, ct);
            var shift = shifts.FirstOrDefault(s => ShiftCovers(appointment.StartTime, s)) ?? shifts.FirstOrDefault();
            if (shift == null) return;
            if (string.IsNullOrWhiteSpace(dto.DoctorName)) dto.DoctorName = shift.EmployeeName;
            if (string.IsNullOrWhiteSpace(dto.Specialty)) dto.Specialty = shift.Department;
            if (!string.IsNullOrWhiteSpace(shift.RoomName)) dto.RoomName = shift.RoomName;
            else if (string.IsNullOrWhiteSpace(dto.RoomName)) dto.RoomName = shift.RoomId;
        }

        private static bool ShiftCovers(TimeSpan time, DoctorShift shift)
        {
            var startStr = shift.ShiftStart;
            var endStr = shift.ShiftEnd;
            if (string.IsNullOrWhiteSpace(startStr) || string.IsNullOrWhiteSpace(endStr))
            {
                var sType = shift.ShiftType?.Trim().ToLowerInvariant();
                (startStr, endStr) = sType switch
                {
                    "afternoon" => ("13:00", "17:00"),
                    "evening" => ("17:30", "20:30"),
                    "fullday" or "full_day" or "full-day" => ("07:00", "17:00"),
                    _ => ("07:00", "11:30")
                };
            }

            if (!TimeSpan.TryParse(startStr, out var start) || !TimeSpan.TryParse(endStr, out var end))
                return false;
            return time >= start && time < end;
        }
    }
}
