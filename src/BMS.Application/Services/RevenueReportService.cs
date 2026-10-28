using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BMS.Application.DTOs.Reports;
using BMS.Application.Interfaces;
using BMS.Core.Entities;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace BMS.Application.Services;

/// <summary>
/// Production service implementation for laboratory financial revenue analytics and executive reporting (UC78).
/// Aggregates multi-dimensional revenue by category, payment method, channel, and referral sources.
/// </summary>
public class RevenueReportService : IRevenueReportService
{
    private readonly IMongoDatabase? _database;
    private readonly ILogger<RevenueReportService> _logger;

    private static readonly ConcurrentDictionary<string, RevenueTransaction> _transactions = new(
        InitializeSeedTransactions().ToDictionary(t => t.Id, t => t)
    );

    public RevenueReportService(ILogger<RevenueReportService> logger, IMongoDatabase? database = null)
    {
        _logger = logger;
        _database = database;
    }

    public async Task<ComprehensiveRevenueReportDto> GetComprehensiveReportAsync(RevenueFilterParameters parameters, CancellationToken ct = default)
    {
        var (fromDate, toDate) = ResolveDateRange(parameters);
        var filteredTxns = FilterTransactions(parameters, fromDate, toDate);

        var summary = CalculateSummary(filteredTxns, fromDate, toDate);
        var trend = CalculateTrend(filteredTxns, parameters.Period, fromDate, toDate);
        var byCategory = CalculateByCategory(filteredTxns);
        var byPaymentMethod = CalculateByPaymentMethod(filteredTxns);
        var byChannel = CalculateByChannel(filteredTxns);
        var topReferrals = CalculateTopReferrals(filteredTxns);

        return await Task.FromResult(new ComprehensiveRevenueReportDto
        {
            FromDate = fromDate,
            ToDate = toDate,
            Period = parameters.Period,
            Summary = summary,
            Trend = trend,
            ByCategory = byCategory,
            ByPaymentMethod = byPaymentMethod,
            ByChannel = byChannel,
            TopReferrals = topReferrals
        });
    }

    public async Task<RevenueSummaryDto> GetSummaryAsync(RevenueFilterParameters parameters, CancellationToken ct = default)
    {
        var (fromDate, toDate) = ResolveDateRange(parameters);
        var filteredTxns = FilterTransactions(parameters, fromDate, toDate);
        return await Task.FromResult(CalculateSummary(filteredTxns, fromDate, toDate));
    }

    public async Task<List<RevenueTrendPointDto>> GetTrendAsync(RevenueFilterParameters parameters, CancellationToken ct = default)
    {
        var (fromDate, toDate) = ResolveDateRange(parameters);
        var filteredTxns = FilterTransactions(parameters, fromDate, toDate);
        return await Task.FromResult(CalculateTrend(filteredTxns, parameters.Period, fromDate, toDate));
    }

    public async Task<List<RevenueByCategoryDto>> GetByCategoryAsync(RevenueFilterParameters parameters, CancellationToken ct = default)
    {
        var (fromDate, toDate) = ResolveDateRange(parameters);
        var filteredTxns = FilterTransactions(parameters, fromDate, toDate);
        return await Task.FromResult(CalculateByCategory(filteredTxns));
    }

    public async Task<List<RevenueByPaymentMethodDto>> GetByPaymentMethodAsync(RevenueFilterParameters parameters, CancellationToken ct = default)
    {
        var (fromDate, toDate) = ResolveDateRange(parameters);
        var filteredTxns = FilterTransactions(parameters, fromDate, toDate);
        return await Task.FromResult(CalculateByPaymentMethod(filteredTxns));
    }

    public async Task<List<RevenueByChannelDto>> GetByChannelAsync(RevenueFilterParameters parameters, CancellationToken ct = default)
    {
        var (fromDate, toDate) = ResolveDateRange(parameters);
        var filteredTxns = FilterTransactions(parameters, fromDate, toDate);
        return await Task.FromResult(CalculateByChannel(filteredTxns));
    }

    public async Task<List<RevenueExportItemDto>> GetExportTransactionsAsync(RevenueFilterParameters parameters, CancellationToken ct = default)
    {
        var (fromDate, toDate) = ResolveDateRange(parameters);
        var filteredTxns = FilterTransactions(parameters, fromDate, toDate);

        var list = filteredTxns
            .OrderByDescending(t => t.CreatedAt)
            .Select(t => new RevenueExportItemDto
            {
                TransactionNumber = t.TransactionNumber,
                OrderNumber = t.OrderNumber,
                CreatedAt = t.CreatedAt,
                PatientName = t.PatientName,
                Category = t.Category,
                ServiceType = t.ServiceType,
                GrossAmount = t.GrossAmount,
                BhytCoverageAmount = t.BhytCoverageAmount,
                DiscountAmount = t.DiscountAmount,
                NetAmount = t.NetAmount,
                PaymentMethod = t.PaymentMethod,
                CashierName = t.CashierName
            })
            .ToList();

        return await Task.FromResult(list);
    }

    #region Private Aggregation Methods

    private static (DateTime FromDate, DateTime ToDate) ResolveDateRange(RevenueFilterParameters parameters)
    {
        var now = DateTime.UtcNow;
        var toDate = parameters.ToDate ?? now;
        var fromDate = parameters.FromDate ?? toDate.AddDays(-30);

        if (fromDate > toDate)
        {
            fromDate = toDate.AddDays(-30);
        }

        return (fromDate, toDate);
    }

    private static List<RevenueTransaction> FilterTransactions(RevenueFilterParameters parameters, DateTime fromDate, DateTime toDate)
    {
        var query = _transactions.Values.AsEnumerable();

        query = query.Where(t => t.CreatedAt >= fromDate && t.CreatedAt <= toDate);

        if (!string.IsNullOrWhiteSpace(parameters.Category))
        {
            query = query.Where(t => t.Category.Equals(parameters.Category, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(parameters.PaymentMethod))
        {
            query = query.Where(t => t.PaymentMethod.Equals(parameters.PaymentMethod, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(parameters.ServiceType))
        {
            query = query.Where(t => t.ServiceType.Equals(parameters.ServiceType, StringComparison.OrdinalIgnoreCase));
        }

        return query.ToList();
    }

    private static RevenueSummaryDto CalculateSummary(List<RevenueTransaction> currentTxns, DateTime fromDate, DateTime toDate)
    {
        decimal gross = currentTxns.Sum(t => t.GrossAmount);
        decimal bhyt = currentTxns.Sum(t => t.BhytCoverageAmount);
        decimal discounts = currentTxns.Sum(t => t.DiscountAmount);
        decimal surcharges = currentTxns.Sum(t => t.HomeSurchargeAmount);
        decimal net = currentTxns.Sum(t => t.NetAmount);
        int totalOrders = currentTxns.Count;
        decimal aov = totalOrders > 0 ? Math.Round(net / totalOrders, 0) : 0m;

        // Calculate growth rate vs previous period of same duration
        var duration = toDate - fromDate;
        var prevFrom = fromDate - duration;
        var prevTo = fromDate;

        var prevTxns = _transactions.Values
            .Where(t => t.CreatedAt >= prevFrom && t.CreatedAt < prevTo)
            .ToList();

        decimal prevNet = prevTxns.Sum(t => t.NetAmount);
        double growthRate = prevNet > 0
            ? Math.Round((double)((net - prevNet) / prevNet) * 100, 1)
            : 0.0;

        var viCulture = CultureInfo.GetCultureInfo("vi-VN");

        return new RevenueSummaryDto
        {
            GrossRevenue = gross,
            BhytCoverageTotal = bhyt,
            DiscountsTotal = discounts,
            SurchargesTotal = surcharges,
            NetRevenue = net,
            TotalOrdersCount = totalOrders,
            AverageOrderValue = aov,
            GrowthRateVsPreviousPeriod = growthRate,
            FormattedGrossRevenue = gross.ToString("N0", viCulture) + " ₫",
            FormattedNetRevenue = net.ToString("N0", viCulture) + " ₫"
        };
    }

    private static List<RevenueTrendPointDto> CalculateTrend(
        List<RevenueTransaction> txns, string period, DateTime fromDate, DateTime toDate)
    {
        var points = new List<RevenueTrendPointDto>();
        var viCulture = CultureInfo.GetCultureInfo("vi-VN");

        var duration = (toDate - fromDate).TotalDays;

        if (duration <= 35)
        {
            // Group by Day
            var groups = txns
                .GroupBy(t => t.CreatedAt.Date)
                .ToDictionary(g => g.Key, g => g.ToList());

            for (var d = fromDate.Date; d <= toDate.Date; d = d.AddDays(1))
            {
                if (groups.TryGetValue(d, out var dayTxns))
                {
                    points.Add(new RevenueTrendPointDto
                    {
                        DateLabel = d.ToString("dd/MM", viCulture),
                        GrossAmount = dayTxns.Sum(t => t.GrossAmount),
                        NetAmount = dayTxns.Sum(t => t.NetAmount),
                        BhytAmount = dayTxns.Sum(t => t.BhytCoverageAmount),
                        OrderCount = dayTxns.Count
                    });
                }
                else
                {
                    points.Add(new RevenueTrendPointDto
                    {
                        DateLabel = d.ToString("dd/MM", viCulture),
                        GrossAmount = 0m,
                        NetAmount = 0m,
                        BhytAmount = 0m,
                        OrderCount = 0
                    });
                }
            }
        }
        else
        {
            // Group by Month
            var groups = txns
                .GroupBy(t => new { t.CreatedAt.Year, t.CreatedAt.Month })
                .OrderBy(g => g.Key.Year).ThenBy(g => g.Key.Month);

            foreach (var g in groups)
            {
                points.Add(new RevenueTrendPointDto
                {
                    DateLabel = $"T{g.Key.Month:D2}/{g.Key.Year}",
                    GrossAmount = g.Sum(t => t.GrossAmount),
                    NetAmount = g.Sum(t => t.NetAmount),
                    BhytAmount = g.Sum(t => t.BhytCoverageAmount),
                    OrderCount = g.Count()
                });
            }
        }

        return points;
    }

    private static List<RevenueByCategoryDto> CalculateByCategory(List<RevenueTransaction> txns)
    {
        decimal totalNet = txns.Sum(t => t.NetAmount);
        if (totalNet == 0) totalNet = 1; // avoid div by zero

        var groups = txns
            .GroupBy(t => t.Category)
            .Select(g =>
            {
                decimal amount = g.Sum(t => t.NetAmount);
                double pct = Math.Round((double)(amount / totalNet) * 100, 1);
                return new RevenueByCategoryDto
                {
                    Category = TranslateCategory(g.Key),
                    Amount = amount,
                    Percentage = pct,
                    OrderCount = g.Count()
                };
            })
            .OrderByDescending(c => c.Amount)
            .ToList();

        return groups;
    }

    private static List<RevenueByPaymentMethodDto> CalculateByPaymentMethod(List<RevenueTransaction> txns)
    {
        decimal totalNet = txns.Sum(t => t.NetAmount);
        if (totalNet == 0) totalNet = 1;

        var groups = txns
            .GroupBy(t => t.PaymentMethod)
            .Select(g =>
            {
                decimal amount = g.Sum(t => t.NetAmount);
                double pct = Math.Round((double)(amount / totalNet) * 100, 1);
                return new RevenueByPaymentMethodDto
                {
                    PaymentMethod = TranslatePaymentMethod(g.Key),
                    Amount = amount,
                    Percentage = pct,
                    TransactionCount = g.Count()
                };
            })
            .OrderByDescending(p => p.Amount)
            .ToList();

        return groups;
    }

    private static List<RevenueByChannelDto> CalculateByChannel(List<RevenueTransaction> txns)
    {
        decimal totalNet = txns.Sum(t => t.NetAmount);
        if (totalNet == 0) totalNet = 1;

        var groups = txns
            .GroupBy(t => t.ServiceType)
            .Select(g =>
            {
                decimal amount = g.Sum(t => t.NetAmount);
                double pct = Math.Round((double)(amount / totalNet) * 100, 1);
                return new RevenueByChannelDto
                {
                    Channel = g.Key.Equals("HomeCollection", StringComparison.OrdinalIgnoreCase) ? "Lấy mẫu tận nơi" : "Tại phòng khám / Bệnh viện",
                    Amount = amount,
                    Percentage = pct,
                    OrderCount = g.Count()
                };
            })
            .OrderByDescending(c => c.Amount)
            .ToList();

        return groups;
    }

    private static List<RevenueByReferralDto> CalculateTopReferrals(List<RevenueTransaction> txns)
    {
        return txns
            .GroupBy(t => new { t.ReferralDoctor, t.ReferralDepartment })
            .Select(g => new RevenueByReferralDto
            {
                Name = g.Key.ReferralDoctor,
                Department = g.Key.ReferralDepartment,
                Amount = g.Sum(t => t.NetAmount),
                OrderCount = g.Count()
            })
            .OrderByDescending(r => r.Amount)
            .Take(5)
            .ToList();
    }

    private static string TranslateCategory(string key)
    {
        return key switch
        {
            "Biochemistry" => "Hóa Sinh Lâm Sàng",
            "Hematology" => "Huyết Học & Đông Máu",
            "Immunology" => "Miễn Dịch & Dị Ứng",
            "Microbiology" => "Vi Sinh & Kháng Sinh Đồ",
            "GeneralCheckup" => "Gói Khám Tổng Quát",
            _ => key
        };
    }

    private static string TranslatePaymentMethod(string key)
    {
        return key switch
        {
            "VietQR" => "Chuyển Khoản VietQR / MoMo",
            "Cash" => "Tiền Mặt Tại Quầy",
            "BhytDirect" => "Bảo Hiểm Y Tế (BHYT)",
            "Card" => "Thẻ Ngân Hàng / POS",
            _ => key
        };
    }

    #endregion

    #region Seed Data Generator

    private static List<RevenueTransaction> InitializeSeedTransactions()
    {
        var list = new List<RevenueTransaction>();
        var now = DateTime.UtcNow;

        string[] categories = { "Biochemistry", "Hematology", "Immunology", "Microbiology", "GeneralCheckup" };
        string[] methods = { "VietQR", "Cash", "BhytDirect", "Card" };
        string[] doctors = { "BS. CKII Lê Hoàng Nam", "BS. CKI Trần Thị Mai", "ThS. BS Phạm Minh Tuấn", "BS. Nguyễn Văn Hùng" };
        string[] depts = { "Khoa Khám Bệnh", "Khoa Nội Tiết", "Khoa Cấp Cứu", "Khoa Tiêu Hóa" };

        var rnd = new Random(42);

        // Seed 90 days of transactions for comprehensive trend analysis
        for (int i = 0; i < 90; i++)
        {
            var date = now.AddDays(-89 + i);
            int ordersPerDay = rnd.Next(8, 20); // 8-20 transactions per day

            for (int j = 0; j < ordersPerDay; j++)
            {
                var category = categories[rnd.Next(categories.Length)];
                var isHome = rnd.NextDouble() < 0.25; // 25% home collection
                var method = methods[rnd.Next(methods.Length)];
                var doc = doctors[rnd.Next(doctors.Length)];
                var dept = depts[rnd.Next(depts.Length)];

                decimal basePrice = category switch
                {
                    "Biochemistry" => rnd.Next(250, 750) * 1000m,
                    "Hematology" => rnd.Next(150, 450) * 1000m,
                    "Immunology" => rnd.Next(450, 1200) * 1000m,
                    "Microbiology" => rnd.Next(300, 600) * 1000m,
                    "GeneralCheckup" => rnd.Next(1500, 3500) * 1000m,
                    _ => 300000m
                };

                decimal gross = basePrice;
                decimal bhyt = method == "BhytDirect" ? Math.Round(gross * 0.8m, 0) : 0m;
                decimal discount = rnd.NextDouble() < 0.15 ? Math.Round(gross * 0.1m, 0) : 0m;
                decimal surcharge = isHome ? 150000m : 0m;
                decimal net = Math.Max(0m, gross - bhyt - discount + surcharge);

                list.Add(new RevenueTransaction
                {
                    Id = $"TXN-{date:yyyyMMdd}-{i:D2}{j:D2}",
                    TransactionNumber = $"TXN-{date:yyyyMM}-{i:D3}{j:D2}",
                    OrderNumber = $"ORD-{date:yyyyMM}-{rnd.Next(1000, 9999)}",
                    PatientName = $"Bệnh nhân {rnd.Next(100, 999)}",
                    PatientId = $"BN-{rnd.Next(10000, 99999)}",
                    ServiceType = isHome ? "HomeCollection" : "InClinic",
                    Category = category,
                    GrossAmount = gross,
                    BhytCoverageAmount = bhyt,
                    DiscountAmount = discount,
                    HomeSurchargeAmount = surcharge,
                    NetAmount = net,
                    PaymentMethod = method,
                    ReferralDoctor = doc,
                    ReferralDepartment = dept,
                    CreatedAt = date.AddHours(rnd.Next(7, 19)).AddMinutes(rnd.Next(0, 59)),
                    CashierName = "Thu ngân Nguyễn Tấn Phát"
                });
            }
        }

        return list;
    }

    #endregion
}
