using BMS.Core.Entities;
using BMS.Core.Interfaces;
using MongoDB.Driver;
using System;
using System.Collections.Generic;
using System.Text;

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

        public async Task<Appointment?> GetByIdAsync(string id, CancellationToken ct = default)
            => await _collection.Find(x => x.Id == id).FirstOrDefaultAsync(ct);

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

        public async Task UpdateStatusAsync(string id, AppointmentStatus newStatus, CancellationToken ct = default)
        {
            var update = Builders<Appointment>.Update
                .Set(x => x.Status, newStatus)
                .Set(x => x.UpdatedAt, DateTime.UtcNow);
            await _collection.UpdateOneAsync(x => x.Id == id, update, cancellationToken: ct);
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
    }
}
