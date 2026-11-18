namespace Nega.Common.Protocol;

/// <summary>
/// Các hằng số giao thức mạng tầng Transport (UDP Broadcast &amp; TCP Binary Stream) của hệ thống Nega.
/// </summary>
public static class ProtocolConstants
{
    // 2 byte định danh đầu khung gói tin TCP (0xAA 0x55)
    public const byte MagicHigh = 0xAA;
    public const byte MagicLow = 0x55;
    public const int HeaderSize = 7; // [Magic: 2B][Type: 1B][Length: 4B Big-Endian]

    // Cổng mạng mặc định
    public const int DefaultUdpDiscoveryPort = 9999;
    public const int DefaultTcpControlPort = 8888;
    public const int DefaultDashboardHttpPort = 5050;

    // Chu kỳ & Timeout (ms / giây)
    public const int UdpBroadcastIntervalMs = 3000;
    public const int ClientOfflineTtlMs = 10000;
    public const int TcpPingIntervalMs = 5000;
    public const int FailSafeAutoUnlockSeconds = 30;

    // Giới hạn kích thước gói tối đa (10 MB) để chống tràn bộ nhớ khi frame bị lỗi
    public const int MaxPayloadSizeBytes = 10 * 1024 * 1024;
}
