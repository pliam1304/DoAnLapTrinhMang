namespace Nega.Common.Models;

/// <summary>
/// Gói tin UDP Broadcast do Server phát ra định kỳ để tìm kiếm các máy trạm trong LAN.
/// </summary>
public sealed class UdpDiscoverMessage
{
    public string Type { get; set; } = "DISCOVER";
    public string ServerName { get; set; } = "NEGA-MASTER";
    public int TcpPort { get; set; } = 8888;
    public long TimestampUnixMs { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    public string AuthTag { get; set; } = string.Empty;
}

/// <summary>
/// Gói tin UDP do Client phản hồi (ANNOUNCE / Heartbeat) để Server lập danh sách máy trạm trong LAN.
/// </summary>
public sealed class UdpAnnounceMessage
{
    public string Type { get; set; } = "ANNOUNCE";
    public string ClientId { get; set; } = string.Empty;
    public string Hostname { get; set; } = string.Empty;
    public string OsDescription { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
    public int ScreenWidth { get; set; } = 1920;
    public int ScreenHeight { get; set; } = 1080;
    public bool IsLocked { get; set; }
    public bool IsSimulated { get; set; }
    public long TimestampUnixMs { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    public string AuthTag { get; set; } = string.Empty;
}
