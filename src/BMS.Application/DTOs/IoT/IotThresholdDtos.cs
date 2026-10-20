using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace BMS.Application.DTOs.IoT;

/// <summary>
/// Data transfer object representing an IoT device threshold configuration.
/// </summary>
public class IotThresholdDto
{
    public string Id { get; set; } = string.Empty;
    public string DeviceId { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public string SensorType { get; set; } = "Temperature";
    public string Unit { get; set; } = "°C";
    public double LowAlert { get; set; }
    public double HighAlert { get; set; }
    public int IntervalSeconds { get; set; }
    public string Status { get; set; } = "Active";
    public DateTime LastUpdated { get; set; }
    public string UpdatedBy { get; set; } = string.Empty;
}

/// <summary>
/// Payload for updating a single IoT device threshold.
/// </summary>
public class UpdateIotThresholdRequestDto
{
    [Required]
    public double LowAlert { get; set; }

    [Required]
    public double HighAlert { get; set; }

    [Range(5, 3600)]
    public int IntervalSeconds { get; set; } = 60;

    public string? Status { get; set; }
}

/// <summary>
/// Payload for batch updating multiple IoT thresholds simultaneously (Save all).
/// </summary>
public class BatchUpdateIotThresholdsRequestDto
{
    [Required]
    public List<IotThresholdUpdateItemDto> Devices { get; set; } = new();
}

public class IotThresholdUpdateItemDto
{
    [Required]
    public string DeviceId { get; set; } = string.Empty;

    [Required]
    public double LowAlert { get; set; }

    [Required]
    public double HighAlert { get; set; }

    [Range(5, 3600)]
    public int IntervalSeconds { get; set; } = 60;
}

/// <summary>
/// IoT alert recipient DTO.
/// </summary>
public class IotAlertRecipientDto
{
    public string Id { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public List<string> Channels { get; set; } = new();
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// Payload for adding a new alert recipient.
/// </summary>
public class AddIotAlertRecipientRequestDto
{
    [Required]
    public string FullName { get; set; } = string.Empty;

    [Required]
    public string Role { get; set; } = string.Empty;

    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    public string Phone { get; set; } = string.Empty;

    public List<string> Channels { get; set; } = new() { "Push", "Email" };
}

/// <summary>
/// Summary response containing all thresholds, rule counts, and alert recipients.
/// </summary>
public class IotThresholdConfigSummaryDto
{
    public int TotalDevices { get; set; }
    public int TotalRules { get; set; }
    public List<IotThresholdDto> Thresholds { get; set; } = new();
    public List<IotAlertRecipientDto> Recipients { get; set; } = new();
    public string EscalationPolicy { get; set; } = "Alert channels: Push notification (FCM) + Email. SMS escalation after 5 min if not acknowledged.";
}
