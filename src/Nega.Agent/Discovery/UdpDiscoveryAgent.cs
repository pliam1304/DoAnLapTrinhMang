using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Nega.Common.Models;
using Nega.Common.Networking;
using Nega.Common.Protocol;
using Nega.Common.Security;

namespace Nega.Agent.Discovery;

/// <summary>
/// Tác nhân UDP Broadcast phía máy trạm (Client Agent):
/// - Lắng nghe gói tin UDP Broadcast DISCOVER từ Server trên cổng 9999 (hỗ trợ SO_REUSEADDR / SO_REUSEPORT).
/// - Định kỳ phát gói tin UDP ANNOUNCE (Heartbeat) để Server tự động nhận diện danh sách máy trạm trong LAN.
/// - Khi phát hiện IP và cổng TCP của Server, kích hoạt sự kiện OnServerDiscovered để mở kết nối TCP.
/// </summary>
public sealed class UdpDiscoveryAgent
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _clientId;
    private readonly string _hostname;
    private readonly string _osDescription;
    private readonly string _ipAddress;
    private readonly bool _isSimulated;
    private readonly int _udpPort;
    private readonly Func<bool> _isLockedProvider;

    public event Action<string, int>? OnServerDiscovered;

    public UdpDiscoveryAgent(
        string clientId,
        string hostname,
        string osDescription,
        string ipAddress,
        bool isSimulated,
        int udpPort,
        Func<bool> isLockedProvider)
    {
        _clientId = clientId;
        _hostname = hostname;
        _osDescription = osDescription;
        _ipAddress = ipAddress;
        _isSimulated = isSimulated;
        _udpPort = udpPort;
        _isLockedProvider = isLockedProvider;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        socket.ExclusiveAddressUse = false;
        socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        socket.EnableBroadcast = true;

        // Trên macOS / BSD: Bật SO_REUSEPORT (0x0200) để nhiều Client trên cùng 1 máy đều nhận được gói UDP Broadcast
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

        try
        {
            socket.Bind(new IPEndPoint(IPAddress.Any, _udpPort));
        }
        catch
        {
            // Fallback sang cổng ngẫu nhiên nếu hệ điều hành không cho bind trùng cổng
            socket.Bind(new IPEndPoint(IPAddress.Any, 0));
        }

        using var udpClient = new UdpClient { Client = socket };
        Task announceLoop = PeriodicAnnounceLoopAsync(udpClient, ct);
        Task listenLoop = ListenDiscoverLoopAsync(udpClient, ct);

        await Task.WhenAll(announceLoop, listenLoop).ConfigureAwait(false);
    }

    private async Task ListenDiscoverLoopAsync(UdpClient udpClient, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                UdpReceiveResult result = await udpClient.ReceiveAsync(ct).ConfigureAwait(false);
                string json = Encoding.UTF8.GetString(result.Buffer);
                if (!json.Contains("\"DISCOVER\"", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var discover = JsonSerializer.Deserialize<UdpDiscoverMessage>(json, JsonOptions);
                if (discover is null || !string.Equals(discover.Type, "DISCOVER", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string clusterKey = NegaRuntimeConfig.Current.ClusterKey;
                if (!PacketAuthHelper.VerifyTag(
                    clusterKey,
                    discover.AuthTag,
                    discover.TimestampUnixMs,
                    discover.ServerName,
                    discover.TcpPort.ToString(),
                    discover.TimestampUnixMs.ToString()))
                {
                    continue; // Bỏ qua gói DISCOVER không khớp NEGA_CLUSTER_KEY
                }

                string serverIp = result.RemoteEndPoint.Address.ToString();
                if (serverIp == "0.0.0.0" || serverIp.StartsWith("255."))
                {
                    serverIp = "127.0.0.1";
                }

                // Phản hồi tức thì gói ANNOUNCE về cho Server
                await SendAnnouncePacketAsync(udpClient, new IPEndPoint(result.RemoteEndPoint.Address, _udpPort)).ConfigureAwait(false);

                int tcpPort = discover.TcpPort > 0 ? discover.TcpPort : ProtocolConstants.DefaultTcpControlPort;
                OnServerDiscovered?.Invoke(serverIp, tcpPort);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                await Task.Delay(500, ct).ConfigureAwait(false);
            }
        }
    }

    private async Task PeriodicAnnounceLoopAsync(UdpClient udpClient, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            foreach (IPAddress bcast in SubnetBroadcastHelper.GetBroadcastAddresses())
            {
                try
                {
                    await SendAnnouncePacketAsync(udpClient, new IPEndPoint(bcast, _udpPort)).ConfigureAwait(false);
                }
                catch
                {
                    // Ignore unreachable broadcast route on specific interface
                }
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

    private async Task SendAnnouncePacketAsync(UdpClient udpClient, IPEndPoint target)
    {
        long ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var announce = new UdpAnnounceMessage
        {
            Type = "ANNOUNCE",
            ClientId = _clientId,
            Hostname = _hostname,
            OsDescription = _osDescription,
            IpAddress = _ipAddress,
            ScreenWidth = 1920,
            ScreenHeight = 1080,
            IsLocked = _isLockedProvider(),
            IsSimulated = _isSimulated,
            TimestampUnixMs = ts,
            AuthTag = PacketAuthHelper.ComputeTag(
                NegaRuntimeConfig.Current.ClusterKey,
                _clientId,
                _hostname,
                ts.ToString())
        };

        byte[] bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(announce, JsonOptions));
        await udpClient.SendAsync(bytes, bytes.Length, target).ConfigureAwait(false);
    }
}
