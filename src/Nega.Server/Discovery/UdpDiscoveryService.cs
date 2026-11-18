using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Nega.Common.Models;
using Nega.Common.Networking;
using Nega.Common.Protocol;
using Nega.Common.Security;
using Nega.Server.State;

namespace Nega.Server.Discovery;

/// <summary>
/// Dịch vụ Khám phá phòng máy qua UDP Broadcast (Port 9999) phía Nega Server:
/// - Định kỳ mỗi 3 giây phát gói tin UDP Broadcast DISCOVER ra tất cả các card mạng LAN/Wi-Fi.
/// - Lắng nghe gói tin UDP ANNOUNCE từ các máy trạm để tự động nhận diện danh sách máy trạm trong LAN.
/// </summary>
public sealed class UdpDiscoveryService : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly WorkstationRegistry _registry;
    private readonly TelemetryService _telemetry;
    private readonly int _udpPort;
    private readonly int _tcpPort;
    private UdpClient? _udpClient;

    public UdpDiscoveryService(
        WorkstationRegistry registry,
        TelemetryService telemetry,
        int udpPort = ProtocolConstants.DefaultUdpDiscoveryPort,
        int tcpPort = ProtocolConstants.DefaultTcpControlPort)
    {
        _registry = registry;
        _telemetry = telemetry;
        _udpPort = udpPort;
        _tcpPort = tcpPort;
    }

    /// <summary>
    /// Cho phép người dùng bấm nút "Quét LAN ngay" trên giao diện Nega Dashboard để phát tức thì gói UDP Broadcast.
    /// </summary>
    public async Task TriggerManualBroadcastAsync()
    {
        if (_udpClient is not null)
        {
            await SendDiscoverBroadcastAsync(_udpClient).ConfigureAwait(false);
            _telemetry.AddLog("UDP", $"Đã phát gói UDP Broadcast DISCOVER lên cổng :{_udpPort}");
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        socket.ExclusiveAddressUse = false;
        socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        socket.EnableBroadcast = true;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            try
            {
                socket.SetSocketOption(SocketOptionLevel.Socket, (SocketOptionName)0x0200, true);
            }
            catch
            {
                // Ignore if unsupported
            }
        }

        socket.Bind(new IPEndPoint(IPAddress.Any, _udpPort));
        using var udpClient = new UdpClient { Client = socket };
        _udpClient = udpClient;

        _telemetry.AddLog("UDP", $"Khởi động dịch vụ UDP Broadcast Discovery trên cổng :{_udpPort}");

        Task broadcastTask = PeriodicBroadcastLoopAsync(udpClient, stoppingToken);
        Task listenTask = ListenAnnounceLoopAsync(udpClient, stoppingToken);

        await Task.WhenAll(broadcastTask, listenTask).ConfigureAwait(false);
    }

    private async Task PeriodicBroadcastLoopAsync(UdpClient udpClient, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await SendDiscoverBroadcastAsync(udpClient).ConfigureAwait(false);
            }
            catch
            {
                // Ignore transient UDP broadcast errors
            }

            try
            {
                await Task.Delay(ProtocolConstants.UdpBroadcastIntervalMs, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task ListenAnnounceLoopAsync(UdpClient udpClient, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                UdpReceiveResult result = await udpClient.ReceiveAsync(ct).ConfigureAwait(false);
                string json = Encoding.UTF8.GetString(result.Buffer);
                if (!json.Contains("\"ANNOUNCE\"", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var announce = JsonSerializer.Deserialize<UdpAnnounceMessage>(json, JsonOptions);
                if (announce is null ||
                    !string.Equals(announce.Type, "ANNOUNCE", StringComparison.OrdinalIgnoreCase) ||
                    string.IsNullOrWhiteSpace(announce.ClientId))
                {
                    continue;
                }

                if (!PacketAuthHelper.VerifyTag(
                    NegaRuntimeConfig.Current.ClusterKey,
                    announce.AuthTag,
                    announce.TimestampUnixMs,
                    announce.ClientId,
                    announce.Hostname,
                    announce.TimestampUnixMs.ToString()))
                {
                    continue; // Bỏ qua máy trạm không khớp NEGA_CLUSTER_KEY
                }

                _telemetry.IncrementUdpPackets();
                var (state, isNew) = _registry.UpsertFromUdpDiscovery(announce, result.RemoteEndPoint.Address.ToString());
                if (isNew)
                {
                    _telemetry.AddLog("UDP", $"Phát hiện máy trạm mới qua UDP Broadcast: {state.Hostname} ({state.IpAddress})");
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                await Task.Delay(300, ct).ConfigureAwait(false);
            }
        }
    }

    private async Task SendDiscoverBroadcastAsync(UdpClient udpClient)
    {
        var cfg = NegaRuntimeConfig.Current;
        string serverName = cfg.MaskHostIdentity ? cfg.ServerDisplayName : Environment.MachineName;
        long ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var discover = new UdpDiscoverMessage
        {
            Type = "DISCOVER",
            ServerName = serverName,
            TcpPort = _tcpPort,
            TimestampUnixMs = ts,
            AuthTag = PacketAuthHelper.ComputeTag(cfg.ClusterKey, serverName, _tcpPort.ToString(), ts.ToString())
        };

        byte[] payload = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(discover, JsonOptions));
        foreach (IPAddress broadcastIp in SubnetBroadcastHelper.GetBroadcastAddresses())
        {
            try
            {
                await udpClient.SendAsync(payload, payload.Length, new IPEndPoint(broadcastIp, _udpPort)).ConfigureAwait(false);
                _telemetry.IncrementUdpPackets();
            }
            catch
            {
                // Bỏ qua các card mạng không hỗ trợ route broadcast trực tiếp
            }
        }
    }
}
