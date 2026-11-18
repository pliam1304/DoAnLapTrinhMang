using System.Net.Sockets;
using Nega.Agent.Capture;
using Nega.Agent.Control;
using Nega.Common.Models;
using Nega.Common.Protocol;
using Nega.Common.Security;

namespace Nega.Agent.TcpClient;

/// <summary>
/// Tác nhân kết nối TCP thời gian thực phía máy trạm (Client Agent):
/// - Tự động kết nối tới Server (Port 8888) ngay khi nhận diện qua UDP Broadcast.
/// - Gửi gói 0x01 HANDSHAKE định danh máy trạm.
/// - Duy trì 2 luồng bất đồng bộ tách biệt:
///   1) Luồng Chụp &amp; Gửi ảnh JPEG nén (0x02 SCREEN_FRAME) qua LatestFrameSlot (chống trễ tích tụ).
///   2) Luồng Nhận &amp; Thực thi lệnh điều khiển (0x03 LOCK, 0x04 SHUTDOWN, 0x05 ALERT, 0x06 STREAM_CONFIG, 0x08 PING).
/// - Tự động kết nối lại (Auto-Reconnect) và tự mở khóa Fail-safe nếu mất kết nối Server quá 30 giây.
/// </summary>
public sealed class TcpClientAgent
{
    private readonly string _clientId;
    private readonly string _hostname;
    private readonly string _osDescription;
    private readonly string _ipAddress;
    private readonly bool _isSimulated;
    private readonly ScreenCaptureService _captureService;
    private readonly RemoteControlExecutor _controlExecutor;
    private readonly LatestFrameSlot _frameSlot = new();

    private StreamConfigPayload _streamConfig = StreamConfigPayload.CreateGridDefault();
    private string _targetServerIp = "127.0.0.1";
    private int _targetTcpPort = ProtocolConstants.DefaultTcpControlPort;
    private volatile bool _isConnected;
    private NetworkStream? _activeStream;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private DateTimeOffset? _disconnectedSinceUtc;

    public bool IsConnected => _isConnected;
    public RemoteControlExecutor ControlExecutor => _controlExecutor;

    public TcpClientAgent(
        string clientId,
        string hostname,
        string osDescription,
        string ipAddress,
        bool isSimulated,
        int themeSeed = 0)
    {
        _clientId = clientId;
        _hostname = hostname;
        _osDescription = osDescription;
        _ipAddress = ipAddress;
        _isSimulated = isSimulated;
        _captureService = new ScreenCaptureService(hostname, ipAddress, isSimulated, themeSeed);
        _controlExecutor = new RemoteControlExecutor(clientId, hostname, isSimulated, SendAckAsync);
    }

    public void UpdateDiscoveredServer(string serverIp, int tcpPort)
    {
        _targetServerIp = serverIp;
        _targetTcpPort = tcpPort;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        Task captureLoop = CaptureProducerLoopAsync(ct);
        Task connectionLoop = ConnectionLifecycleLoopAsync(ct);
        await Task.WhenAll(captureLoop, connectionLoop).ConfigureAwait(false);
    }

    private async Task ConnectionLifecycleLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            using var tcpClient = new System.Net.Sockets.TcpClient
            {
                NoDelay = true
            };

            try
            {
                using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                connectCts.CancelAfter(TimeSpan.FromSeconds(4));

                await tcpClient.ConnectAsync(_targetServerIp, _targetTcpPort, connectCts.Token).ConfigureAwait(false);
                using NetworkStream stream = tcpClient.GetStream();
                _activeStream = stream;
                _isConnected = true;
                _disconnectedSinceUtc = null;
                _streamConfig = StreamConfigPayload.CreateGridDefault();

                // 1. Gửi gói 0x01 HANDSHAKE ngay khi kết nối thành công
                long hsTs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                var cfgSec = NegaRuntimeConfig.Current;
                string displayUser = cfgSec.MaskHostIdentity ? "Lab-Student" : Environment.UserName;
                var handshake = PacketEnvelope.FromJson(PacketType.Handshake, new HandshakePayload
                {
                    ClientId = _clientId,
                    Hostname = _hostname,
                    OsDescription = _osDescription,
                    Username = displayUser,
                    IpAddress = _ipAddress,
                    ScreenWidth = 1920,
                    ScreenHeight = 1080,
                    IsSimulated = _isSimulated,
                    TimestampUnixMs = hsTs,
                    AuthTag = PacketAuthHelper.ComputeTag(cfgSec.ClusterKey, _clientId, _hostname, hsTs.ToString())
                });
                await PacketFramer.WritePacketAsync(stream, handshake.Type, handshake.Payload, _writeLock, ct).ConfigureAwait(false);
                Console.WriteLine($"[TCP Connected] {_hostname} ({_ipAddress}) đã kết nối tới Server {_targetServerIp}:{_targetTcpPort}");

                // Gửi ngay frame JPEG đầu tiên để Server hiển thị tức thì không phải đợi chu kỳ kế tiếp
                byte[] initialJpeg = await _captureService.CaptureJpegFrameAsync(
                    _streamConfig,
                    _controlExecutor.IsLocked,
                    _controlExecutor.ActiveAlertTitle,
                    _controlExecutor.ActiveAlertMessage,
                    _controlExecutor.ShutdownRemainingSeconds,
                    ct).ConfigureAwait(false);
                await PacketFramer.WritePacketAsync(stream, PacketType.ScreenFrame, initialJpeg, _writeLock, ct).ConfigureAwait(false);

                // 2. Chạy song song luồng Gửi JPEG Frame và luồng Nhận Lệnh Điều khiển
                using var sessionCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                Task senderTask = FrameSenderLoopAsync(stream, sessionCts.Token);
                Task receiverTask = CommandReceiverLoopAsync(stream, sessionCts.Token);

                await Task.WhenAny(senderTask, receiverTask).ConfigureAwait(false);
                sessionCts.Cancel();
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch
            {
                // Kết nối thất bại hoặc bị ngắt -> chuyển sang chế độ chờ kết nối lại
            }
            finally
            {
                _isConnected = false;
                _activeStream = null;
                _disconnectedSinceUtc ??= DateTimeOffset.UtcNow;

                // Kiểm tra Fail-safe Auto-Unlock nếu mất kết nối quá 30 giây trong lúc đang khóa
                if (_controlExecutor.IsLocked &&
                    (DateTimeOffset.UtcNow - _disconnectedSinceUtc.Value).TotalSeconds >= ProtocolConstants.FailSafeAutoUnlockSeconds)
                {
                    _controlExecutor.TriggerFailSafeUnlockIfDisconnected();
                }
            }

            try
            {
                await Task.Delay(1500, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task CaptureProducerLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (_isConnected)
                {
                    StreamConfigPayload cfg = _streamConfig;
                    byte[] jpeg = await _captureService.CaptureJpegFrameAsync(
                        cfg,
                        _controlExecutor.IsLocked,
                        _controlExecutor.ActiveAlertTitle,
                        _controlExecutor.ActiveAlertMessage,
                        _controlExecutor.ShutdownRemainingSeconds,
                        ct).ConfigureAwait(false);

                    // Đẩy vào Single-Slot Conflation Queue (ghi đè frame cũ nếu mạng chưa kịp gửi)
                    _frameSlot.PublishFrame(jpeg);

                    int fps = Math.Clamp(cfg.Fps, 1, 20);
                    await Task.Delay(1000 / fps, ct).ConfigureAwait(false);
                }
                else
                {
                    await Task.Delay(500, ct).ConfigureAwait(false);
                }
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

    private async Task FrameSenderLoopAsync(NetworkStream stream, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            byte[]? jpegBytes = await _frameSlot.WaitAndTakeFrameAsync(ct).ConfigureAwait(false);
            if (jpegBytes is { Length: > 0 })
            {
                await PacketFramer.WritePacketAsync(
                    stream,
                    PacketType.ScreenFrame,
                    jpegBytes,
                    _writeLock,
                    ct).ConfigureAwait(false);
            }
        }
    }

    private async Task CommandReceiverLoopAsync(NetworkStream stream, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            PacketEnvelope? packet = await PacketFramer.ReadPacketAsync(stream, ct).ConfigureAwait(false);
            if (packet is null)
            {
                break; // Server đóng kết nối
            }

            switch (packet.Type)
            {
                case PacketType.CmdLockInput:
                {
                    var cmd = packet.DeserializeJson<LockInputPayload>();
                    if (cmd is not null)
                    {
                        await _controlExecutor.ExecuteLockCommandAsync(cmd).ConfigureAwait(false);
                    }
                    break;
                }

                case PacketType.CmdAlert:
                {
                    var alert = packet.DeserializeJson<EmergencyAlertPayload>();
                    if (alert is not null)
                    {
                        _controlExecutor.ExecuteEmergencyAlert(alert);
                    }
                    break;
                }

                case PacketType.CmdShutdown:
                {
                    var shutdown = packet.DeserializeJson<ShutdownCommandPayload>();
                    if (shutdown is not null)
                    {
                        await _controlExecutor.ExecuteShutdownCommandAsync(shutdown).ConfigureAwait(false);
                    }
                    break;
                }

                case PacketType.CmdStreamConfig:
                {
                    var cfg = packet.DeserializeJson<StreamConfigPayload>();
                    if (cfg is not null)
                    {
                        _streamConfig = cfg;
                    }
                    break;
                }

                case PacketType.PingPong:
                {
                    var ping = packet.DeserializeJson<PingPongPayload>();
                    if (ping is { IsPong: false })
                    {
                        var pong = PacketEnvelope.FromJson(PacketType.PingPong, new PingPongPayload
                        {
                            IsPong = true,
                            SentAtUnixMs = ping.SentAtUnixMs
                        });
                        await PacketFramer.WritePacketAsync(stream, pong.Type, pong.Payload, _writeLock, ct).ConfigureAwait(false);
                    }
                    break;
                }
            }
        }
    }

    private async Task SendAckAsync(AckResponsePayload ack)
    {
        NetworkStream? stream = _activeStream;
        if (stream is null || !_isConnected)
        {
            return;
        }

        try
        {
            var packet = PacketEnvelope.FromJson(PacketType.Ack, ack);
            await PacketFramer.WritePacketAsync(stream, packet.Type, packet.Payload, _writeLock).ConfigureAwait(false);
        }
        catch
        {
            // Ignore if socket dropped during ACK
        }
    }
}
