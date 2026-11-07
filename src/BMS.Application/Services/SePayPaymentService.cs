using System.Globalization;
using System.Text.RegularExpressions;
using BMS.Application.DTOs.Billing;
using BMS.Application.Interfaces;
using BMS.Application.Options;
using BMS.Core.Entities;
using BMS.Core.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BMS.Application.Services;

/// <summary>
/// Confirms an appointment when SePay reports an incoming bank transfer that matches the appointment code and amount.
/// </summary>
public sealed class SePayPaymentService(
    IAppointmentRepository appointments,
    ITestPackageRepository packages,
    IOptions<SePayOptions> options,
    ILogger<SePayPaymentService> logger)
{
    private static readonly Regex AppointmentCodePattern = new(@"APT-?\d{6}-?\d+", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(options.Value.AccountNumber) &&
        !string.IsNullOrWhiteSpace(options.Value.BankCode);

    public bool ApiKeyMatches(string? authorizationHeader)
    {
        var expected = options.Value.WebhookApiKey?.Trim();
        if (string.IsNullOrWhiteSpace(expected)) return true;
        if (string.IsNullOrWhiteSpace(authorizationHeader)) return false;

        var provided = authorizationHeader.Trim();
        const string prefix = "Apikey ";
        if (provided.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            provided = provided[prefix.Length..].Trim();
        else if (provided.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            provided = provided["Bearer ".Length..].Trim();

        return string.Equals(provided, expected, StringComparison.Ordinal);
    }

    public async Task<SePayCheckoutDto?> GetCheckoutAsync(string appointmentId, string? currentUserId, bool isPatient, CancellationToken ct)
    {
        var appointment = await FindAppointmentAsync(appointmentId, ct);
        if (appointment == null) return null;
        if (isPatient && !string.IsNullOrWhiteSpace(currentUserId) &&
            !string.Equals(appointment.PatientId, currentUserId, StringComparison.Ordinal))
            return null;

        var amount = await ResolveAmountAsync(appointment, ct);
        var content = BuildPaymentCode(appointment.AppointmentCode);
        var account = options.Value.AccountNumber?.Trim() ?? string.Empty;
        var bank = options.Value.BankCode?.Trim() ?? string.Empty;
        var package = string.IsNullOrWhiteSpace(appointment.PackageId)
            ? null
            : await packages.GetByIdOrCodeAsync(appointment.PackageId, includeInactive: true, ct);
        var doctor = string.IsNullOrWhiteSpace(appointment.DoctorId)
            ? null
            : await appointments.GetDoctorAsync(appointment.DoctorId, ct);
        var qr = string.Empty;
        if (IsConfigured && amount > 0)
        {
            qr = "https://qr.sepay.vn/img?acc=" + Uri.EscapeDataString(account)
                + "&bank=" + Uri.EscapeDataString(bank)
                + "&amount=" + amount.ToString("0", CultureInfo.InvariantCulture)
                + "&des=" + Uri.EscapeDataString(content);
        }

        return new SePayCheckoutDto
        {
            AppointmentId = appointment.Id,
            AppointmentCode = appointment.AppointmentCode,
            PackageName = package?.PackageName ?? string.Empty,
            VisitDate = appointment.AppointmentDate.ToString("dd/MM/yyyy"),
            SlotTime = appointment.StartTime.ToString(@"hh\:mm"),
            DoctorName = doctor?.FullName ?? string.Empty,
            RoomName = appointment.RoomId ?? string.Empty,
            TransactionId = appointment.SepayTransactionId,
            PaidAt = appointment.PaidAt?.ToLocalTime().ToString("dd/MM/yyyy HH:mm"),
            Status = appointment.Status.ToString(),
            Paid = appointment.Status == AppointmentStatus.Confirmed || appointment.PaidAt != null,
            Amount = appointment.PaidAmount ?? amount,
            TransferContent = content,
            AccountNumber = account,
            BankCode = bank,
            AccountName = options.Value.AccountName?.Trim() ?? string.Empty,
            QrUrl = qr
        };
    }

    public async Task HandleWebhookAsync(SePayWebhookPayload payload, CancellationToken ct)
    {
        if (!string.Equals(payload.TransferType, "in", StringComparison.OrdinalIgnoreCase))
            return;

        var appointment = await MatchAppointmentAsync(payload, ct);
        if (appointment == null)
        {
            logger.LogInformation("SePay transfer {TransactionId} did not match an appointment.", payload.Id);
            return;
        }

        if (appointment.Status == AppointmentStatus.Confirmed)
            return;

        var expected = await ResolveAmountAsync(appointment, ct);
        if (expected <= 0 || payload.TransferAmount + 0.01m < expected)
        {
            logger.LogWarning(
                "SePay transfer {TransactionId} for {Code} amount {Paid} is below expected {Expected}.",
                payload.Id, appointment.AppointmentCode, payload.TransferAmount, expected);
            return;
        }

        var confirmed = await appointments.TryConfirmPaymentAsync(
            appointment.Id,
            payload.Id.ToString(CultureInfo.InvariantCulture),
            payload.TransferAmount,
            ct);
        if (confirmed)
        {
            logger.LogInformation(
                "Appointment {Code} confirmed from SePay transaction {TransactionId}.",
                appointment.AppointmentCode, payload.Id);
        }
    }

    private string BuildPaymentCode(string appointmentCode)
    {
        var prefix = string.IsNullOrWhiteSpace(options.Value.PaymentPrefix) ? "SEVQR" : options.Value.PaymentPrefix.Trim();
        var compact = Regex.Replace(appointmentCode ?? string.Empty, "[^A-Za-z0-9]", "").ToUpperInvariant();
        if (compact.StartsWith("APT", StringComparison.Ordinal))
            compact = compact[3..];
        return prefix + compact;
    }

    private async Task<Appointment?> MatchAppointmentAsync(SePayWebhookPayload payload, CancellationToken ct)
    {
        var haystack = $"{payload.Code} {payload.Content}";
        var prefix = string.IsNullOrWhiteSpace(options.Value.PaymentPrefix) ? "SEVQR" : options.Value.PaymentPrefix.Trim();
        var prefixed = Regex.Match(haystack, Regex.Escape(prefix) + @"(\d{6})(\d+)", RegexOptions.IgnoreCase);
        if (prefixed.Success)
        {
            var fromPrefix = "APT-" + prefixed.Groups[1].Value + "-" + prefixed.Groups[2].Value;
            var found = await appointments.GetByCodeAsync(fromPrefix, ct);
            if (found != null) return found;
        }

        foreach (Match match in AppointmentCodePattern.Matches(haystack))
        {
            var found = await FindByCodeTokenAsync(match.Value, ct);
            if (found != null) return found;
        }
        return null;
    }

    private async Task<Appointment?> FindByCodeTokenAsync(string token, CancellationToken ct)
    {
        var direct = await appointments.GetByCodeAsync(token, ct);
        if (direct != null) return direct;

        var compact = Regex.Replace(token, "[^A-Za-z0-9]", "");
        if (compact.Length >= 13 && compact.StartsWith("APT", StringComparison.OrdinalIgnoreCase))
        {
            var normalized = "APT-" + compact[3..9] + "-" + compact[9..];
            return await appointments.GetByCodeAsync(normalized, ct);
        }
        return null;
    }

    private Task<Appointment?> FindAppointmentAsync(string idOrCode, CancellationToken ct)
    {
        return FindCore(idOrCode, ct);

        async Task<Appointment?> FindCore(string value, CancellationToken token)
        {
            var byId = await appointments.GetByIdAsync(value, token);
            if (byId != null) return byId;
            return await appointments.GetByCodeAsync(value, token);
        }
    }

    private async Task<decimal> ResolveAmountAsync(Appointment appointment, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(appointment.PackageId)) return 0;
        var package = await packages.GetByIdOrCodeAsync(appointment.PackageId, includeInactive: true, ct);
        if (package == null) return 0;
        return package.Price < 0 ? 0 : package.Price;
    }
}
