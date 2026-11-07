using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BMS.Application.DTOs.IoT;
using BMS.Application.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BMS.Api.Services;

/// <summary>
/// Background service that connects the BMS Backend to an MQTT v3.1.1 Broker (<c>broker.emqx.io:1883</c>)
/// so the physical ESP32 sensor (<c>BMS-IOT-001</c>) and the Backend can exchange real-time temperature/humidity
/// telemetry, offline buffered readings, 2-way threshold configurations, and Last-Will disconnection alerts
/// across different Wi-Fi networks without requiring a static LAN IP.
/// Conforms to: UC65, UC66, UC67, UC68, UC69, MSG-ERR-03 &amp; AGENTS-CODE.md.
/// </summary>
public sealed class IotMqttBridgeHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<IotMqttBridgeHostedService> _logger;

    private readonly string _brokerHost;
    private readonly int _brokerPort;
    private readonly string _topicPrefix;

    private static NetworkStream? _activeStream;
    private static SemaphoreSlim? _activeWriteLock;
    private static string _activeTopicPrefix = "bms/sep490/iot";

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public IotMqttBridgeHostedService(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<IotMqttBridgeHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _logger = logger;

        _brokerHost = _configuration["IoT:MqttBrokerHost"] ?? "broker.emqx.io";
        _brokerPort = int.TryParse(_configuration["IoT:MqttBrokerPort"], out var port) ? port : 1883;
        _topicPrefix = _configuration["IoT:MqttTopicPrefix"] ?? "bms/sep490/iot";
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "[MQTT Bridge] Starting IoT MQTT Bridge connecting to {Broker}:{Port} (Topic prefix: {Prefix}/#)",
            _brokerHost, _brokerPort, _topicPrefix);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunMqttSessionAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning("[MQTT Bridge] Connection interrupted ({Message}). Reconnecting in 3s...", ex.Message);
                await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
            }
        }
    }

    private async Task RunMqttSessionAsync(CancellationToken ct)
    {
        using var tcp = new TcpClient();
        tcp.NoDelay = true;
        using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        connectCts.CancelAfter(TimeSpan.FromSeconds(10));

        await tcp.ConnectAsync(_brokerHost, _brokerPort, connectCts.Token);
        using var stream = tcp.GetStream();
        using var writeLock = new SemaphoreSlim(1, 1);

        var clientId = $"bms-be-{Environment.MachineName}-{Guid.NewGuid().ToString("N")[..6]}";
        await SendConnectPacketAsync(stream, clientId, keepAliveSeconds: 30, ct);

        // Read CONNACK (0x20, 0x02, flags, returnCode)
        var connAckHeader = await ReadExactAsync(stream, 4, ct);
        if (connAckHeader[0] != 0x20 || connAckHeader[3] != 0x00)
        {
            throw new IOException($"MQTT Broker rejected CONNECT with code {connAckHeader[3]}.");
        }

        // Subscribe to telemetry and status topics for all BMS IoT devices
        var telemetryFilter = $"{_topicPrefix}/+/telemetry";
        var statusFilter = $"{_topicPrefix}/+/status";
        await SendSubscribePacketAsync(stream, packetId: 1, [telemetryFilter, statusFilter], ct);

        _logger.LogInformation(
            "[MQTT Bridge] Connected to {Broker}:{Port} as {ClientId}. Subscribed to '{Telemetry}' & '{Status}'.",
            _brokerHost, _brokerPort, clientId, telemetryFilter, statusFilter);

        // Publish initial threshold config for BMS-IOT-001 so ESP32 gets it immediately upon subscribing
        _activeStream = stream;
        _activeWriteLock = writeLock;
        _activeTopicPrefix = _topicPrefix;
        await PublishDeviceConfigAsync(stream, writeLock, "BMS-IOT-001", ct);

        var lastPingAt = DateTime.UtcNow;

        while (!ct.IsCancellationRequested && tcp.Connected)
        {
            if (stream.DataAvailable)
            {
                await ReadAndProcessNextPacketAsync(stream, writeLock, ct);
            }
            else
            {
                if ((DateTime.UtcNow - lastPingAt).TotalSeconds >= 15)
                {
                    await writeLock.WaitAsync(ct);
                    try
                    {
                        // MQTT PINGREQ (0xC0, 0x00)
                        await stream.WriteAsync(new byte[] { 0xC0, 0x00 }, ct);
                        await stream.FlushAsync(ct);
                    }
                    finally
                    {
                        writeLock.Release();
                    }
                    lastPingAt = DateTime.UtcNow;
                }

                await Task.Delay(80, ct);
            }
        }
    }

    private async Task ReadAndProcessNextPacketAsync(NetworkStream stream, SemaphoreSlim writeLock, CancellationToken ct)
    {
        int firstByte = stream.ReadByte();
        if (firstByte < 0)
        {
            throw new EndOfStreamException("MQTT TCP stream closed by remote broker.");
        }

        int remainingLength = await ReadRemainingLengthAsync(stream, ct);
        byte[] payloadBuffer = remainingLength > 0
            ? await ReadExactAsync(stream, remainingLength, ct)
            : Array.Empty<byte>();

        int packetType = (firstByte >> 4) & 0x0F;
        int flags = firstByte & 0x0F;

        // PacketType 3 = PUBLISH
        if (packetType == 3 && payloadBuffer.Length >= 2)
        {
            int topicLen = (payloadBuffer[0] << 8) | payloadBuffer[1];
            if (payloadBuffer.Length < 2 + topicLen) return;

            string topic = Encoding.UTF8.GetString(payloadBuffer, 2, topicLen);
            int offset = 2 + topicLen;

            int qos = (flags >> 1) & 0x03;
            if (qos > 0)
            {
                if (payloadBuffer.Length < offset + 2) return;
                int packetId = (payloadBuffer[offset] << 8) | payloadBuffer[offset + 1];
                offset += 2;

                if (qos == 1)
                {
                    await writeLock.WaitAsync(ct);
                    try
                    {
                        byte[] pubAck = [0x40, 0x02, (byte)(packetId >> 8), (byte)(packetId & 0xFF)];
                        await stream.WriteAsync(pubAck, ct);
                        await stream.FlushAsync(ct);
                    }
                    finally
                    {
                        writeLock.Release();
                    }
                }
            }

            string jsonPayload = Encoding.UTF8.GetString(payloadBuffer, offset, payloadBuffer.Length - offset);
            await HandleIncomingMqttMessageAsync(topic, jsonPayload, stream, writeLock, ct);
        }
    }

    private async Task HandleIncomingMqttMessageAsync(
        string topic,
        string jsonPayload,
        NetworkStream stream,
        SemaphoreSlim writeLock,
        CancellationToken ct)
    {
        try
        {
            // Extract deviceId from topic: bms/sep490/iot/{deviceId}/telemetry or /status
            var segments = topic.Split('/', StringSplitOptions.RemoveEmptyEntries);
            string topicDeviceId = segments.Length >= 4 ? segments[3] : "BMS-IOT-001";

            if (topic.EndsWith("/telemetry", StringComparison.OrdinalIgnoreCase))
            {
                var dto = JsonSerializer.Deserialize<IngestIotTelemetryRequestDto>(jsonPayload, _jsonOptions);
                if (dto == null) return;

                if (string.IsNullOrWhiteSpace(dto.DeviceId))
                {
                    dto.DeviceId = topicDeviceId;
                }

                using var scope = _scopeFactory.CreateScope();
                var telemetryService = scope.ServiceProvider.GetRequiredService<IIotTelemetryService>();
                var result = await telemetryService.IngestTelemetryAsync(dto, ct);

                _logger.LogInformation(
                    "[MQTT Telemetry] Received from {DeviceId}: {Temp:F1}°C, {Hum:F1}% (Buffered synced: {Buf})",
                    result.DeviceId, result.Temperature, result.Humidity, result.BufferedSyncedCount);

                // Push 2-way threshold configuration back to ESP32 over MQTT
                var configJson = JsonSerializer.Serialize(new
                {
                    deviceId = result.DeviceId,
                    minTemp = result.MinTemp,
                    maxTemp = result.MaxTemp,
                    minHumidity = result.MinHumidity,
                    maxHumidity = result.MaxHumidity,
                    intervalSeconds = result.IntervalSeconds,
                    status = result.Status
                });

                await SendPublishPacketAsync(
                    stream,
                    writeLock,
                    $"{_topicPrefix}/{result.DeviceId}/config",
                    configJson,
                    retain: true,
                    ct);
            }
            else if (topic.EndsWith("/status", StringComparison.OrdinalIgnoreCase))
            {
                if (jsonPayload.Contains("OFFLINE", StringComparison.OrdinalIgnoreCase) ||
                    jsonPayload.Contains("DISCONNECTED", StringComparison.OrdinalIgnoreCase))
                {
                    using var scope = _scopeFactory.CreateScope();
                    var telemetryService = scope.ServiceProvider.GetRequiredService<IIotTelemetryService>();
                    await telemetryService.ForceDisconnectStateAsync(
                        topicDeviceId,
                        "MSG-ERR-03: Mất kết nối với mạch cảm biến ESP32 (MQTT Last-Will-and-Testament).",
                        ct);

                    _logger.LogWarning("[MQTT Status] Device {DeviceId} reported OFFLINE via MQTT LWT!", topicDeviceId);
                }
                else if (jsonPayload.Contains("ONLINE", StringComparison.OrdinalIgnoreCase))
                {
                    await PublishDeviceConfigAsync(stream, writeLock, topicDeviceId, ct);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("[MQTT Bridge] Error processing message on {Topic}: {Error}", topic, ex.Message);
        }
    }

    private async Task PublishDeviceConfigAsync(
        NetworkStream stream,
        SemaphoreSlim writeLock,
        string deviceId,
        CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var telemetryService = scope.ServiceProvider.GetRequiredService<IIotTelemetryService>();
            var dashboard = await telemetryService.GetMonitoringDashboardAsync(new IotSensorLogQueryDto
            {
                DeviceId = deviceId,
                PageSize = 1
            }, ct);

            var configJson = JsonSerializer.Serialize(new
            {
                deviceId,
                minTemp = dashboard.MinTempThreshold,
                maxTemp = dashboard.MaxTempThreshold,
                minHumidity = dashboard.MinHumidityThreshold,
                maxHumidity = dashboard.MaxHumidityThreshold,
                intervalSeconds = dashboard.IntervalSeconds
            });

            await SendPublishPacketAsync(
                stream,
                writeLock,
                $"{_topicPrefix}/{deviceId}/config",
                configJson,
                retain: true,
                ct);
        }
        catch
        {
            // Non-fatal during startup
        }
    }

    public static async Task PublishConfigImmediatelyAsync(IotTelemetryIngestResponseDto result, CancellationToken ct = default)
    {
        var stream = _activeStream;
        var writeLock = _activeWriteLock;
        if (stream == null || writeLock == null || !stream.CanWrite) return;

        try
        {
            var configJson = JsonSerializer.Serialize(new
            {
                deviceId = result.DeviceId,
                minTemp = result.MinTemp,
                maxTemp = result.MaxTemp,
                minHumidity = result.MinHumidity,
                maxHumidity = result.MaxHumidity,
                intervalSeconds = result.IntervalSeconds,
                status = result.Status
            });

            await SendPublishPacketAsync(
                stream,
                writeLock,
                $"{_activeTopicPrefix}/{result.DeviceId}/config",
                configJson,
                retain: true,
                ct);
        }
        catch
        {
            // Non-fatal: will retry on next telemetry tick
        }
    }

    #region MQTT v3.1.1 Packet Framing Helpers

    private static async Task SendConnectPacketAsync(
        NetworkStream stream,
        string clientId,
        ushort keepAliveSeconds,
        CancellationToken ct)
    {
        using var body = new MemoryStream();
        // Protocol Name "MQTT" (level 4 = v3.1.1)
        WriteMqttString(body, "MQTT");
        body.WriteByte(0x04); // Protocol Level 4
        body.WriteByte(0x02); // Connect Flags: CleanSession = 1
        body.WriteByte((byte)(keepAliveSeconds >> 8));
        body.WriteByte((byte)(keepAliveSeconds & 0xFF));
        WriteMqttString(body, clientId);

        byte[] bodyBytes = body.ToArray();
        using var packet = new MemoryStream();
        packet.WriteByte(0x10); // CONNECT control packet type
        WriteRemainingLength(packet, bodyBytes.Length);
        packet.Write(bodyBytes, 0, bodyBytes.Length);

        byte[] full = packet.ToArray();
        await stream.WriteAsync(full, ct);
        await stream.FlushAsync(ct);
    }

    private static async Task SendSubscribePacketAsync(
        NetworkStream stream,
        ushort packetId,
        string[] topics,
        CancellationToken ct)
    {
        using var body = new MemoryStream();
        body.WriteByte((byte)(packetId >> 8));
        body.WriteByte((byte)(packetId & 0xFF));

        foreach (var t in topics)
        {
            WriteMqttString(body, t);
            body.WriteByte(0x00); // QoS 0
        }

        byte[] bodyBytes = body.ToArray();
        using var packet = new MemoryStream();
        packet.WriteByte(0x82); // SUBSCRIBE control packet type (bits 3..0 must be 0010)
        WriteRemainingLength(packet, bodyBytes.Length);
        packet.Write(bodyBytes, 0, bodyBytes.Length);

        byte[] full = packet.ToArray();
        await stream.WriteAsync(full, ct);
        await stream.FlushAsync(ct);
    }

    private static async Task SendPublishPacketAsync(
        NetworkStream stream,
        SemaphoreSlim writeLock,
        string topic,
        string payload,
        bool retain,
        CancellationToken ct)
    {
        using var body = new MemoryStream();
        WriteMqttString(body, topic);
        byte[] payloadBytes = Encoding.UTF8.GetBytes(payload);
        body.Write(payloadBytes, 0, payloadBytes.Length);

        byte[] bodyBytes = body.ToArray();
        using var packet = new MemoryStream();
        byte header = (byte)(0x30 | (retain ? 0x01 : 0x00));
        packet.WriteByte(header);
        WriteRemainingLength(packet, bodyBytes.Length);
        packet.Write(bodyBytes, 0, bodyBytes.Length);

        byte[] full = packet.ToArray();
        await writeLock.WaitAsync(ct);
        try
        {
            await stream.WriteAsync(full, ct);
            await stream.FlushAsync(ct);
        }
        finally
        {
            writeLock.Release();
        }
    }

    private static void WriteMqttString(Stream stream, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        stream.WriteByte((byte)(bytes.Length >> 8));
        stream.WriteByte((byte)(bytes.Length & 0xFF));
        stream.Write(bytes, 0, bytes.Length);
    }

    private static void WriteRemainingLength(Stream stream, int length)
    {
        do
        {
            byte encodedByte = (byte)(length % 128);
            length /= 128;
            if (length > 0)
            {
                encodedByte |= 128;
            }
            stream.WriteByte(encodedByte);
        } while (length > 0);
    }

    private static async Task<int> ReadRemainingLengthAsync(NetworkStream stream, CancellationToken ct)
    {
        int multiplier = 1;
        int value = 0;
        byte encodedByte;
        do
        {
            byte[] b = await ReadExactAsync(stream, 1, ct);
            encodedByte = b[0];
            value += (encodedByte & 127) * multiplier;
            multiplier *= 128;
            if (multiplier > 128 * 128 * 128)
            {
                throw new IOException("Malformed MQTT Remaining Length.");
            }
        } while ((encodedByte & 128) != 0);

        return value;
    }

    private static async Task<byte[]> ReadExactAsync(NetworkStream stream, int count, CancellationToken ct)
    {
        byte[] buffer = new byte[count];
        int offset = 0;
        while (offset < count)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(offset, count - offset), ct);
            if (read == 0)
            {
                throw new EndOfStreamException("Unexpected EOF while reading MQTT packet.");
            }
            offset += read;
        }
        return buffer;
    }

    #endregion
}
