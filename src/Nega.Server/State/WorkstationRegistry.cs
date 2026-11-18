using System.Collections.Concurrent;
using Nega.Common.Models;
using Nega.Common.Protocol;

namespace Nega.Server.State;

/// <summary>
/// Mô hình trạng thái thời gian thực của một máy trạm được quản lý tại Nega Server.
/// </summary>
public sealed class WorkstationState
{
    public string ClientId { get; set; } = string.Empty;
    public string Hostname { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
    public string OsDescription { get; set; } = string.Empty;
    public string Username { get; set; } = "Student";
    public int ScreenWidth { get; set; } = 1920;
    public int ScreenHeight { get; set; } = 1080;
    public bool IsSimulated { get; set; }

    public bool IsTcpConnected { get; set; }
    public bool IsLocked { get; set; }
    public bool IsShutdownPending { get; set; }
    public string StreamMode { get; set; } = "grid"; // "grid" | "zoom"

    public long PingMs { get; set; } = 1;
    public double CurrentFps { get; set; }
    public int LastFrameBytes { get; set; }
    public double BandwidthKbps { get; set; }
    public long TotalFramesReceived { get; set; }
    public string LastAckDetail { get; set; } = "Sẵn sàng";

    public DateTimeOffset LastSeenUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastFrameUtc { get; set; } = DateTimeOffset.MinValue;
    public byte[]? LatestJpegFrame { get; set; }

    public string StatusBadge => !IsTcpConnected && (DateTimeOffset.UtcNow - LastSeenUtc).TotalMilliseconds > ProtocolConstants.ClientOfflineTtlMs
        ? "Offline"
        : (IsLocked ? "Locked" : "Online");
}

/// <summary>
/// Kho lưu trữ trạng thái máy trạm tập trung (Thread-safe) tại Server:
/// - Hợp nhất thông tin từ kênh UDP Broadcast (ANNOUNCE) và kênh TCP (HANDSHAKE, SCREEN_FRAME, ACK).
/// - Tự động đánh dấu Offline khi quá thời gian TTL (10 giây) không nhận được tín hiệu.
/// </summary>
public sealed class WorkstationRegistry
{
    private readonly ConcurrentDictionary<string, WorkstationState> _workstations = new(StringComparer.OrdinalIgnoreCase);

    public event Action<WorkstationState, bool>? OnWorkstationUpdated;

    public IReadOnlyList<WorkstationState> GetAllSorted()
    {
        return _workstations.Values
            .OrderBy(w => w.IsSimulated ? 1 : 0)
            .ThenBy(w => w.Hostname, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public WorkstationState? GetById(string clientId)
    {
        _workstations.TryGetValue(clientId, out WorkstationState? state);
        return state;
    }

    /// <summary>
    /// Cập nhật thông tin máy trạm khi nhận được gói UDP ANNOUNCE trên cổng 9999.
    /// </summary>
    public (WorkstationState State, bool IsNew) UpsertFromUdpDiscovery(UdpAnnounceMessage msg, string remoteIp)
    {
        bool isNew = false;
        string ip = string.IsNullOrWhiteSpace(msg.IpAddress) ? remoteIp : msg.IpAddress;

        WorkstationState state = _workstations.AddOrUpdate(
            msg.ClientId,
            _ =>
            {
                isNew = true;
                return new WorkstationState
                {
                    ClientId = msg.ClientId,
                    Hostname = msg.Hostname,
                    IpAddress = ip,
                    OsDescription = msg.OsDescription,
                    ScreenWidth = msg.ScreenWidth,
                    ScreenHeight = msg.ScreenHeight,
                    IsLocked = msg.IsLocked,
                    IsSimulated = msg.IsSimulated,
                    LastSeenUtc = DateTimeOffset.UtcNow
                };
            },
            (_, existing) =>
            {
                existing.Hostname = msg.Hostname;
                existing.IpAddress = ip;
                existing.OsDescription = msg.OsDescription;
                existing.ScreenWidth = msg.ScreenWidth;
                existing.ScreenHeight = msg.ScreenHeight;
                existing.IsSimulated = msg.IsSimulated;
                existing.LastSeenUtc = DateTimeOffset.UtcNow;
                return existing;
            });

        OnWorkstationUpdated?.Invoke(state, isNew);
        return (state, isNew);
    }

    /// <summary>
    /// Cập nhật thông tin máy trạm khi nhận gói 0x01 HANDSHAKE qua kết nối TCP.
    /// </summary>
    public WorkstationState UpsertFromTcpHandshake(HandshakePayload hs, string remoteIp)
    {
        string ip = string.IsNullOrWhiteSpace(hs.IpAddress) ? remoteIp : hs.IpAddress;

        WorkstationState state = _workstations.AddOrUpdate(
            hs.ClientId,
            _ => new WorkstationState
            {
                ClientId = hs.ClientId,
                Hostname = hs.Hostname,
                IpAddress = ip,
                OsDescription = hs.OsDescription,
                Username = hs.Username,
                ScreenWidth = hs.ScreenWidth,
                ScreenHeight = hs.ScreenHeight,
                IsSimulated = hs.IsSimulated,
                IsTcpConnected = true,
                LastSeenUtc = DateTimeOffset.UtcNow
            },
            (_, existing) =>
            {
                existing.Hostname = hs.Hostname;
                existing.IpAddress = ip;
                existing.OsDescription = hs.OsDescription;
                existing.Username = hs.Username;
                existing.ScreenWidth = hs.ScreenWidth;
                existing.ScreenHeight = hs.ScreenHeight;
                existing.IsSimulated = hs.IsSimulated;
                existing.IsTcpConnected = true;
                existing.LastSeenUtc = DateTimeOffset.UtcNow;
                return existing;
            });

        OnWorkstationUpdated?.Invoke(state, false);
        return state;
    }

    /// <summary>
    /// Cập nhật frame ảnh JPEG mới nhất (0x02 SCREEN_FRAME) và tính toán FPS / Băng thông KB/s.
    /// </summary>
    public void UpdateScreenFrame(string clientId, byte[] jpegBytes)
    {
        if (!_workstations.TryGetValue(clientId, out WorkstationState? state))
        {
            return;
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        if (state.LastFrameUtc != DateTimeOffset.MinValue)
        {
            double deltaSec = Math.Max(0.05, (now - state.LastFrameUtc).TotalSeconds);
            double instantFps = 1.0 / deltaSec;
            state.CurrentFps = Math.Round(state.CurrentFps * 0.65 + instantFps * 0.35, 1);
            double instantKbps = (jpegBytes.Length / 1024.0) / deltaSec;
            state.BandwidthKbps = Math.Round(state.BandwidthKbps * 0.65 + instantKbps * 0.35, 1);
        }
        else
        {
            state.CurrentFps = 3.0;
            state.BandwidthKbps = Math.Round((jpegBytes.Length / 1024.0) * 3.0, 1);
        }

        state.LatestJpegFrame = jpegBytes;
        state.LastFrameBytes = jpegBytes.Length;
        state.TotalFramesReceived++;
        state.LastFrameUtc = now;
        state.LastSeenUtc = now;
        state.IsTcpConnected = true;
    }

    public void UpdateAckState(AckResponsePayload ack)
    {
        if (!_workstations.TryGetValue(ack.ClientId, out WorkstationState? state))
        {
            return;
        }

        state.LastSeenUtc = DateTimeOffset.UtcNow;
        state.LastAckDetail = ack.Detail;

        switch (ack.CommandType)
        {
            case "LOCK":
                state.IsLocked = true;
                break;
            case "UNLOCK":
                state.IsLocked = false;
                break;
            case "SHUTDOWN":
                state.IsShutdownPending = true;
                break;
            case "CANCEL_SHUTDOWN":
            case "SHUTDOWN_COMPLETED":
                state.IsShutdownPending = false;
                break;
        }

        OnWorkstationUpdated?.Invoke(state, false);
    }

    public void UpdatePing(string clientId, long rttMs)
    {
        if (_workstations.TryGetValue(clientId, out WorkstationState? state))
        {
            state.PingMs = Math.Max(1, rttMs);
            state.LastSeenUtc = DateTimeOffset.UtcNow;
        }
    }

    public void SetStreamMode(string clientId, string mode)
    {
        if (_workstations.TryGetValue(clientId, out WorkstationState? state))
        {
            state.StreamMode = mode;
        }
    }

    public void MarkTcpDisconnected(string clientId)
    {
        if (_workstations.TryGetValue(clientId, out WorkstationState? state))
        {
            state.IsTcpConnected = false;
            state.CurrentFps = 0;
            state.BandwidthKbps = 0;
            OnWorkstationUpdated?.Invoke(state, false);
        }
    }
}
