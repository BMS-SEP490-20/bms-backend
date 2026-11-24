using BMS.Core;
using BMS.Core.Entities;
using BMS.Core.Interfaces;
using MongoDB.Bson;
using MongoDB.Driver;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BMS.Infrastructure.Repositories
{
    public class AppointmentRepository : IAppointmentRepository
    {
        private readonly IMongoCollection<Appointment> _collection;
        private readonly IMongoCollection<DoctorShift> _shiftCollection;
        private readonly IMongoCollection<Doctor> _doctorCollection;

        public AppointmentRepository(IMongoDatabase database)
        {
            _collection = database.GetCollection<Appointment>("appointments");
            _shiftCollection = database.GetCollection<DoctorShift>("doctor_shifts");
            _doctorCollection = database.GetCollection<Doctor>("doctors");

            try
            {
                var indexKeys = Builders<Appointment>.IndexKeys.Ascending(x => x.AppointmentCode);
                _collection.Indexes.CreateOne(
                    new CreateIndexModel<Appointment>(indexKeys, new CreateIndexOptions { Unique = true }));

                var slotIndex = Builders<Appointment>.IndexKeys
                    .Ascending(x => x.DoctorId)
                    .Ascending(x => x.AppointmentDate)
                    .Ascending(x => x.StartTime)
                    .Ascending(x => x.Status);
                _collection.Indexes.CreateOne(new CreateIndexModel<Appointment>(slotIndex));
            }
            catch
            {
                // Ignore index build exception on dirty legacy dev database
            }
        }

        public async Task<List<Appointment>> GetAllAsync(CancellationToken ct = default)
            => await _collection.Find(_ => true)
                .SortByDescending(x => x.CreatedAt)
                .ToListAsync(ct);

        public async Task<Appointment?> GetByIdAsync(string id, CancellationToken ct = default)
            => await _collection.Find(x => x.Id == id || x.AppointmentCode == id).FirstOrDefaultAsync(ct);

        public async Task<Appointment?> GetByCodeAsync(string appointmentCode, CancellationToken ct = default)
            => await _collection.Find(x => x.AppointmentCode == appointmentCode).FirstOrDefaultAsync(ct);

        public async Task<List<Appointment>> GetByPatientIdAsync(string patientId, CancellationToken ct = default)
        {
            var filter = Builders<Appointment>.Filter.Or(
                Builders<Appointment>.Filter.Eq(x => x.PatientId, patientId),
                Builders<Appointment>.Filter.Eq("patient_id", patientId)
            );
            return await _collection
                .Find(filter)
                .SortByDescending(x => x.AppointmentDate)
                .ToListAsync(ct);
        }

        public async Task<List<Appointment>> GetByDoctorAndDateAsync(string doctorId, DateTime date, CancellationToken ct = default)
        {
            var start = date.Date;
            var end = start.AddDays(1);
            var docFilter = Builders<Appointment>.Filter.Or(
                Builders<Appointment>.Filter.Eq(x => x.DoctorId, doctorId),
                Builders<Appointment>.Filter.Eq("doctor_id", doctorId),
                ObjectId.TryParse(doctorId, out var oid) ? Builders<Appointment>.Filter.Eq("doctor_id", oid) : Builders<Appointment>.Filter.Empty
            );
            var dateFilter = Builders<Appointment>.Filter.Or(
                Builders<Appointment>.Filter.Gte(x => x.AppointmentDate, start) & Builders<Appointment>.Filter.Lt(x => x.AppointmentDate, end),
                Builders<Appointment>.Filter.Gte("appointment_date", start) & Builders<Appointment>.Filter.Lt("appointment_date", end)
            );
            return await _collection
                .Find(Builders<Appointment>.Filter.And(docFilter, dateFilter, Builders<Appointment>.Filter.Ne(x => x.Status, AppointmentStatus.Cancelled)))
                .ToListAsync(ct);
        }

        public async Task<int> CountByDoctorDateStartAsync(
            string doctorId, DateTime date, TimeSpan startTime, CancellationToken ct = default)
        {
            var (start, end) = ClinicClock.DayWindow(date);
            return (int)await _collection.CountDocumentsAsync(
                x => x.DoctorId == doctorId
                     && x.AppointmentDate >= start
                     && x.AppointmentDate < end
                     && x.StartTime == startTime
                     && x.Status != AppointmentStatus.Cancelled,
                cancellationToken: ct);
        }

        public async Task<int> CountByDoctorAndDateAsync(string doctorId, DateTime date, CancellationToken ct = default)
        {
            var (start, end) = ClinicClock.DayWindow(date);
            return (int)await _collection.CountDocumentsAsync(
                x => x.DoctorId == doctorId
                     && x.AppointmentDate >= start
                     && x.AppointmentDate < end
                     && x.Status != AppointmentStatus.Cancelled,
                cancellationToken: ct);
        }

        public async Task<string> GenerateNextCodeAsync(DateTime date, CancellationToken ct = default)
        {
            var prefix = $"APT-{date:yyMMdd}-";
            var last = await _collection
                .Find(x => x.AppointmentCode.StartsWith(prefix))
                .SortByDescending(x => x.AppointmentCode)
                .Limit(1)
                .FirstOrDefaultAsync(ct);

            var nextSeq = 1;
            if (last != null)
            {
                var seqPart = last.AppointmentCode.Split('-').LastOrDefault();
                if (int.TryParse(seqPart, out var current))
                    nextSeq = current + 1;
            }
            return $"{prefix}{nextSeq:D4}";
        }

        public async Task InsertAsync(Appointment appointment, CancellationToken ct = default)
            => await _collection.InsertOneAsync(appointment, cancellationToken: ct);

        public async Task UpdateAsync(Appointment appointment, CancellationToken ct = default)
        {
            appointment.UpdatedAt = DateTime.UtcNow;
            await _collection.ReplaceOneAsync(x => x.Id == appointment.Id, appointment, cancellationToken: ct);
        }

        public async Task UpdateStatusAsync(string id, AppointmentStatus newStatus, CancellationToken ct = default)
        {
            var update = Builders<Appointment>.Update
                .Set(x => x.Status, newStatus)
                .Set(x => x.UpdatedAt, DateTime.UtcNow);
            await _collection.UpdateOneAsync(x => x.Id == id, update, cancellationToken: ct);
        }

        public async Task<bool> TryConfirmPaymentAsync(string id, string transactionId, decimal amount, CancellationToken ct = default)
        {
            var existing = await GetByIdAsync(id, ct);
            if (existing == null) return false;
            if (existing.Status == AppointmentStatus.Confirmed && existing.PaidAt != null) return true;

            var update = Builders<Appointment>.Update
                .Set(x => x.Status, AppointmentStatus.Confirmed)
                .Set(x => x.SepayTransactionId, transactionId)
                .Set(x => x.PaidAmount, amount)
                .Set(x => x.PaidAt, DateTime.UtcNow)
                .Set(x => x.UpdatedAt, DateTime.UtcNow);
            var result = await _collection.UpdateOneAsync(
                x => x.Id == existing.Id && x.Status != AppointmentStatus.Cancelled,
                update,
                cancellationToken: ct);
            return result.ModifiedCount > 0 || result.MatchedCount > 0;
        }

        public async Task<bool> DeleteAsync(string id, CancellationToken ct = default)
        {
            var result = await _collection.DeleteOneAsync(x => x.Id == id, ct);
            return result.DeletedCount > 0;
        }

        public async Task<List<DoctorShift>> GetDoctorShiftsAsync(
            string doctorId, DateTime date, CancellationToken ct = default)
        {
            var (start, end) = ClinicClock.DayWindow(date);
            return await _shiftCollection
                .Find(x => x.DoctorId == doctorId
                           && x.ShiftDate >= start
                           && x.ShiftDate < end
                           && x.Status != "Cancelled"
                           && x.Status != "Rejected")
                .ToListAsync(ct);
        }

        public async Task<List<DoctorShift>> GetShiftsByDateAsync(DateTime date, CancellationToken ct = default)
        {
            var (start, end) = ClinicClock.DayWindow(date);
            return await _shiftCollection
                .Find(x => x.ShiftDate >= start
                           && x.ShiftDate < end
                           && x.Status != "Cancelled"
                           && x.Status != "Rejected")
                .ToListAsync(ct);
        }

        public async Task<Doctor?> GetDoctorAsync(string doctorId, CancellationToken ct = default)
            => await _doctorCollection.Find(x => x.Id == doctorId).FirstOrDefaultAsync(ct);

        public async Task<List<Doctor>> GetDoctorsByIdsAsync(
            IEnumerable<string> doctorIds, CancellationToken ct = default)
        {
            var ids = doctorIds.Where(id => MongoDB.Bson.ObjectId.TryParse(id, out _)).Distinct().ToList();
            if (ids.Count == 0) return new List<Doctor>();
            return await _doctorCollection.Find(x => ids.Contains(x.Id)).ToListAsync(ct);
        }

        // UC23: View & Filter Appointments
        public async Task<(List<Appointment> Items, long TotalCount)> GetFilteredAsync(
            DateTime? fromDate,
            DateTime? toDate,
            string? doctorId,
            AppointmentStatus? status,
            string? keyword,
            string? patientId,
            int page,
            int pageSize,
            CancellationToken ct = default)
        {
            var builder = Builders<Appointment>.Filter;
            var filter = builder.Empty;

            if (!string.IsNullOrWhiteSpace(patientId))
            {
                var tokens = patientId.Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (tokens.Length > 1)
                {
                    var orFilters = new List<FilterDefinition<Appointment>>();
                    foreach (var tok in tokens)
                    {
                        var tokRx = new MongoDB.Bson.BsonRegularExpression(System.Text.RegularExpressions.Regex.Escape(tok), "i");
                        orFilters.Add(builder.Eq(x => x.PatientId, tok));
                        orFilters.Add(builder.Regex(x => x.PatientId, tokRx));
                        orFilters.Add(Builders<Appointment>.Filter.Eq("patient_id", tok));
                        orFilters.Add(Builders<Appointment>.Filter.Regex("patient_id", tokRx));
                    }
                    filter &= builder.Or(orFilters);
                }
                else
                {
                    var cleanPat = patientId.Trim();
                    var patRx = new MongoDB.Bson.BsonRegularExpression(System.Text.RegularExpressions.Regex.Escape(cleanPat), "i");
                    filter &= builder.Or(
                        builder.Eq(x => x.PatientId, cleanPat),
                        builder.Regex(x => x.PatientId, patRx),
                        Builders<Appointment>.Filter.Eq("patient_id", cleanPat),
                        Builders<Appointment>.Filter.Regex("patient_id", patRx)
                    );
                }
            }

            if (!string.IsNullOrWhiteSpace(doctorId))
            {
                var docOr = new List<FilterDefinition<Appointment>>
                {
                    builder.Eq(x => x.DoctorId, doctorId),
                    Builders<Appointment>.Filter.Eq("doctor_id", doctorId)
                };
                if (ObjectId.TryParse(doctorId, out var docOid))
                {
                    docOr.Add(Builders<Appointment>.Filter.Eq("doctor_id", docOid));
                }
                filter &= builder.Or(docOr);
            }

            if (status.HasValue)
            {
                var strVal = status.Value.ToString();
                var intVal = (int)status.Value;
                var statusOr = new List<FilterDefinition<Appointment>>
                {
                    builder.Eq(x => x.Status, status.Value),
                    Builders<Appointment>.Filter.Eq("status", strVal),
                    Builders<Appointment>.Filter.Eq("status", intVal)
                };
                if (status.Value == AppointmentStatus.Pending)
                {
                    statusOr.Add(Builders<Appointment>.Filter.Eq("status", "Scheduled"));
                }
                else if (status.Value == AppointmentStatus.CheckedIn)
                {
                    statusOr.Add(Builders<Appointment>.Filter.Eq("status", "SampleCollected"));
                }
                filter &= builder.Or(statusOr);
            }

            if (fromDate.HasValue)
            {
                var start = fromDate.Value.Date;
                filter &= builder.Or(
                    builder.Gte(x => x.AppointmentDate, start),
                    Builders<Appointment>.Filter.Gte("appointment_date", start)
                );
            }

            if (toDate.HasValue)
            {
                var end = toDate.Value.Date.AddDays(1).AddTicks(-1);
                filter &= builder.Or(
                    builder.Lte(x => x.AppointmentDate, end),
                    Builders<Appointment>.Filter.Lte("appointment_date", end)
                );
            }

            if (!string.IsNullOrWhiteSpace(keyword))
            {
                var escaped = System.Text.RegularExpressions.Regex.Escape(keyword.Trim());
                var regex = new MongoDB.Bson.BsonRegularExpression(escaped, "i");
                var kwFilter = builder.Or(
                    builder.Regex(x => x.AppointmentCode, regex),
                    builder.Regex(x => x.Notes, regex),
                    builder.Regex(x => x.CollectionAddress, regex),
                    builder.Regex(x => x.PatientId, regex),
                    Builders<Appointment>.Filter.Regex("patient_id", regex),
                    Builders<Appointment>.Filter.Regex("appointment_code", regex)
                );
                filter &= kwFilter;
            }

            var total = await _collection.CountDocumentsAsync(filter, cancellationToken: ct);

            var items = await _collection.Find(filter)
                .SortByDescending(x => x.AppointmentDate)
                .ThenByDescending(x => x.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Limit(pageSize)
                .ToListAsync(ct);

            return (items, total);
        }

        // UC28: Smart Queue & Doctor Call
        public async Task<Appointment?> CallNextPatientAtomicAsync(string doctorId, string roomId, CancellationToken ct = default)
        {
            var vnOffset = TimeSpan.FromHours(7);
            var vnToday = (DateTime.UtcNow + vnOffset).Date;
            var startUtc = vnToday - vnOffset;
            var endUtc = vnToday.AddDays(1) - vnOffset;
            var dateOnlyStart = vnToday;
            var dateOnlyEnd = vnToday.AddDays(1);

            var fb = Builders<Appointment>.Filter;
            var filter = fb.And(
                fb.Eq(x => x.Status, AppointmentStatus.CheckedIn),
                fb.Or(
                    fb.And(fb.Gte(x => x.AppointmentDate, startUtc), fb.Lt(x => x.AppointmentDate, endUtc)),
                    fb.And(fb.Gte(x => x.AppointmentDate, dateOnlyStart), fb.Lt(x => x.AppointmentDate, dateOnlyEnd))),
                // Prefer this doctor's queue; also allow unassigned doctorId so reception can feed the desk.
                fb.Or(
                    fb.Eq(x => x.DoctorId, doctorId),
                    fb.Eq(x => x.DoctorId, null),
                    fb.Eq(x => x.DoctorId, string.Empty))
            );

            var sort = Builders<Appointment>.Sort
                .Ascending(x => x.QueueNumber)
                .Ascending(x => x.CreatedAt);

            var update = Builders<Appointment>.Update
                .Set(x => x.Status, AppointmentStatus.InProgress)
                .Set(x => x.DoctorId, doctorId)
                .Set(x => x.RoomId, roomId)
                .Set(x => x.ConsultationStartTime, DateTime.UtcNow)
                .Set(x => x.UpdatedAt, DateTime.UtcNow);

            var options = new FindOneAndUpdateOptions<Appointment>
            {
                Sort = sort,
                ReturnDocument = ReturnDocument.After
            };

            return await _collection.FindOneAndUpdateAsync(filter, update, options, ct);
        }

        public async Task<List<Appointment>> GetTodayQueueAppointmentsAsync(string? roomId, string? doctorId, CancellationToken ct = default)
        {
            // Clinic day = Vietnam (UTC+7). AppointmentDate is often stored as local calendar midnight UTC
            // (e.g. 2026-11-20T00:00:00Z for a 20/11 VN booking) — bound the window in VN then convert to UTC.
            var vnOffset = TimeSpan.FromHours(7);
            var vnNow = DateTime.UtcNow + vnOffset;
            var vnToday = vnNow.Date;
            var startUtc = vnToday - vnOffset;                 // 00:00 VN → prior day 17:00 UTC
            var endUtc = vnToday.AddDays(1) - vnOffset;        // next 00:00 VN → today 17:00 UTC
            // Also accept date-only docs stamped at midnight UTC of the VN calendar day.
            var dateOnlyStart = vnToday;
            var dateOnlyEnd = vnToday.AddDays(1);

            var filterBuilder = Builders<Appointment>.Filter;
            var filter = filterBuilder.And(
                filterBuilder.In(x => x.Status, new[] { AppointmentStatus.CheckedIn, AppointmentStatus.InProgress }),
                filterBuilder.Or(
                    filterBuilder.And(
                        filterBuilder.Gte(x => x.AppointmentDate, startUtc),
                        filterBuilder.Lt(x => x.AppointmentDate, endUtc)),
                    filterBuilder.And(
                        filterBuilder.Gte(x => x.AppointmentDate, dateOnlyStart),
                        filterBuilder.Lt(x => x.AppointmentDate, dateOnlyEnd)))
            );

            if (!string.IsNullOrWhiteSpace(doctorId))
            {
                filter &= filterBuilder.Eq(x => x.DoctorId, doctorId);
            }

            // Room filter: exact match OR unassigned room (null/empty). Strict Eq hid InProgress after reload
            // when Call-Next wrote a different/missing roomId than the Doctor console dropdown.
            if (!string.IsNullOrWhiteSpace(roomId))
            {
                filter &= filterBuilder.Or(
                    filterBuilder.Eq(x => x.RoomId, roomId),
                    filterBuilder.Eq(x => x.RoomId, null),
                    filterBuilder.Eq(x => x.RoomId, string.Empty));
            }

            return await _collection.Find(filter)
                .SortBy(x => x.Status)
                .ThenBy(x => x.QueueNumber)
                .ThenBy(x => x.CreatedAt)
                .ToListAsync(ct);
        }

        public async Task<bool> HasActiveDoctorShiftTodayAsync(string doctorId, CancellationToken ct = default)
        {
            // Match clinic day in VN (UTC+7); ShiftDate is often midnight UTC of the calendar day.
            var vnOffset = TimeSpan.FromHours(7);
            var vnToday = (DateTime.UtcNow + vnOffset).Date;
            var startUtc = vnToday - vnOffset;
            var endUtc = vnToday.AddDays(1) - vnOffset;
            var dateOnlyStart = vnToday;
            var dateOnlyEnd = vnToday.AddDays(1);

            var count = await _shiftCollection.CountDocumentsAsync(
                x => x.DoctorId == doctorId
                     && x.Status != "Cancelled"
                     && x.Status != "Rejected"
                     && ((x.ShiftDate >= startUtc && x.ShiftDate < endUtc)
                         || (x.ShiftDate >= dateOnlyStart && x.ShiftDate < dateOnlyEnd)),
                cancellationToken: ct);

            return count > 0;
        }
    }
}
