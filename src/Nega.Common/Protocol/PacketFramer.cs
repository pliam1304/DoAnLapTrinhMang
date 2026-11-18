using System.Buffers.Binary;

namespace Nega.Common.Protocol;

/// <summary>
/// Bộ đóng khung &amp; tách khung nhị phân (Length-Prefixed Binary Framing) trên luồng TCP.
/// Giải quyết triệt để hiện tượng dính gói (coalescing) và phân mảnh gói (fragmentation) của TCP Byte Stream.
/// Cấu trúc Khung: [0xAA][0x55][PacketType: 1B][PayloadLength: 4B Big-Endian][Payload: N bytes]
/// </summary>
public static class PacketFramer
{
    /// <summary>
    /// Ghi trọn vẹn một gói tin ra luồng TCP một cách đồng bộ hóa (Thread-safe bằng SemaphoreSlim).
    /// </summary>
    public static async Task WritePacketAsync(
        Stream stream,
        PacketType type,
        ReadOnlyMemory<byte> payload,
        SemaphoreSlim writeLock,
        CancellationToken ct = default)
    {
        byte[] header = new byte[ProtocolConstants.HeaderSize];
        header[0] = ProtocolConstants.MagicHigh;
        header[1] = ProtocolConstants.MagicLow;
        header[2] = (byte)type;
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(3, 4), payload.Length);

        await writeLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await stream.WriteAsync(header.AsMemory(0, ProtocolConstants.HeaderSize), ct).ConfigureAwait(false);
            if (!payload.IsEmpty)
            {
                await stream.WriteAsync(payload, ct).ConfigureAwait(false);
            }
            await stream.FlushAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            writeLock.Release();
        }
    }

    /// <summary>
    /// Đọc chính xác 1 gói tin hoàn chỉnh từ luồng TCP. Trả về null nếu đối phương đã đóng kết nối (EOF).
    /// </summary>
    public static async Task<PacketEnvelope?> ReadPacketAsync(Stream stream, CancellationToken ct = default)
    {
        byte[] header = new byte[ProtocolConstants.HeaderSize];
        bool hasHeader = await ReadExactAsync(stream, header, ProtocolConstants.HeaderSize, ct).ConfigureAwait(false);
        if (!hasHeader)
        {
            return null;
        }

        if (header[0] != ProtocolConstants.MagicHigh || header[1] != ProtocolConstants.MagicLow)
        {
            throw new InvalidDataException(
                $"Lỗi đồng bộ khung TCP: Magic bytes không hợp lệ (0x{header[0]:X2} 0x{header[1]:X2}).");
        }

        var type = (PacketType)header[2];
        int length = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(3, 4));

        if (length < 0 || length > ProtocolConstants.MaxPayloadSizeBytes)
        {
            throw new InvalidDataException($"Kích thước Payload vượt ngưỡng an toàn: {length} bytes.");
        }

        byte[] payload = length > 0 ? new byte[length] : Array.Empty<byte>();
        if (length > 0)
        {
            bool hasPayload = await ReadExactAsync(stream, payload, length, ct).ConfigureAwait(false);
            if (!hasPayload)
            {
                return null;
            }
        }

        return new PacketEnvelope(type, payload);
    }

    /// <summary>
    /// Vòng lặp đọc cho đến khi gom đủ chính xác count bytes từ luồng TCP.
    /// </summary>
    private static async Task<bool> ReadExactAsync(Stream stream, byte[] buffer, int count, CancellationToken ct)
    {
        int offset = 0;
        while (offset < count)
        {
            int bytesRead = await stream.ReadAsync(buffer.AsMemory(offset, count - offset), ct).ConfigureAwait(false);
            if (bytesRead == 0)
            {
                return false; // Kết nối đã đóng
            }
            offset += bytesRead;
        }
        return true;
    }
}
