using Nega.Common.Models;
using Nega.Server.State;

namespace Nega.Tests;

public sealed class WorkstationRegistryTests
{
    [Fact]
    public void UpsertFromUdpDiscovery_RegistersNewAndUpdatesExisting()
    {
        var registry = new WorkstationRegistry();
        var msg = new UdpAnnounceMessage
        {
            ClientId = "sim-may-01",
            Hostname = "MAY-01",
            OsDescription = "Windows 11 Pro",
            IpAddress = "10.10.0.101",
            IsSimulated = true
        };

        var (first, isNew1) = registry.UpsertFromUdpDiscovery(msg, "127.0.0.1");
        Assert.True(isNew1);
        Assert.Equal("MAY-01", first.Hostname);

        msg.OsDescription = "Windows 11 Enterprise";
        var (second, isNew2) = registry.UpsertFromUdpDiscovery(msg, "127.0.0.1");
        Assert.False(isNew2);
        Assert.Equal("Windows 11 Enterprise", second.OsDescription);
        Assert.Same(first, second);
    }

    [Fact]
    public void UpsertFromTcpHandshake_MarksOnlineAndUsesRemoteIpWhenBlank()
    {
        var registry = new WorkstationRegistry();
        WorkstationState state = registry.UpsertFromTcpHandshake(new HandshakePayload
        {
            ClientId = "real-pc",
            Hostname = "LAB-PC-01",
            IpAddress = "",
            Username = "Lab-Student",
            IsSimulated = false
        }, "192.168.1.45");

        Assert.True(state.IsTcpConnected);
        Assert.Equal("192.168.1.45", state.IpAddress);
        Assert.Equal("Online", state.StatusBadge);
    }

    [Fact]
    public void UpdateScreenFrameAndAckState_UpdatesMetricsAndLockStatus()
    {
        var registry = new WorkstationRegistry();
        registry.UpsertFromTcpHandshake(new HandshakePayload
        {
            ClientId = "sim-may-02",
            Hostname = "MAY-02",
            IpAddress = "10.10.0.102",
            IsSimulated = true
        }, "127.0.0.1");

        byte[] jpeg = new byte[2048];
        registry.UpdateScreenFrame("sim-may-02", jpeg);

        WorkstationState ws = registry.GetById("sim-may-02")!;
        Assert.Equal(1, ws.TotalFramesReceived);
        Assert.Equal(2048, ws.LastFrameBytes);
        Assert.True(ws.BandwidthKbps > 0);
        Assert.Same(jpeg, ws.LatestJpegFrame);

        registry.UpdateAckState(new AckResponsePayload
        {
            ClientId = "sim-may-02",
            CommandType = "LOCK",
            Detail = "Locked OK"
        });
        Assert.True(ws.IsLocked);
        Assert.Equal("Locked", ws.StatusBadge);

        registry.UpdateAckState(new AckResponsePayload
        {
            ClientId = "sim-may-02",
            CommandType = "SHUTDOWN",
            Detail = "Counting down"
        });
        Assert.True(ws.IsShutdownPending);

        registry.UpdateAckState(new AckResponsePayload
        {
            ClientId = "sim-may-02",
            CommandType = "CANCEL_SHUTDOWN",
            Detail = "Cancelled"
        });
        Assert.False(ws.IsShutdownPending);
    }

    [Fact]
    public void MarkTcpDisconnected_AndOfflineTtl_TransitionsToOffline()
    {
        var registry = new WorkstationRegistry();
        WorkstationState state = registry.UpsertFromTcpHandshake(new HandshakePayload
        {
            ClientId = "sim-may-03",
            Hostname = "MAY-03",
            IsSimulated = true
        }, "10.10.0.103");
        registry.UpdateScreenFrame("sim-may-03", new byte[1024]);

        registry.MarkTcpDisconnected("sim-may-03");
        Assert.False(state.IsTcpConnected);
        Assert.Equal(0, state.CurrentFps);
        Assert.Equal(0, state.BandwidthKbps);

        // Simulate expired heartbeat > 10s TTL
        state.LastSeenUtc = DateTimeOffset.UtcNow.AddSeconds(-15);
        Assert.Equal("Offline", state.StatusBadge);
    }

    [Fact]
    public void GetAllSorted_PlacesRealHostsBeforeSimulatedAndSortsByHostname()
    {
        var registry = new WorkstationRegistry();
        registry.UpsertFromUdpDiscovery(new UdpAnnounceMessage { ClientId = "s2", Hostname = "MAY-02", IsSimulated = true }, "10.0.0.2");
        registry.UpsertFromUdpDiscovery(new UdpAnnounceMessage { ClientId = "s1", Hostname = "MAY-01", IsSimulated = true }, "10.0.0.1");
        registry.UpsertFromUdpDiscovery(new UdpAnnounceMessage { ClientId = "r1", Hostname = "HOST-PC", IsSimulated = false }, "192.168.1.10");

        IReadOnlyList<WorkstationState> sorted = registry.GetAllSorted();
        Assert.Equal(new[] { "HOST-PC", "MAY-01", "MAY-02" }, sorted.Select(s => s.Hostname).ToArray());
    }
}
