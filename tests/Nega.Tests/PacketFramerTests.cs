using System.Text;
using Nega.Common.Models;
using Nega.Common.Protocol;

namespace Nega.Tests;

public sealed class PacketFramerTests
{
    private sealed class ChunkedReadStream : Stream
    {
        private readonly byte[] _data;
        private readonly int _maxChunkSize;
        private int _pos;

        public ChunkedReadStream(byte[] data, int maxChunkSize)
        {
            _data = data;
            _maxChunkSize = maxChunkSize;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _data.Length;
        public override long Position { get => _pos; set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_pos >= _data.Length) return 0;
            int toCopy = Math.Min(Math.Min(count, _maxChunkSize), _data.Length - _pos);
            Buffer.BlockCopy(_data, _pos, buffer, offset, toCopy);
            _pos += toCopy;
            return toCopy;
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_pos >= _data.Length) return ValueTask.FromResult(0);
            int toCopy = Math.Min(Math.Min(buffer.Length, _maxChunkSize), _data.Length - _pos);
            _data.AsSpan(_pos, toCopy).CopyTo(buffer.Span);
            _pos += toCopy;
            return ValueTask.FromResult(toCopy);
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    [Fact]
    public async Task WriteAndRead_RoundTripJsonPayload_Succeeds()
    {
        using var ms = new MemoryStream();
        using var writeLock = new SemaphoreSlim(1, 1);

        var original = new HandshakePayload
        {
            ClientId = "may-01",
            Hostname = "MAY-01",
            IpAddress = "10.10.0.101",
            IsSimulated = true
        };
        PacketEnvelope envelope = PacketEnvelope.FromJson(PacketType.Handshake, original);

        await PacketFramer.WritePacketAsync(ms, envelope.Type, envelope.Payload, writeLock);
        byte[] raw = ms.ToArray();

        Assert.Equal(ProtocolConstants.MagicHigh, raw[0]);
        Assert.Equal(ProtocolConstants.MagicLow, raw[1]);
        Assert.Equal((byte)PacketType.Handshake, raw[2]);

        ms.Position = 0;
        PacketEnvelope? decoded = await PacketFramer.ReadPacketAsync(ms);
        Assert.NotNull(decoded);
        Assert.Equal(PacketType.Handshake, decoded!.Type);

        var restored = decoded.DeserializeJson<HandshakePayload>();
        Assert.NotNull(restored);
        Assert.Equal("may-01", restored!.ClientId);
        Assert.Equal("MAY-01", restored.Hostname);
        Assert.Equal("10.10.0.101", restored.IpAddress);
    }

    [Fact]
    public async Task ReadPacketAsync_HandlesTcpFragmentationAndCoalescing()
    {
        using var wire = new MemoryStream();
        using var writeLock = new SemaphoreSlim(1, 1);

        byte[] p1 = Encoding.UTF8.GetBytes("{\"locked\":true}");
        byte[] p2 = Encoding.UTF8.GetBytes("{\"isPong\":true}");

        // Coalesce 2 frames back-to-back in one buffer
        await PacketFramer.WritePacketAsync(wire, PacketType.CmdLockInput, p1, writeLock);
        await PacketFramer.WritePacketAsync(wire, PacketType.PingPong, p2, writeLock);

        // Fragment reading into 3-byte chunks (smaller than 7-byte header!)
        using var fragmentedStream = new ChunkedReadStream(wire.ToArray(), maxChunkSize: 3);

        PacketEnvelope? first = await PacketFramer.ReadPacketAsync(fragmentedStream);
        PacketEnvelope? second = await PacketFramer.ReadPacketAsync(fragmentedStream);
        PacketEnvelope? eof = await PacketFramer.ReadPacketAsync(fragmentedStream);

        Assert.NotNull(first);
        Assert.Equal(PacketType.CmdLockInput, first!.Type);
        Assert.Equal(p1, first.Payload);

        Assert.NotNull(second);
        Assert.Equal(PacketType.PingPong, second!.Type);
        Assert.Equal(p2, second.Payload);

        Assert.Null(eof);
    }

    [Fact]
    public async Task ReadPacketAsync_RejectsInvalidMagicHeader()
    {
        byte[] corrupt = { 0x00, 0xFF, 0x01, 0x00, 0x00, 0x00, 0x02, 0x7B, 0x7D };
        using var ms = new MemoryStream(corrupt);

        await Assert.ThrowsAsync<InvalidDataException>(() => PacketFramer.ReadPacketAsync(ms));
    }

    [Fact]
    public async Task ReadPacketAsync_RejectsOversizedPayload()
    {
        byte[] header =
        {
            ProtocolConstants.MagicHigh,
            ProtocolConstants.MagicLow,
            (byte)PacketType.ScreenFrame,
            0x7F, 0xFF, 0xFF, 0xFF // ~2GB > MaxPayloadSizeBytes (10MB)
        };
        using var ms = new MemoryStream(header);

        await Assert.ThrowsAsync<InvalidDataException>(() => PacketFramer.ReadPacketAsync(ms));
    }
}
