namespace Nega.Common.Models;

/// <summary>
/// Gói tin bắt tay (0x01 HANDSHAKE) khi Client vừa mở kết nối TCP tới Server.
/// </summary>
public sealed class HandshakePayload
{
    public string ClientId { get; set; } = string.Empty;
    public string Hostname { get; set; } = string.Empty;
    public string OsDescription { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
    public int ScreenWidth { get; set; } = 1920;
    public int ScreenHeight { get; set; } = 1080;
    public bool IsSimulated { get; set; }
    public long TimestampUnixMs { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    public string AuthTag { get; set; } = string.Empty;
}

/// <summary>
/// Gói cấu hình luồng ảnh JPEG (0x06 CMD_STREAM_CONFIG): chuyển đổi giữa chế độ Grid và chế độ Zoom HD.
/// </summary>
public sealed class StreamConfigPayload
{
    public int Width { get; set; } = 480;
    public int Height { get; set; } = 270;
    public int Quality { get; set; } = 45;
    public int Fps { get; set; } = 3;
    public string Mode { get; set; } = "grid"; // "grid" | "zoom"

    public static StreamConfigPayload CreateGridDefault() => new()
    {
        Width = 480,
        Height = 270,
        Quality = 45,
        Fps = 3,
        Mode = "grid"
    };

    public static StreamConfigPayload CreateZoomHd() => new()
    {
        Width = 960,
        Height = 540,
        Quality = 75,
        Fps = 10,
        Mode = "zoom"
    };
}

/// <summary>
/// Lệnh khóa / mở khóa chuột và bàn phím (0x03 CMD_LOCK_INPUT).
/// </summary>
public sealed class LockInputPayload
{
    public string CommandId { get; set; } = Guid.NewGuid().ToString("N")[..10];
    public bool Locked { get; set; }
    public string Message { get; set; } = "MÁY TRẠM ĐANG BỊ KHÓA BỞI GIẢNG VIÊN / QUẢN TRỊ VIÊN";
    public int FailSafeUnlockSeconds { get; set; } = 30;
}

/// <summary>
/// Lệnh tắt máy từ xa hoặc hủy tắt máy (0x04 CMD_SHUTDOWN).
/// </summary>
public sealed class ShutdownCommandPayload
{
    public string CommandId { get; set; } = Guid.NewGuid().ToString("N")[..10];
    public string Action { get; set; } = "shutdown"; // "shutdown" | "cancel"
    public int DelaySeconds { get; set; } = 15;
    public bool DryRun { get; set; } = true; // Safe Mode khi demo
    public string Reason { get; set; } = "Kết thúc ca thực hành phòng máy";
}

/// <summary>
/// Lệnh gửi thông báo khẩn cấp (0x05 CMD_ALERT) hiển thị cửa sổ AlwaysOnTop trên máy trạm.
/// </summary>
public sealed class EmergencyAlertPayload
{
    public string AlertId { get; set; } = Guid.NewGuid().ToString("N")[..10];
    public string Title { get; set; } = "THÔNG BÁO KHẨN TỪ GIẢNG VIÊN";
    public string Message { get; set; } = string.Empty;
    public string Severity { get; set; } = "urgent"; // "urgent" | "warning" | "info"
    public bool RequireAck { get; set; } = true;
    public long CreatedAtUnixMs { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}

/// <summary>
/// Gói phản hồi xác nhận thực thi lệnh từ Client về Server (0x07 ACK).
/// </summary>
public sealed class AckResponsePayload
{
    public string CommandId { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string Hostname { get; set; } = string.Empty;
    public string CommandType { get; set; } = string.Empty; // "LOCK" | "UNLOCK" | "ALERT_READ" | "SHUTDOWN" | "CANCEL_SHUTDOWN"
    public bool Success { get; set; } = true;
    public string Detail { get; set; } = string.Empty;
    public long TimestampUnixMs { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}

/// <summary>
/// Gói kiểm tra kết nối sống và đo độ trễ RTT (0x08 PING_PONG).
/// </summary>
public sealed class PingPongPayload
{
    public bool IsPong { get; set; }
    public long SentAtUnixMs { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}
