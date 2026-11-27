using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
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
/// Hardened Background Service connecting BMS Backend to an MQTT v3.1.1 Broker over TLS (MQTTS port 8883)
/// with optional Username/Password authentication and per-message DeviceKey / HMAC-SHA256 verification.
/// Conforms to: UC65, UC66, UC67, UC68, UC69, MSG-ERR-03 &amp; ISO 15189 Cold-Chain Security.
/// </summary>
public sealed class IotMqttBridgeHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<IotMqttBridgeHostedService> _logger;

    private string _brokerHost;
    private int _brokerPort;
    private bool _useTls;
    private string? _username;
    private string? _password;
    private bool _requireDeviceAuth;
    private string _topicPrefix;
    private string _defaultDeviceId;
    private string _deviceApiKey;

    private static Stream? _activeStream;
    private static SemaphoreSlim? _activeWriteLock;
    private static string _activeTopicPrefix = "bms/sep490/iot/hkd-lab01";
    private static string _activeDeviceApiKey = string.Empty;
    private static CancellationTokenSource? _sessionCts;

    public static bool IsConnected => _activeStream?.CanWrite == true;
    public static bool ActiveUseTls { get; private set; }

    public static void RequestReconnect()
    {
        try { _sessionCts?.Cancel(); } catch { }
    }

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

        _brokerHost = FirstNonEmpty(_configuration["IoT:MqttBrokerHost"]) ?? "broker.emqx.io";
        _brokerPort = int.TryParse(_configuration["IoT:MqttBrokerPort"], out var port) && port > 0 ? port : 8883;
        _useTls = bool.TryParse(_configuration["IoT:MqttUseTls"], out var tls) ? tls : _brokerPort == 8883;
        _username = FirstNonEmpty(_configuration["IoT:MqttUsername"]);
        _password = FirstNonEmpty(_configuration["IoT:MqttPassword"]);
        _requireDeviceAuth = !bool.TryParse(_configuration["IoT:RequireMqttDeviceAuth"], out var reqAuth) || reqAuth;
        _topicPrefix = (FirstNonEmpty(_configuration["IoT:MqttTopicPrefix"]) ?? "bms/sep490/iot/hkd-lab01").TrimEnd('/');
        _defaultDeviceId = FirstNonEmpty(_configuration["IoT:DefaultDeviceId"]) ?? "BMS-IOT-001";
        _deviceApiKey = FirstNonEmpty(_configuration["IoT:DeviceApiKey"]) ?? string.Empty;
        _activeDeviceApiKey = _deviceApiKey;
    }

    private static string? FirstNonEmpty(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "[MQTT Bridge] Starting hardened IoT MQTT Bridge targeting {Scheme}://{Broker}:{Port} (Topic prefix: {Prefix}/#, AuthUser={HasUser}, PayloadKeyAuth={HasKey})",
            _useTls ? "mqtts" : "mqtt",
            _brokerHost,
            _brokerPort,
            _topicPrefix,
            !string.IsNullOrEmpty(_username),
            !string.IsNullOrEmpty(_deviceApiKey));

        while (!stoppingToken.IsCancellationRequested)
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            _sessionCts = linkedCts;
            try
            {
                await RefreshConfigFromDatabaseAsync(stoppingToken);
                await RunMqttSessionAsync(linkedCts.Token);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation(
                    "[MQTT Bridge] Configuration updated. Reconnecting to {Scheme}://{Broker}:{Port} ({Prefix})...",
                    _useTls ? "mqtts" : "mqtt", _brokerHost, _brokerPort, _topicPrefix);
                await Task.Delay(TimeSpan.FromMilliseconds(300), stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("[MQTT Bridge] Connection interrupted ({Message}). Reconnecting in 3s...", ex.Message);
                await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
            }
            finally
            {
                _activeStream = null;
                _activeWriteLock = null;
                ActiveUseTls = false;
                if (ReferenceEquals(_sessionCts, linkedCts)) _sessionCts = null;
            }
        }
    }

    private async Task RefreshConfigFromDatabaseAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<BMS.Core.Interfaces.IIotSensorLogRepository>();
            var dbConfig = await repo.GetMqttConfigAsync(ct);
            if (dbConfig != null)
            {
                if (!string.IsNullOrWhiteSpace(dbConfig.BrokerHost)) _brokerHost = dbConfig.BrokerHost.Trim();
                if (dbConfig.BrokerPort > 0) _brokerPort = dbConfig.BrokerPort;
                _useTls = dbConfig.UseTls || _brokerPort == 8883;
                if (!string.IsNullOrWhiteSpace(dbConfig.Username)) _username = dbConfig.Username.Trim();
                if (!string.IsNullOrWhiteSpace(dbConfig.Password)) _password = dbConfig.Password;
                _requireDeviceAuth = dbConfig.RequireDeviceAuth;
                if (!string.IsNullOrWhiteSpace(dbConfig.TopicPrefix)) _topicPrefix = dbConfig.TopicPrefix.Trim().TrimEnd('/');
                if (!string.IsNullOrWhiteSpace(dbConfig.DefaultDeviceId)) _defaultDeviceId = dbConfig.DefaultDeviceId.Trim();
            }

            _deviceApiKey = FirstNonEmpty(_configuration["IoT:DeviceApiKey"]) ?? string.Empty;
            _activeDeviceApiKey = _deviceApiKey;
        }
        catch { }
    }

    private async Task RunMqttSessionAsync(CancellationToken ct)
    {
        using var tcp = new TcpClient();
        tcp.NoDelay = true;
        using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        connectCts.CancelAfter(TimeSpan.FromSeconds(12));

        await tcp.ConnectAsync(_brokerHost, _brokerPort, connectCts.Token);
        Stream stream = tcp.GetStream();
        SslStream? sslStream = null;

        try
        {
            if (_useTls)
            {
                sslStream = new SslStream(stream, leaveInnerStreamOpen: false);
                var sslOptions = new SslClientAuthenticationOptions
                {
                    TargetHost = _brokerHost,
                    EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13
                };
                await sslStream.AuthenticateAsClientAsync(sslOptions, connectCts.Token);
                stream = sslStream;
            }

            using var writeLock = new SemaphoreSlim(1, 1);

            var clientId = $"bms-be-{Environment.MachineName}-{Guid.NewGuid().ToString("N")[..6]}";
            await SendConnectPacketAsync(stream, clientId, keepAliveSeconds: 30, _username, _password, ct);

            // Read CONNACK (0x20, 0x02, flags, returnCode)
            var connAckHeader = await ReadExactAsync(stream, 4, ct);
            if (connAckHeader[0] != 0x20 || connAckHeader[3] != 0x00)
            {
                throw new IOException($"MQTT Broker rejected CONNECT with return code {connAckHeader[3]}.");
            }

            // Subscribe to telemetry and status topics for all BMS IoT devices
            var telemetryFilter = $"{_topicPrefix}/+/telemetry";
            var statusFilter = $"{_topicPrefix}/+/status";
            await SendSubscribePacketAsync(stream, packetId: 1, [telemetryFilter, statusFilter], ct);

            _logger.LogInformation(
                "[MQTT Bridge] Connected via {Scheme} to {Broker}:{Port} as {ClientId}. Subscribed to '{Telemetry}' & '{Status}'.",
                _useTls ? "MQTTS (TLS)" : "MQTT", _brokerHost, _brokerPort, clientId, telemetryFilter, statusFilter);

            _activeStream = stream;
            _activeWriteLock = writeLock;
            _activeTopicPrefix = _topicPrefix;
            ActiveUseTls = _useTls;
            await PublishDeviceConfigAsync(stream, writeLock, _defaultDeviceId, ct);

            // Run concurrent keep-alive pinger while reader awaits packets on Stream (supports both SslStream & NetworkStream)
            using var pingCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var pingTask = RunKeepAlivePingLoopAsync(stream, writeLock, pingCts.Token);

            try
            {
                while (!ct.IsCancellationRequested && tcp.Connected)
                {
                    await ReadAndProcessNextPacketAsync(stream, writeLock, ct);
                }
            }
            finally
            {
                pingCts.Cancel();
                try { await pingTask; } catch { }
            }
        }
        finally
        {
            if (sslStream != null)
            {
                await sslStream.DisposeAsync();
            }
            else
            {
                await stream.DisposeAsync();
            }
        }
    }

    private static async Task RunKeepAlivePingLoopAsync(Stream stream, SemaphoreSlim writeLock, CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
        while (await timer.WaitForNextTickAsync(ct))
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
        }
    }

    private async Task ReadAndProcessNextPacketAsync(Stream stream, SemaphoreSlim writeLock, CancellationToken ct)
    {
        byte[] headerBuf = await ReadExactAsync(stream, 1, ct);
        int firstByte = headerBuf[0];

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

    private bool VerifyMqttPayloadAuthenticity(IngestIotTelemetryRequestDto dto, string deviceId)
    {
        if (!_requireDeviceAuth || string.IsNullOrWhiteSpace(_deviceApiKey))
        {
            return true;
        }

        // 1. Direct pre-shared key check (constant-time)
        if (!string.IsNullOrWhiteSpace(dto.DeviceKey) &&
            ConstantTimeEquals(dto.DeviceKey.Trim(), _deviceApiKey))
        {
            return true;
        }

        // 2. Optional HMAC-SHA256 signature verification
        if (!string.IsNullOrWhiteSpace(dto.Signature))
        {
            var tempStr = dto.Temperature.HasValue ? dto.Temperature.Value.ToString("F2", CultureInfo.InvariantCulture) : "";
            var humStr = dto.Humidity.HasValue ? dto.Humidity.Value.ToString("F2", CultureInfo.InvariantCulture) : "";
            var canonical = $"{deviceId}|{tempStr}|{humStr}";
            var expectedHmac = ComputeHmacSha256Hex(canonical, _deviceApiKey);
            if (ConstantTimeEquals(dto.Signature.Trim().ToLowerInvariant(), expectedHmac))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ConstantTimeEquals(string a, string b)
    {
        var aBytes = Encoding.UTF8.GetBytes(a);
        var bBytes = Encoding.UTF8.GetBytes(b);
        return aBytes.Length == bBytes.Length && CryptographicOperations.FixedTimeEquals(aBytes, bBytes);
    }

    private static string ComputeHmacSha256Hex(string message, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(message));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string BuildSignedConfigJson(
        string deviceId,
        double minTemp,
        double maxTemp,
        double minHumidity,
        double maxHumidity,
        int intervalSeconds,
        string? status = null)
    {
        string? configHmac = null;
        if (!string.IsNullOrWhiteSpace(_activeDeviceApiKey))
        {
            var canonical = string.Create(
                CultureInfo.InvariantCulture,
                $"{deviceId}|{minTemp:F2}|{maxTemp:F2}|{minHumidity:F2}|{maxHumidity:F2}|{intervalSeconds}");
            configHmac = ComputeHmacSha256Hex(canonical, _activeDeviceApiKey);
        }

        return JsonSerializer.Serialize(new
        {
            deviceId,
            minTemp,
            maxTemp,
            minHumidity,
            maxHumidity,
            intervalSeconds,
            status,
            configHmac
        });
    }

    private async Task HandleIncomingMqttMessageAsync(
        string topic,
        string jsonPayload,
        Stream stream,
        SemaphoreSlim writeLock,
        CancellationToken ct)
    {
        try
        {
            string relative = topic.StartsWith(_topicPrefix + "/", StringComparison.OrdinalIgnoreCase)
                ? topic[(_topicPrefix.Length + 1)..]
                : topic;
            var segments = relative.Split('/', StringSplitOptions.RemoveEmptyEntries);
            string topicDeviceId = segments.Length >= 2 ? segments[0] : _defaultDeviceId;

            if (topic.EndsWith("/telemetry", StringComparison.OrdinalIgnoreCase))
            {
                var dto = JsonSerializer.Deserialize<IngestIotTelemetryRequestDto>(jsonPayload, _jsonOptions);
                if (dto == null) return;

                if (string.IsNullOrWhiteSpace(dto.DeviceId))
                {
                    dto.DeviceId = topicDeviceId;
                }

                if (!VerifyMqttPayloadAuthenticity(dto, dto.DeviceId))
                {
                    _logger.LogWarning(
                        "[MQTT Security] Rejected unauthenticated telemetry packet on topic '{Topic}' for device '{DeviceId}' (missing/invalid deviceKey or signature).",
                        topic, dto.DeviceId);
                    return;
                }

                using var scope = _scopeFactory.CreateScope();
                var telemetryService = scope.ServiceProvider.GetRequiredService<IIotTelemetryService>();
                var result = await telemetryService.IngestTelemetryAsync(dto, ct);

                _logger.LogDebug(
                    "[MQTT Telemetry] Received authenticated packet from {DeviceId}: {Temp:F1}°C, {Hum:F1}% (Buffered synced: {Buf})",
                    result.DeviceId, result.Temperature, result.Humidity, result.BufferedSyncedCount);

                var configJson = BuildSignedConfigJson(
                    result.DeviceId,
                    result.MinTemp,
                    result.MaxTemp,
                    result.MinHumidity,
                    result.MaxHumidity,
                    result.IntervalSeconds,
                    result.Status);

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
        Stream stream,
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

            var configJson = BuildSignedConfigJson(
                deviceId,
                dashboard.MinTempThreshold,
                dashboard.MaxTempThreshold,
                dashboard.MinHumidityThreshold,
                dashboard.MaxHumidityThreshold,
                dashboard.IntervalSeconds);

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
            var configJson = BuildSignedConfigJson(
                result.DeviceId,
                result.MinTemp,
                result.MaxTemp,
                result.MinHumidity,
                result.MaxHumidity,
                result.IntervalSeconds,
                result.Status);

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
        Stream stream,
        string clientId,
        ushort keepAliveSeconds,
        string? username,
        string? password,
        CancellationToken ct)
    {
        bool hasUser = !string.IsNullOrEmpty(username);
        bool hasPass = hasUser && !string.IsNullOrEmpty(password);

        byte connectFlags = 0x02; // CleanSession = 1
        if (hasUser) connectFlags |= 0x80;
        if (hasPass) connectFlags |= 0x40;

        using var body = new MemoryStream();
        WriteMqttString(body, "MQTT");
        body.WriteByte(0x04); // Protocol Level 4 (MQTT 3.1.1)
        body.WriteByte(connectFlags);
        body.WriteByte((byte)(keepAliveSeconds >> 8));
        body.WriteByte((byte)(keepAliveSeconds & 0xFF));
        WriteMqttString(body, clientId);
        if (hasUser) WriteMqttString(body, username!);
        if (hasPass) WriteMqttString(body, password!);

        byte[] bodyBytes = body.ToArray();
        using var packet = new MemoryStream();
        packet.WriteByte(0x10); // CONNECT
        WriteRemainingLength(packet, bodyBytes.Length);
        packet.Write(bodyBytes, 0, bodyBytes.Length);

        byte[] full = packet.ToArray();
        await stream.WriteAsync(full, ct);
        await stream.FlushAsync(ct);
    }

    private static async Task SendSubscribePacketAsync(
        Stream stream,
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
        packet.WriteByte(0x82); // SUBSCRIBE
        WriteRemainingLength(packet, bodyBytes.Length);
        packet.Write(bodyBytes, 0, bodyBytes.Length);

        byte[] full = packet.ToArray();
        await stream.WriteAsync(full, ct);
        await stream.FlushAsync(ct);
    }

    private static async Task SendPublishPacketAsync(
        Stream stream,
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

    private static async Task<int> ReadRemainingLengthAsync(Stream stream, CancellationToken ct)
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

    private static async Task<byte[]> ReadExactAsync(Stream stream, int count, CancellationToken ct)
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
