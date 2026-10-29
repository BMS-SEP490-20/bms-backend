using BMS.Application.Interfaces;
using BMS.Core.Entities;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace BMS.Infrastructure.Repositories;

/// <inheritdoc />
/// <remarks>WHY one place: the old repositories created indexes in their constructors (synchronously, on every
/// request scope, swallowing errors). Startup creation is idempotent and failures are visible in the log.
/// WHY per-collection try/catch: an existing database with duplicate legacy rows must not stop the API from
/// starting; the failure is logged (index name only, no data) so ops can clean the rows.</remarks>
public sealed class FlowIndexInitializer(IMongoDatabase database, ILogger<FlowIndexInitializer> logger) : IFlowIndexInitializer
{
    /// <inheritdoc />
    public async Task EnsureAsync(CancellationToken ct)
    {
        await TryAsync(QueueRepository.CollectionName, () => database.GetCollection<QueueTicket>(QueueRepository.CollectionName)
            .Indexes.CreateManyAsync(QueueRepository.Indexes(), ct));
        await TryAsync("patients", () => database.GetCollection<Patient>("patients")
            .Indexes.CreateManyAsync(PatientRepository.Indexes(), ct));
        await TryAsync(TestOrderRepository.CollectionName, () => database.GetCollection<TestOrder>(TestOrderRepository.CollectionName)
            .Indexes.CreateManyAsync(TestOrderRepository.Indexes(), ct));
        await TryAsync(SpecimenRepository.CollectionName, () => database.GetCollection<Sample>(SpecimenRepository.CollectionName)
            .Indexes.CreateManyAsync(SpecimenRepository.Indexes(), ct));
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
