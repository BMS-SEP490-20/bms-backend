using BMS.Core.Entities;
using BMS.Core.Interfaces;
using MongoDB.Driver;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BMS.Infrastructure.Repositories;

public class InvoiceRepository : IInvoiceRepository
{
    private readonly IMongoDatabase _database;
    private readonly IMongoCollection<Invoice> _collection;

    public InvoiceRepository(IMongoDatabase database)
    {
        _database = database;
        _collection = database.GetCollection<Invoice>("invoices");

        var indexKeys = Builders<Invoice>.IndexKeys.Ascending(x => x.InvoiceCode);
        _collection.Indexes.CreateOne(new CreateIndexModel<Invoice>(indexKeys, new CreateIndexOptions { Sparse = true }));

        var receiptIndex = Builders<Invoice>.IndexKeys.Ascending(x => x.ReceiptNumber);
        _collection.Indexes.CreateOne(new CreateIndexModel<Invoice>(receiptIndex, new CreateIndexOptions { Sparse = true }));
    }

    public async Task<Invoice?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        var inv = await _collection.Find(x => x.Id == id || x.InvoiceCode == id).FirstOrDefaultAsync(ct);
        if (inv != null) await EnrichAppointmentCodesAsync([inv], ct);
        return inv;
    }

    /// <inheritdoc />
    public async Task<Invoice?> GetPaidByOrderIdAsync(string orderId, CancellationToken ct = default) =>
        await _collection.Find(x => x.OrderId == orderId && x.Status == InvoiceStatus.Paid).SortByDescending(x => x.PaidAt).FirstOrDefaultAsync(ct);

    /// <inheritdoc />
    public async Task<bool> TryMarkRefundedAsync(string invoiceId, InvoiceStatus to, CancellationToken ct = default) =>
        (await _collection.UpdateOneAsync(x => x.Id == invoiceId && x.Status == InvoiceStatus.Paid,
            Builders<Invoice>.Update.Set(x => x.Status, to), cancellationToken: ct)).ModifiedCount == 1;

    /// <inheritdoc />
    public async Task<long> VoidUnpaidByOrderIdAsync(string orderId, CancellationToken ct = default) =>
        (await _collection.UpdateManyAsync(x => x.OrderId == orderId && x.Status == InvoiceStatus.Unpaid,
            Builders<Invoice>.Update.Set(x => x.Status, InvoiceStatus.Voided), cancellationToken: ct)).ModifiedCount;

    public async Task<Invoice?> GetByInvoiceCodeAsync(string invoiceCode, CancellationToken ct = default)
    {
        return await _collection.Find(x => x.InvoiceCode == invoiceCode).FirstOrDefaultAsync(ct);
    }

    public async Task<Invoice?> GetByReceiptNumberAsync(string receiptNumber, CancellationToken ct = default)
    {
        return await _collection.Find(x => x.ReceiptNumber == receiptNumber).FirstOrDefaultAsync(ct);
    }

    public async Task<List<Invoice>> GetInvoicesAsync(
        string? patientId,
        InvoiceStatus? status,
        DateTime? fromDate,
        DateTime? toDate,
        int page = 1,
        int pageSize = 20,
        CancellationToken ct = default)
    {
        var filter = BuildFilter(patientId, status, fromDate, toDate);
        var list = await _collection.Find(filter)
            .SortByDescending(x => x.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Limit(pageSize)
            .ToListAsync(ct);
        await EnrichAppointmentCodesAsync(list, ct);
        return list;
    }

    private async Task EnrichAppointmentCodesAsync(List<Invoice> invoices, CancellationToken ct)
    {
        if (invoices.Count == 0) return;
        try
        {
            var apptCol = _database.GetCollection<MongoDB.Bson.BsonDocument>("appointments");
            foreach (var inv in invoices)
            {
                if (!string.IsNullOrWhiteSpace(inv.AppointmentCode)) continue;
                if (!string.IsNullOrWhiteSpace(inv.OrderId) && inv.OrderId.StartsWith("ORD-", StringComparison.OrdinalIgnoreCase))
                {
                    inv.AppointmentCode = inv.OrderId;
                    continue;
                }
                if (string.IsNullOrWhiteSpace(inv.AppointmentId)) continue;
                if (inv.AppointmentId.StartsWith("APT-", StringComparison.OrdinalIgnoreCase) || inv.AppointmentId.StartsWith("ORD-", StringComparison.OrdinalIgnoreCase))
                {
                    inv.AppointmentCode = inv.AppointmentId;
                    continue;
                }
                var filters = new List<FilterDefinition<MongoDB.Bson.BsonDocument>>
                {
                    Builders<MongoDB.Bson.BsonDocument>.Filter.Eq("_id", inv.AppointmentId),
                    Builders<MongoDB.Bson.BsonDocument>.Filter.Eq("appointmentCode", inv.AppointmentId)
                };
                if (MongoDB.Bson.ObjectId.TryParse(inv.AppointmentId, out var oid))
                {
                    filters.Add(Builders<MongoDB.Bson.BsonDocument>.Filter.Eq("_id", oid));
                }
                var apptDoc = await apptCol.Find(Builders<MongoDB.Bson.BsonDocument>.Filter.Or(filters)).FirstOrDefaultAsync(ct);
                if (apptDoc != null)
                {
                    var code = apptDoc.TryGetValue("appointmentCode", out var ac) && ac.IsString ? ac.AsString
                        : (apptDoc.TryGetValue("appointment_code", out var ac2) && ac2.IsString ? ac2.AsString : null);
                    if (!string.IsNullOrWhiteSpace(code))
                    {
                        inv.AppointmentCode = code;
                    }
                }
            }
        }
        catch { }
    }

    public async Task<long> CountInvoicesAsync(
        string? patientId,
        InvoiceStatus? status,
        DateTime? fromDate,
        DateTime? toDate,
        CancellationToken ct = default)
    {
        var filter = BuildFilter(patientId, status, fromDate, toDate);
        return await _collection.CountDocumentsAsync(filter, cancellationToken: ct);
    }

    public async Task<Invoice> CreateAsync(Invoice invoice, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(invoice.InvoiceCode))
        {
            invoice.InvoiceCode = $"INV-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..6].ToUpper()}";
        }
        await _collection.InsertOneAsync(invoice, cancellationToken: ct);
        return invoice;
    }

    public async Task<Invoice?> UpdateAsync(Invoice invoice, CancellationToken ct = default)
    {
        var result = await _collection.ReplaceOneAsync(x => x.Id == invoice.Id, invoice, cancellationToken: ct);
        return result.ModifiedCount > 0 ? invoice : null;
    }

    public async Task<Invoice?> IncrementPrintCountAsync(string id, string? cashierId, string? cashierName, CancellationToken ct = default)
    {
        var existing = await GetByIdAsync(id, ct);
        if (existing == null) return null;

        var receiptNum = existing.ReceiptNumber;
        if (string.IsNullOrWhiteSpace(receiptNum))
        {
            receiptNum = $"REC-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..6].ToUpper()}";
        }

        var update = Builders<Invoice>.Update
            .Inc(x => x.PrintCount, 1)
            .Set(x => x.LastPrintedAt, DateTime.UtcNow)
            .Set(x => x.ReceiptNumber, receiptNum);

        if (!string.IsNullOrWhiteSpace(cashierId))
        {
            update = update.Set(x => x.CashierId, cashierId);
        }
        if (!string.IsNullOrWhiteSpace(cashierName))
        {
            update = update.Set(x => x.CashierName, cashierName);
        }

        var options = new FindOneAndUpdateOptions<Invoice>
        {
            ReturnDocument = ReturnDocument.After
        };

        return await _collection.FindOneAndUpdateAsync(x => x.Id == existing.Id, update, options, ct);
    }

    private static FilterDefinition<Invoice> BuildFilter(
        string? patientId,
        InvoiceStatus? status,
        DateTime? fromDate,
        DateTime? toDate)
    {
        var builder = Builders<Invoice>.Filter;
        var filters = new List<FilterDefinition<Invoice>>();

        if (!string.IsNullOrWhiteSpace(patientId))
        {
            filters.Add(builder.Eq(x => x.PatientId, patientId));
        }

        if (status.HasValue)
        {
            filters.Add(builder.Eq(x => x.Status, status.Value));
        }

        if (fromDate.HasValue)
        {
            filters.Add(builder.Gte(x => x.CreatedAt, fromDate.Value));
        }

        if (toDate.HasValue)
        {
            filters.Add(builder.Lte(x => x.CreatedAt, toDate.Value));
        }

        return filters.Count > 0 ? builder.And(filters) : builder.Empty;
    }
}
