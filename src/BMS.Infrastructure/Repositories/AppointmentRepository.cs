using BMS.Core.Entities;
using BMS.Core.Interfaces;
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

        public async Task<List<Appointment>> GetAllAsync(CancellationToken ct = default)
            => await _collection.Find(_ => true)
                .SortByDescending(x => x.CreatedAt)
                .ToListAsync(ct);

        public async Task<Appointment?> GetByIdAsync(string id, CancellationToken ct = default)
            => await _collection.Find(x => x.Id == id || x.AppointmentCode == id).FirstOrDefaultAsync(ct);

        public async Task<Appointment?> GetByCodeAsync(string appointmentCode, CancellationToken ct = default)
            => await _collection.Find(x => x.AppointmentCode == appointmentCode).FirstOrDefaultAsync(ct);

        public async Task<List<Appointment>> GetByPatientIdAsync(string patientId, CancellationToken ct = default)
            => await _collection
                .Find(x => x.PatientId == patientId)
                .SortByDescending(x => x.AppointmentDate)
                .ToListAsync(ct);

        public async Task<List<Appointment>> GetByDoctorAndDateAsync(string doctorId, DateTime date, CancellationToken ct = default)
        {
            var start = date.Date;
            var end = start.AddDays(1);
            return await _collection
                .Find(x => x.DoctorId == doctorId
                           && x.AppointmentDate >= start
                           && x.AppointmentDate < end
                           && x.Status != AppointmentStatus.Cancelled)
                .ToListAsync(ct);
        }

        public async Task<int> CountByDoctorDateStartAsync(
            string doctorId, DateTime date, TimeSpan startTime, CancellationToken ct = default)
        {
            var start = date.Date;
            var end = start.AddDays(1);
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
            var start = date.Date;
            var end = start.AddDays(1);
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

        public async Task<bool> DeleteAsync(string id, CancellationToken ct = default)
        {
            var result = await _collection.DeleteOneAsync(x => x.Id == id, ct);
            return result.DeletedCount > 0;
        }

        public async Task<List<DoctorShift>> GetDoctorShiftsAsync(
            string doctorId, DateTime date, CancellationToken ct = default)
        {
            var start = date.Date;
            var end = start.AddDays(1);
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
            var start = date.Date;
            var end = start.AddDays(1);
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
            var ids = doctorIds.Distinct().ToList();
            if (ids.Count == 0) return new List<Doctor>();
            return await _doctorCollection.Find(x => ids.Contains(x.Id)).ToListAsync(ct);
        }

        public async Task<Appointment?> CallNextPatientAtomicAsync(string doctorId, string roomId, CancellationToken ct = default)
        {
            var startOfToday = DateTime.UtcNow.Date;
            var endOfToday = startOfToday.AddDays(1);

            var filter = Builders<Appointment>.Filter.And(
                Builders<Appointment>.Filter.Eq(x => x.Status, AppointmentStatus.CheckedIn),
                Builders<Appointment>.Filter.Gte(x => x.AppointmentDate, startOfToday.AddDays(-1)),
                Builders<Appointment>.Filter.Lt(x => x.AppointmentDate, endOfToday)
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
            var startOfToday = DateTime.UtcNow.Date.AddDays(-1);
            var endOfToday = DateTime.UtcNow.Date.AddDays(1);

            var filterBuilder = Builders<Appointment>.Filter;
            var filter = filterBuilder.And(
                filterBuilder.In(x => x.Status, new[] { AppointmentStatus.CheckedIn, AppointmentStatus.InProgress }),
                filterBuilder.Gte(x => x.AppointmentDate, startOfToday),
                filterBuilder.Lt(x => x.AppointmentDate, endOfToday)
            );

            if (!string.IsNullOrWhiteSpace(roomId))
            {
                filter &= filterBuilder.Or(
                    filterBuilder.Eq(x => x.RoomId, roomId),
                    filterBuilder.Eq(x => x.Status, AppointmentStatus.CheckedIn)
                );
            }

            return await _collection.Find(filter)
                .SortBy(x => x.Status)
                .ThenBy(x => x.QueueNumber)
                .ThenBy(x => x.CreatedAt)
                .ToListAsync(ct);
        }

        public async Task<bool> HasActiveDoctorShiftTodayAsync(string doctorId, CancellationToken ct = default)
        {
            var startOfToday = DateTime.UtcNow.Date;
            var endOfToday = startOfToday.AddDays(1);

            var count = await _shiftCollection.CountDocumentsAsync(
                x => x.DoctorId == doctorId
                     && x.ShiftDate >= startOfToday
                     && x.ShiftDate < endOfToday
                     && x.Status != "Cancelled"
                     && x.Status != "Rejected",
                cancellationToken: ct);

            return count > 0;
        }
    }
}
