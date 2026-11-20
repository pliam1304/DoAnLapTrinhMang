using System.Net;
using Nega.Common.Models;
using Nega.Common.Networking;
using Nega.Server.State;

namespace Nega.Tests;

public sealed class TelemetryAndNetworkTests
{
    [Fact]
    public void TelemetryService_TracksCountersAlertAcksAndCapsLogQueue()
    {
        var telemetry = new TelemetryService();
        telemetry.IncrementUdpPackets();
        telemetry.IncrementTcpFrames();
        telemetry.IncrementCommandsSent();

        Assert.Equal(1, telemetry.UdpPacketsCount);
        Assert.Equal(1, telemetry.TcpFramesCount);
        Assert.Equal(1, telemetry.CommandsSentCount);

        telemetry.RegisterAlertBroadcast(new EmergencyAlertPayload
        {
            AlertId = "alert-99",
            Title = "Kiểm tra kết nối",
            Message = "Vui lòng giữ nguyên vị trí"
        }, targetCount: 3);

        telemetry.RecordAlertAck(new AckResponsePayload
        {
            CommandId = "alert-99",
            ClientId = "may-01",
            Hostname = "MAY-01",
            CommandType = "ALERT_READ"
        });

        Assert.Single(telemetry.GetAlertSummaries());

        for (int i = 0; i < 80; i++)
        {
            telemetry.AddLog("INFO", $"Event {i}");
        }

        Assert.Equal(25, telemetry.GetRecentLogs(25).Count);
        Assert.Equal(60, telemetry.GetRecentLogs(100).Count);
    }

    [Fact]
    public void SubnetBroadcastHelper_AlwaysIncludesLoopbackAndGlobalBroadcast()
    {
        IReadOnlyList<IPAddress> addrs = SubnetBroadcastHelper.GetBroadcastAddresses();
        Assert.Contains(IPAddress.Loopback, addrs);
        Assert.Contains(IPAddress.Broadcast, addrs);
        Assert.False(string.IsNullOrWhiteSpace(SubnetBroadcastHelper.GetPrimaryLocalIpAddress()));
    }
}
