using BMS.Application.Interfaces;
using BMS.Core;
using BMS.Core.Entities;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace BMS.Infrastructure.Repositories;

/// <inheritdoc />
/// <remarks>Also seeds the BHYT group-code catalog (insert-if-missing). WHY one place: the old repositories created indexes in their constructors (synchronously, on every
/// request scope, swallowing errors). Startup creation is idempotent and failures are visible in the log.
/// WHY per-collection try/catch: an existing database with duplicate legacy rows must not stop the API from
/// starting; the failure is logged (index name only, no data) so ops can clean the rows.</remarks>
public sealed class FlowIndexInitializer(IMongoDatabase database, ILogger<FlowIndexInitializer> logger) : IFlowIndexInitializer
{
    /// <inheritdoc />
    public async Task EnsureAsync(CancellationToken ct)
    {
        await TryAsync("accounts", () => database.GetCollection<User>("accounts")
            .Indexes.CreateManyAsync(UserRepository.Indexes(), ct));
        await TryAsync(QueueRepository.CollectionName, () => database.GetCollection<QueueTicket>(QueueRepository.CollectionName)
            .Indexes.CreateManyAsync(QueueRepository.Indexes(), ct));
        await TryAsync("patients", () => database.GetCollection<Patient>("patients")
            .Indexes.CreateManyAsync(PatientRepository.Indexes(), ct));
        await TryAsync(TestOrderRepository.CollectionName, () => database.GetCollection<TestOrder>(TestOrderRepository.CollectionName)
            .Indexes.CreateManyAsync(TestOrderRepository.Indexes(), ct));
        await TryAsync(SpecimenRepository.CollectionName, () => database.GetCollection<Sample>(SpecimenRepository.CollectionName)
            .Indexes.CreateManyAsync(SpecimenRepository.Indexes(), ct));
        await TryAsync(RefundRepository.CollectionName, () => database.GetCollection<Refund>(RefundRepository.CollectionName)
            .Indexes.CreateManyAsync(RefundRepository.Indexes(), ct));
        await TryAsync(IotSensorLogRepository.LogsCollectionName, () => database.GetCollection<IotSensorLog>(IotSensorLogRepository.LogsCollectionName)
            .Indexes.CreateManyAsync(IotSensorLogRepository.LogIndexes(), ct));
        await TryAsync(IotSensorLogRepository.DailySummariesCollectionName, () => database.GetCollection<IotDailySummary>(IotSensorLogRepository.DailySummariesCollectionName)
            .Indexes.CreateManyAsync(IotSensorLogRepository.DailySummaryIndexes(), ct));

        var inserted = await new BillingSettingsRepository(database)
            .SeedBhytGroupCodesAsync(BMS.Application.Services.Billing.BhytCard.SeedGroupCodes, DateTime.UtcNow, ct);
        if (inserted > 0) logger.LogInformation("Seeded {Count} BHYT group codes", inserted);

        await EnsureUpcomingDoctorShiftsAsync(ct);
    }

    private async Task EnsureUpcomingDoctorShiftsAsync(CancellationToken ct)
    {
        try
        {
            var doctors = await database.GetCollection<Doctor>("doctors").Find(_ => true).Limit(10).ToListAsync(ct);
            if (doctors.Count == 0) return;

            var shiftCol = database.GetCollection<DoctorShift>("doctor_shifts");
            var today = ClinicClock.CalendarDate(DateTime.UtcNow);
            var seeded = new List<DoctorShift>();

            for (var offset = 0; offset <= 14; offset++)
            {
                var day = today.AddDays(offset);
                var (start, end) = ClinicClock.DayWindow(day);
                var hasDoctorShift = await shiftCol.Find(s =>
                    s.ShiftDate >= start &&
                    s.ShiftDate < end &&
                    s.Status != "Cancelled" &&
                    s.Status != "Rejected" &&
                    (s.EmployeeRole == null || s.EmployeeRole == "" || s.EmployeeRole == "Doctor"))
                    .AnyAsync(ct);

                if (hasDoctorShift) continue;

                var primaryDoc = doctors[0];
                var primaryRoom = string.IsNullOrWhiteSpace(primaryDoc.RoomId) ? "ROOM-101" : primaryDoc.RoomId;
                seeded.Add(new DoctorShift
                {
                    DoctorId = primaryDoc.Id,
                    EmployeeName = primaryDoc.FullName,
                    EmployeeRole = "Doctor",
                    Department = string.IsNullOrWhiteSpace(primaryDoc.Specialty) ? "Khám Bệnh" : primaryDoc.Specialty,
                    RoomId = primaryRoom,
                    RoomName = "Phòng khám Huyết học Tổng quát 1",
                    ShiftDate = day,
                    ShiftType = "Morning",
                    ShiftStart = "07:00",
                    ShiftEnd = "11:30",
                    MaxPatients = 40,
                    Status = "Approved",
                    CreatedAt = DateTime.UtcNow
                });
                seeded.Add(new DoctorShift
                {
                    DoctorId = primaryDoc.Id,
                    EmployeeName = primaryDoc.FullName,
                    EmployeeRole = "Doctor",
                    Department = string.IsNullOrWhiteSpace(primaryDoc.Specialty) ? "Khám Bệnh" : primaryDoc.Specialty,
                    RoomId = primaryRoom,
                    RoomName = "Phòng khám Huyết học Tổng quát 1",
                    ShiftDate = day,
                    ShiftType = "Afternoon",
                    ShiftStart = "13:00",
                    ShiftEnd = "17:00",
                    MaxPatients = 40,
                    Status = "Approved",
                    CreatedAt = DateTime.UtcNow
                });

                if (doctors.Count > 1)
                {
                    var secondDoc = doctors[1];
                    var secondRoom = string.IsNullOrWhiteSpace(secondDoc.RoomId) ? "ROOM-102" : secondDoc.RoomId;
                    seeded.Add(new DoctorShift
                    {
                        DoctorId = secondDoc.Id,
                        EmployeeName = secondDoc.FullName,
                        EmployeeRole = "Doctor",
                        Department = string.IsNullOrWhiteSpace(secondDoc.Specialty) ? "Nội tiết" : secondDoc.Specialty,
                        RoomId = secondRoom,
                        RoomName = "Phòng khám Nội tiết - Chuyển hóa",
                        ShiftDate = day,
                        ShiftType = "Morning",
                        ShiftStart = "07:00",
                        ShiftEnd = "11:30",
                        MaxPatients = 35,
                        Status = "Approved",
                        CreatedAt = DateTime.UtcNow
                    });
                }
            }

            if (seeded.Count > 0)
            {
                await shiftCol.InsertManyAsync(seeded, cancellationToken: ct);
                logger.LogInformation("Ensured {Count} upcoming clinical doctor shift(s) across the next 14 days", seeded.Count);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning("Unable to ensure upcoming doctor shifts: {Message}", ex.Message);
        }
    }

    private async Task TryAsync(string collection, Func<Task> create)
    {
        try
        {
            await create();
        }
        catch (MongoCommandException ex)
        {
            logger.LogError("Index creation failed on {Collection} (code {Code}); flow uniqueness is not enforced there until fixed.",
                collection, ex.Code);
        }
    }
}
