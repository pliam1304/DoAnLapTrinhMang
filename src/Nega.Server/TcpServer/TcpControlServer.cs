using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Nega.Common.Models;
using Nega.Common.Protocol;
using Nega.Common.Security;
using Nega.Server.State;

namespace Nega.Server.TcpServer;

/// <summary>
/// Máy chủ điều khiển TCP trung tâm của Nega (Port 8888):
/// - Chấp nhận kết nối đồng thời từ nhiều máy trạm trong phòng LAN.
/// - Quản lý danh sách phiên kết nối (ClientSession).
/// - Cung cấp các phương thức gửi lệnh điều khiển từ xa tới 1 máy hoặc toàn bộ phòng máy:
///   1) Khóa / Mở khóa chuột và bàn phím (0x03 CMD_LOCK_INPUT)
///   2) Tắt máy từ xa / Hủy tắt máy (0x04 CMD_SHUTDOWN)
///   3) Gửi thông báo khẩn cấp (0x05 CMD_ALERT)
///   4) Chuyển đổi chế độ luồng ảnh JPEG Grid / Zoom HD (0x06 CMD_STREAM_CONFIG)
/// </summary>
public sealed class TcpControlServer : BackgroundService
{
    private readonly WorkstationRegistry _registry;
    private readonly TelemetryService _telemetry;
    private readonly int _tcpPort;
    private readonly ConcurrentDictionary<string, ClientSession> _sessions = new(StringComparer.OrdinalIgnoreCase);

    public TcpControlServer(
        WorkstationRegistry registry,
        TelemetryService telemetry,
        int tcpPort = ProtocolConstants.DefaultTcpControlPort)
    {
        _registry = registry;
        _telemetry = telemetry;
        _tcpPort = tcpPort;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var listener = new TcpListener(IPAddress.Any, _tcpPort);
        listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        listener.Start();

        _telemetry.AddLog("TCP", $"Khởi động máy chủ TCP Control & JPEG Stream trên cổng :{_tcpPort}");

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                System.Net.Sockets.TcpClient tcpClient = await listener.AcceptTcpClientAsync(stoppingToken).ConfigureAwait(false);
                var session = new ClientSession(
                    tcpClient,
                    _registry,
                    _telemetry,
                    RegisterClientSession,
                    UnregisterClientSession);

                _ = Task.Run(() => session.RunSessionAsync(stoppingToken), stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Server shutting down
        }
        finally
        {
            listener.Stop();
            foreach (var session in _sessions.Values)
            {
                await session.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private void RegisterClientSession(string clientId, ClientSession session)
    {
        _sessions[clientId] = session;
    }

    private void UnregisterClientSession(string clientId)
    {
        _sessions.TryRemove(clientId, out _);
        _telemetry.AddLog("TCP", $"Máy trạm {clientId} đã ngắt kết nối TCP");
    }

    /// <summary>
    /// Gửi lệnh Khóa hoặc Mở khóa chuột &amp; bàn phím (0x03 CMD_LOCK_INPUT) tới 1 máy hoặc toàn bộ phòng ("ALL").
    /// </summary>
    public async Task<int> SendLockInputCommandAsync(string targetClientId, bool locked, string? customMessage = null)
    {
        var payload = new LockInputPayload
        {
            Locked = locked,
            Message = string.IsNullOrWhiteSpace(customMessage)
                ? "MÁY TRẠM ĐANG BỊ KHÓA BỞI GIẢNG VIÊN / QUẢN TRỊ VIÊN"
                : customMessage,
            FailSafeUnlockSeconds = ProtocolConstants.FailSafeAutoUnlockSeconds
        };

        PacketEnvelope packet = PacketEnvelope.FromJson(PacketType.CmdLockInput, payload);
        int sentCount = await DispatchPacketAsync(targetClientId, packet).ConfigureAwait(false);

        string actionText = locked ? "Khóa chuột/bàn phím" : "Mở khóa chuột/bàn phím";
        _telemetry.AddLog("LOCK", $"Đã gửi lệnh {actionText} tới {(targetClientId == "ALL" ? $"toàn bộ ({sentCount} máy)" : targetClientId)}");
        return sentCount;
    }

    /// <summary>
    /// Gửi Thông báo khẩn cấp (0x05 CMD_ALERT) hiển thị cửa sổ AlwaysOnTop trên máy trạm.
    /// </summary>
    public async Task<(string AlertId, int SentCount)> SendEmergencyAlertCommandAsync(
        string targetClientId,
        string title,
        string message,
        string severity = "urgent")
    {
        var payload = new EmergencyAlertPayload
        {
            Title = string.IsNullOrWhiteSpace(title) ? "THÔNG BÁO KHẨN TỪ GIẢNG VIÊN" : title.Trim(),
            Message = message.Trim(),
            Severity = severity,
            RequireAck = true
        };

        IReadOnlyList<ClientSession> targets = ResolveTargetSessions(targetClientId);
        _telemetry.RegisterAlertBroadcast(payload, targets.Count);

        PacketEnvelope packet = PacketEnvelope.FromJson(PacketType.CmdAlert, payload);
        int sentCount = await DispatchToSessionsAsync(targets, packet).ConfigureAwait(false);

        _telemetry.AddLog("ALERT", $"Đã phát thông báo khẩn \"{payload.Title}\" tới {sentCount} máy trạm");
        return (payload.AlertId, sentCount);
    }

    /// <summary>
    /// Gửi lệnh Tắt máy từ xa hoặc Hủy lệnh tắt máy (0x04 CMD_SHUTDOWN).
    /// </summary>
    public async Task<int> SendShutdownCommandAsync(
        string targetClientId,
        string action,
        int delaySeconds,
        bool dryRun,
        string reason)
    {
        bool effectiveDryRun = dryRun || !NegaRuntimeConfig.Current.AllowRealShutdown;
        var payload = new ShutdownCommandPayload
        {
            Action = action,
            DelaySeconds = Math.Clamp(delaySeconds, 5, 300),
            DryRun = effectiveDryRun,
            Reason = string.IsNullOrWhiteSpace(reason) ? "Kết thúc ca thực hành phòng máy" : reason.Trim()
        };

        PacketEnvelope packet = PacketEnvelope.FromJson(PacketType.CmdShutdown, payload);
        int sentCount = await DispatchPacketAsync(targetClientId, packet).ConfigureAwait(false);

        string logDesc = string.Equals(action, "cancel", StringComparison.OrdinalIgnoreCase)
            ? $"Đã gửi lệnh HỦY TẮT MÁY tới {sentCount} máy trạm"
            : $"Đã gửi lệnh TẮT MÁY (trễ {payload.DelaySeconds}s, {(effectiveDryRun ? "Safe Mode" : "Tắt thật")}) tới {sentCount} máy trạm";
        _telemetry.AddLog("SHUTDOWN", logDesc);
        return sentCount;
    }

    /// <summary>
    /// Điều chỉnh chất lượng luồng JPEG (0x06 CMD_STREAM_CONFIG): chuyển giữa Grid View (480x270) và Zoom HD (960x540).
    /// </summary>
    public async Task<bool> SetClientStreamModeAsync(string clientId, string mode)
    {
        bool isZoom = string.Equals(mode, "zoom", StringComparison.OrdinalIgnoreCase);
        StreamConfigPayload cfg = isZoom
            ? StreamConfigPayload.CreateZoomHd()
            : StreamConfigPayload.CreateGridDefault();

        _registry.SetStreamMode(clientId, cfg.Mode);

        if (_sessions.TryGetValue(clientId, out ClientSession? session))
        {
            PacketEnvelope packet = PacketEnvelope.FromJson(PacketType.CmdStreamConfig, cfg);
            bool ok = await session.SendPacketAsync(packet).ConfigureAwait(false);
            if (ok)
            {
                _telemetry.IncrementCommandsSent();
                _telemetry.AddLog("TCP", $"Chuyển luồng {clientId} sang chế độ {(isZoom ? "ZOOM HD (960x540 @ 10 FPS)" : "GRID (480x270 @ 3 FPS)")}");
            }
            return ok;
        }
        return false;
    }

    private IReadOnlyList<ClientSession> ResolveTargetSessions(string targetClientId)
    {
        if (string.IsNullOrWhiteSpace(targetClientId) ||
            string.Equals(targetClientId, "ALL", StringComparison.OrdinalIgnoreCase))
        {
            return _sessions.Values.ToList();
        }

        return _sessions.TryGetValue(targetClientId, out ClientSession? single)
            ? new List<ClientSession> { single }
            : Array.Empty<ClientSession>();
    }

    private async Task<int> DispatchPacketAsync(string targetClientId, PacketEnvelope packet)
    {
        IReadOnlyList<ClientSession> targets = ResolveTargetSessions(targetClientId);
        return await DispatchToSessionsAsync(targets, packet).ConfigureAwait(false);
    }

    private async Task<int> DispatchToSessionsAsync(IReadOnlyList<ClientSession> targets, PacketEnvelope packet)
    {
        int count = 0;
        foreach (ClientSession session in targets)
        {
            if (await session.SendPacketAsync(packet).ConfigureAwait(false))
            {
                count++;
                _telemetry.IncrementCommandsSent();
            }
        }
        return count;
    }
}
