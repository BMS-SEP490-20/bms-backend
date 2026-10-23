using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BMS.Application.DTOs.Billing;
using BMS.Application.Services;
using BMS.Application.Utilities;

Console.WriteLine("=================================================");
Console.WriteLine("🧪 UC53 Automated Verification Suite: Calculate Fees");
Console.WriteLine("=================================================");

int passed = 0;
int total = 0;

void AssertEqual<T>(string checkName, T actual, T expected)
{
    total++;
    if (EqualityComparer<T>.Default.Equals(actual, expected))
    {
        Console.WriteLine($"  [PASS] {checkName} (Expected: {expected}, Got: {actual})");
        passed++;
    }
    else
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"  [FAIL] {checkName} (Expected: {expected}, Got: {actual})");
        Console.ResetColor();
    }
}

void AssertTrue(string checkName, bool condition)
{
    total++;
    if (condition)
    {
        Console.WriteLine($"  [PASS] {checkName}");
        passed++;
    }
    else
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"  [FAIL] {checkName} (Condition was FALSE)");
        Console.ResetColor();
    }
}

var feeService = new CalculateFeeService();

// Test 1: Standard Clinic Base Fee (CBC + BIO)
Console.WriteLine("\n--- Check 1: Standard Clinic Base Fee (CBC 150k + BIO 250k = 400k) ---");
var req1 = new CalculateFeeRequestDto
{
    PackageIds = new List<string> { "PKG-CBC-01", "PKG-BIO-01" },
    ServiceType = "Clinic"
};
var res1 = await feeService.CalculateFeeAsync(req1);
AssertEqual("Subtotal equals 400,000", res1.TestSubTotal, 400000m);
AssertEqual("Home surcharge is 0 for Clinic", res1.HomeCollectionSurcharge, 0m);
AssertEqual("Net total payable is 400,000", res1.NetTotalPayable, 400000m);
AssertTrue("Total in words contains 'Bốn trăm nghìn đồng chẵn'", res1.TotalInWords.Contains("Bốn trăm nghìn đồng chẵn"));

// Test 2: Home Collection Surcharge (+150,000 VND)
Console.WriteLine("\n--- Check 2: Home Collection Surcharge ---");
var req2 = new CalculateFeeRequestDto
{
    PackageIds = new List<string> { "PKG-CBC-01" },
    ServiceType = "HomeCollection"
};
var res2 = await feeService.CalculateFeeAsync(req2);
AssertEqual("CBC Subtotal is 150,000", res2.TestSubTotal, 150000m);
AssertEqual("Home Collection Surcharge is 150,000", res2.HomeCollectionSurcharge, 150000m);
AssertEqual("Gross total is 300,000", res2.GrossTotal, 300000m);
AssertEqual("Net total payable is 300,000", res2.NetTotalPayable, 300000m);
AssertTrue("Total in words contains 'Ba trăm nghìn đồng chẵn'", res2.TotalInWords.Contains("Ba trăm nghìn đồng chẵn"));

// Test 3: Standard Health Insurance (BHYT 80% coverage)
Console.WriteLine("\n--- Check 3: Standard BHYT (80% coverage on tests, patient pays 20% + home surcharge) ---");
var req3 = new CalculateFeeRequestDto
{
    PackageIds = new List<string> { "PKG-CBC-01" }, // 150,000 VND
    ServiceType = "HomeCollection",                  // 150,000 VND
    InsuranceCardNumber = "DN4791234567890"          // 80% coverage on 150,000 = 120,000
};
var res3 = await feeService.CalculateFeeAsync(req3);
AssertTrue("Insurance verified", res3.HasInsurance);
AssertEqual("Insurance coverage percent is 80%", res3.InsuranceCoveragePercent, 80);
AssertEqual("Insurance deduction is 120,000", res3.InsuranceDeduction, 120000m);
// Patient pays: 150,000 (test) - 120,000 (BHYT) + 150,000 (home surcharge) = 180,000 VND
AssertEqual("Net total payable is 180,000", res3.NetTotalPayable, 180000m);
AssertTrue("Total in words contains 'Một trăm tám mươi nghìn đồng chẵn'", res3.TotalInWords.Contains("Một trăm tám mươi nghìn đồng chẵn"));

// Test 4: Special BHYT Tier (TE - 100% coverage on tests)
Console.WriteLine("\n--- Check 4: 100% BHYT Coverage for Children < 6 (TE prefix) ---");
var req4 = new CalculateFeeRequestDto
{
    PackageIds = new List<string> { "PKG-CBC-01", "PKG-BIO-01" }, // 400,000 VND
    ServiceType = "Clinic",
    InsuranceCardNumber = "TE1012345678901"
};
var res4 = await feeService.CalculateFeeAsync(req4);
AssertEqual("Insurance coverage percent is 100%", res4.InsuranceCoveragePercent, 100);
AssertEqual("Insurance deduction equals test total (400,000)", res4.InsuranceDeduction, 400000m);
AssertEqual("Net total payable is 0", res4.NetTotalPayable, 0m);
AssertTrue("Total in words contains 'Không đồng chẵn'", res4.TotalInWords.Contains("Không đồng chẵn"));

// Test 5: Promotional Voucher (BMS10 - 10% discount)
Console.WriteLine("\n--- Check 5: Promotional Voucher (BMS10 = 10% off) ---");
var req5 = new CalculateFeeRequestDto
{
    PackageIds = new List<string> { "PKG-GENERAL-01" }, // 1,200,000 VND
    ServiceType = "Clinic",
    VoucherCode = "BMS10" // 10% capped at 200,000 VND -> 120,000 VND
};
var res5 = await feeService.CalculateFeeAsync(req5);
AssertTrue("Voucher valid", res5.IsVoucherValid);
AssertEqual("Voucher discount is 120,000", res5.VoucherDiscount, 120000m);
AssertEqual("Net total payable is 1,080,000", res5.NetTotalPayable, 1080000m);

// Test 6: Free Home Surcharge Voucher (FREEHOME)
Console.WriteLine("\n--- Check 6: FREEHOME Voucher ---");
var req6 = new CalculateFeeRequestDto
{
    PackageIds = new List<string> { "PKG-CBC-01" }, // 150,000 VND
    ServiceType = "HomeCollection",                  // 150,000 VND
    VoucherCode = "FREEHOME"                         // 150,000 VND off
};
var res6 = await feeService.CalculateFeeAsync(req6);
AssertTrue("Voucher FREEHOME valid", res6.IsVoucherValid);
AssertEqual("Voucher discount is 150,000", res6.VoucherDiscount, 150000m);
AssertEqual("Net total payable is 150,000", res6.NetTotalPayable, 150000m);

// Test 7: Non-negative constraint when discounts exceed total
Console.WriteLine("\n--- Check 7: Non-negative constraint ---");
var req7 = new CalculateFeeRequestDto
{
    PackageIds = new List<string> { "PKG-CBC-01" }, // 150,000 VND
    InsuranceCardNumber = "TE1012345678901",         // 150,000 deduction
    VoucherCode = "VIP50"                            // 50,000 discount
};
var res7 = await feeService.CalculateFeeAsync(req7);
AssertTrue("Net total payable is strictly >= 0", res7.NetTotalPayable >= 0);
AssertEqual("Net total payable is 0", res7.NetTotalPayable, 0m);

// Test 8: Vietnamese Number to Words Conversion accuracy
Console.WriteLine("\n--- Check 8: Vietnamese Number to Words Utility ---");
AssertEqual("0 VND words", VietnameseCurrencyWords.Convert(0), "Không đồng chẵn");
AssertEqual("150,000 VND words", VietnameseCurrencyWords.Convert(150000), "Một trăm năm mươi nghìn đồng chẵn");
AssertEqual("1,250,000 VND words", VietnameseCurrencyWords.Convert(1250000), "Một triệu hai trăm năm mươi nghìn đồng chẵn");

Console.WriteLine("\n=================================================");
Console.WriteLine($"RESULTS: {passed}/{total} checks PASSED!");
Console.WriteLine("=================================================");

if (passed != total)
{
    Environment.Exit(1);
}
