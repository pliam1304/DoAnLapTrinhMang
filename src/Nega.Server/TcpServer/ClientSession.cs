using System.Net;
using System.Net.Sockets;
using Nega.Common.Models;
using Nega.Common.Protocol;
using Nega.Common.Security;
using Nega.Server.State;

namespace Nega.Server.TcpServer;

/// <summary>
/// Quản lý phiên kết nối TCP song công (Full-Duplex) với một máy trạm cụ thể tại Nega Server.
/// - Luồng Đọc (ReceiveLoop): Giải mã khung nhị phân PacketFramer (0x01 HANDSHAKE, 0x02 SCREEN_FRAME, 0x07 ACK, 0x08 PONG).
/// - Luồng Ghi (SendPacketAsync): Đồng bộ hóa bằng SemaphoreSlim để tránh trộn byte giữa gói Ping và gói lệnh điều khiển.
/// </summary>
public sealed class ClientSession : IAsyncDisposable
{
    private readonly System.Net.Sockets.TcpClient _tcpClient;
    private readonly WorkstationRegistry _registry;
    private readonly TelemetryService _telemetry;
    private readonly Action<string, ClientSession> _onHandshakeRegistered;
    private readonly Action<string> _onDisconnected;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    private NetworkStream? _stream;
    public string? ClientId { get; private set; }
    public string RemoteIp { get; }

    public ClientSession(
        System.Net.Sockets.TcpClient tcpClient,
        WorkstationRegistry registry,
        TelemetryService telemetry,
        Action<string, ClientSession> onHandshakeRegistered,
        Action<string> onDisconnected)
    {
        _tcpClient = tcpClient;
        _tcpClient.NoDelay = true;
        _registry = registry;
        _telemetry = telemetry;
        _onHandshakeRegistered = onHandshakeRegistered;
        _onDisconnected = onDisconnected;
        RemoteIp = (_tcpClient.Client.RemoteEndPoint as IPEndPoint)?.Address.ToString() ?? "127.0.0.1";
    }

    public async Task RunSessionAsync(CancellationToken ct)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        try
        {
            _stream = _tcpClient.GetStream();
            Task readTask = ReceiveLoopAsync(_stream, linkedCts.Token);
            Task pingTask = PingLoopAsync(linkedCts.Token);

            await Task.WhenAny(readTask, pingTask).ConfigureAwait(false);
        }
        catch
        {
            // Session disconnected
        }
        finally
        {
            linkedCts.Cancel();
            if (!string.IsNullOrEmpty(ClientId))
            {
                _registry.MarkTcpDisconnected(ClientId);
                _onDisconnected(ClientId);
            }
        }
    }

    public async Task<bool> SendPacketAsync(PacketEnvelope envelope, CancellationToken ct = default)
    {
        NetworkStream? stream = _stream;
        if (stream is null || !_tcpClient.Connected)
        {
            return false;
        }

        try
        {
            await PacketFramer.WritePacketAsync(stream, envelope.Type, envelope.Payload, _writeLock, ct).ConfigureAwait(false);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task ReceiveLoopAsync(NetworkStream stream, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            PacketEnvelope? packet = await PacketFramer.ReadPacketAsync(stream, ct).ConfigureAwait(false);
            if (packet is null)
            {
                break; // Client đóng kết nối
            }

            switch (packet.Type)
            {
                case PacketType.Handshake:
                {
                    var hs = packet.DeserializeJson<HandshakePayload>();
                    if (hs is not null && !string.IsNullOrWhiteSpace(hs.ClientId))
                    {
                        if (!PacketAuthHelper.VerifyTag(
                            NegaRuntimeConfig.Current.ClusterKey,
                            hs.AuthTag,
                            hs.TimestampUnixMs,
                            hs.ClientId,
                            hs.Hostname,
                            hs.TimestampUnixMs.ToString()))
                        {
                            _telemetry.AddLog("TCP", $"Từ chối Handshake từ {RemoteIp} ({hs.Hostname}): Sai khóa xác thực NEGA_CLUSTER_KEY");
                            return;
                        }

                        ClientId = hs.ClientId;
                        WorkstationState state = _registry.UpsertFromTcpHandshake(hs, RemoteIp);
                        _onHandshakeRegistered(hs.ClientId, this);
                        _telemetry.AddLog("TCP", $"Kết nối TCP thành công: {state.Hostname} ({state.IpAddress})");
                    }
                    break;
                }

                case PacketType.ScreenFrame:
                {
                    if (!string.IsNullOrEmpty(ClientId) && packet.Payload.Length > 0)
                    {
                        _registry.UpdateScreenFrame(ClientId, packet.Payload);
                        _telemetry.IncrementTcpFrames();
                    }
                    break;
                }

                case PacketType.Ack:
                {
                    var ack = packet.DeserializeJson<AckResponsePayload>();
                    if (ack is not null)
                    {
                        _registry.UpdateAckState(ack);
                        _telemetry.RecordAlertAck(ack);
                        _telemetry.AddLog(ack.CommandType, $"[ACK] {ack.Detail}");
                    }
                    break;
                }

                case PacketType.PingPong:
                {
                    var pong = packet.DeserializeJson<PingPongPayload>();
                    if (pong is { IsPong: true } && !string.IsNullOrEmpty(ClientId))
                    {
                        long rtt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - pong.SentAtUnixMs;
                        _registry.UpdatePing(ClientId, rtt);
                    }
                    break;
                }
            }
        }
    }

    private async Task PingLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(ProtocolConstants.TcpPingIntervalMs, ct).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(ClientId))
            {
                var ping = PacketEnvelope.FromJson(PacketType.PingPong, new PingPongPayload
                {
                    IsPong = false,
                    SentAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                });
                bool ok = await SendPacketAsync(ping, ct).ConfigureAwait(false);
                if (!ok)
                {
                    break;
                }
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        try
        {
            _tcpClient.Close();
            _tcpClient.Dispose();
        }
        catch
        {
            // Ignore
        }
        return ValueTask.CompletedTask;
    }
}
