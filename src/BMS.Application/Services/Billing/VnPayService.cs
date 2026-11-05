using System;
using System.Collections.Generic;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace BMS.Application.Services.Billing;

/// <summary>
/// Official VNPAY Gateway v2.1.0 Integration Service (HMAC-SHA512).
/// Merchant Terminal: G73VS0FO (BMS Clinical Lab Sandbox)
/// </summary>
public sealed class VnPayService
{
    private const string TmnCode = "G73VS0FO";
    private const string HashSecret = "QQG02AQ7C273SB3ZJ16SQ2DCTCGIELIZ";
    private const string BaseUrl = "https://sandbox.vnpayment.vn/paymentv2/vpcpay.html";

    public static string CreatePaymentUrl(
        string txnRef,
        decimal amount,
        string orderInfo,
        string returnUrl,
        string clientIp = "127.0.0.1")
    {
        var vnpAmount = ((long)Math.Round(amount * 100)).ToString();

        DateTime vnpayTime;
        try
        {
            var tz = TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");
            vnpayTime = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);
        }
        catch
        {
            vnpayTime = DateTime.UtcNow.AddHours(7);
        }

        var createDate = vnpayTime.ToString("yyyyMMddHHmmss");
        var expireDate = vnpayTime.AddMinutes(15).ToString("yyyyMMddHHmmss");

        var cleanIp = string.IsNullOrWhiteSpace(clientIp) || clientIp.Contains(':') ? "127.0.0.1" : clientIp.Trim();

        // VNPay max txnRef length is 34 characters (alphanumeric only)
        var cleanTxnRef = (txnRef ?? "").Replace("-", "");
        if (cleanTxnRef.Length > 30) cleanTxnRef = cleanTxnRef[..30];
        if (string.IsNullOrWhiteSpace(cleanTxnRef)) cleanTxnRef = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();

        var cleanOrderInfo = string.IsNullOrWhiteSpace(orderInfo)
            ? $"Thanh toan BMS {cleanTxnRef}"
            : orderInfo.Replace("-", " ").Trim();
        if (cleanOrderInfo.Length > 100) cleanOrderInfo = cleanOrderInfo[..100];

        var data = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            { "vnp_Version", "2.1.0" },
            { "vnp_Command", "pay" },
            { "vnp_TmnCode", TmnCode },
            { "vnp_Amount", vnpAmount },
            { "vnp_CreateDate", createDate },
            { "vnp_CurrCode", "VND" },
            { "vnp_IpAddr", cleanIp },
            { "vnp_Locale", "vn" },
            { "vnp_OrderInfo", cleanOrderInfo },
            { "vnp_OrderType", "other" },
            { "vnp_ReturnUrl", returnUrl },
            { "vnp_TxnRef", cleanTxnRef },
            { "vnp_ExpireDate", expireDate }
        };

        var query = new StringBuilder();
        var rawData = new StringBuilder();

        foreach (var (key, val) in data)
        {
            if (!string.IsNullOrEmpty(val))
            {
                var encKey = WebUtility.UrlEncode(key);
                var encVal = WebUtility.UrlEncode(val);
                query.Append(encKey).Append('=').Append(encVal).Append('&');
                rawData.Append(encKey).Append('=').Append(encVal).Append('&');
            }
        }

        if (rawData.Length > 0)
        {
            rawData.Length--;
        }

        var secureHash = HmacSha512(HashSecret, rawData.ToString());
        query.Append("vnp_SecureHash=").Append(secureHash);

        return $"{BaseUrl}?{query}";
    }

    public static bool VerifySignature(IDictionary<string, string> queryParams, string inputHash)
    {
        if (string.IsNullOrWhiteSpace(inputHash)) return false;

        var sorted = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var (k, v) in queryParams)
        {
            if (k.StartsWith("vnp_", StringComparison.OrdinalIgnoreCase) &&
                !k.Equals("vnp_SecureHash", StringComparison.OrdinalIgnoreCase) &&
                !k.Equals("vnp_SecureHashType", StringComparison.OrdinalIgnoreCase))
            {
                sorted[k] = v;
            }
        }

        var raw = new StringBuilder();
        foreach (var (k, v) in sorted)
        {
            if (!string.IsNullOrEmpty(v))
            {
                var encKey = WebUtility.UrlEncode(k);
                var encVal = WebUtility.UrlEncode(v);
                raw.Append(encKey).Append('=').Append(encVal).Append('&');
            }
        }

        if (raw.Length > 0)
        {
            raw.Length--;
        }

        var checkHash = HmacSha512(HashSecret, raw.ToString());
        return string.Equals(checkHash, inputHash, StringComparison.OrdinalIgnoreCase);
    }

    private static string HmacSha512(string key, string inputData)
    {
        var keyBytes = Encoding.UTF8.GetBytes(key);
        var inputBytes = Encoding.UTF8.GetBytes(inputData);
        using var hmac = new HMACSHA512(keyBytes);
        var hashBytes = hmac.ComputeHash(inputBytes);
        return BitConverter.ToString(hashBytes).Replace("-", string.Empty);
    }
}
