using System.Text;
using System.Text.Json;

namespace Nega.Common.Protocol;

/// <summary>
/// Gói dữ liệu đã được đóng/mở khung hoàn chỉnh từ luồng TCP.
/// </summary>
public sealed class PacketEnvelope
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public PacketType Type { get; }
    public byte[] Payload { get; }

    public PacketEnvelope(PacketType type, byte[] payload)
    {
        Type = type;
        Payload = payload ?? Array.Empty<byte>();
    }

    public T? DeserializeJson<T>()
    {
        if (Payload.Length == 0)
        {
            return default;
        }
        return JsonSerializer.Deserialize<T>(Payload, JsonOptions);
    }

    public static PacketEnvelope FromJson<T>(PacketType type, T data)
    {
        string json = JsonSerializer.Serialize(data, JsonOptions);
        return new PacketEnvelope(type, Encoding.UTF8.GetBytes(json));
    }
}
