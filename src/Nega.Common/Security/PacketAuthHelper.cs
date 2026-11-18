using System.Security.Cryptography;
using System.Text;

namespace Nega.Common.Security;

/// <summary>
/// Hỗ trợ xác thực gói tin UDP Discovery và TCP Handshake bằng chữ ký HMAC-SHA256
/// dựa trên NEGA_CLUSTER_KEY (được lưu kín trong tệp .env cục bộ).
/// Ngăn chặn các thiết bị lạ trong cùng Wi-Fi giả mạo Server hoặc giả mạo máy trạm.
/// </summary>
public static class PacketAuthHelper
{
    private const long MaxClockSkewMs = 120_000;

    public static string ComputeTag(string clusterKey, params string[] fields)
    {
        if (string.IsNullOrWhiteSpace(clusterKey))
        {
            return string.Empty;
        }

        string canonical = string.Join('|', fields);
        byte[] keyBytes = Encoding.UTF8.GetBytes(clusterKey);
        byte[] dataBytes = Encoding.UTF8.GetBytes(canonical);
        byte[] hash = HMACSHA256.HashData(keyBytes, dataBytes);
        return Convert.ToHexString(hash);
    }

    public static bool VerifyTag(string clusterKey, string? providedTag, long timestampUnixMs, params string[] fields)
    {
        if (string.IsNullOrWhiteSpace(clusterKey))
        {
            // Chế độ mặc định khi chưa thiết lập NEGA_CLUSTER_KEY trong .env (giúp chạy ngay sau khi clone)
            return true;
        }

        if (string.IsNullOrWhiteSpace(providedTag))
        {
            return false;
        }

        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (Math.Abs(now - timestampUnixMs) > MaxClockSkewMs)
        {
            return false;
        }

        string expectedHex = ComputeTag(clusterKey, fields);
        byte[] expectedBytes = Encoding.UTF8.GetBytes(expectedHex);
        byte[] actualBytes = Encoding.UTF8.GetBytes(providedTag.Trim().ToUpperInvariant());

        return expectedBytes.Length == actualBytes.Length &&
               CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);
    }

    public static bool ConstantTimeEquals(string a, string b)
    {
        byte[] aBytes = Encoding.UTF8.GetBytes(a ?? string.Empty);
        byte[] bBytes = Encoding.UTF8.GetBytes(b ?? string.Empty);
        return aBytes.Length == bBytes.Length &&
               CryptographicOperations.FixedTimeEquals(aBytes, bBytes);
    }
}
