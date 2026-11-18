using System.Runtime.InteropServices;
using Nega.Agent.Discovery;
using Nega.Agent.TcpClient;
using Nega.Common.Networking;
using Nega.Common.Protocol;
using Nega.Common.Security;

namespace Nega.Agent.Simulator;

/// <summary>
/// Đại diện cho một phiên bản Máy trạm hoàn chỉnh (gồm UdpDiscoveryAgent + TcpClientAgent).
/// Có thể khởi chạy cho máy trạm thật (IsSimulated = false) hoặc máy trạm giả lập trong phòng máy (IsSimulated = true).
/// </summary>
public sealed class WorkstationAgentInstance : IAsyncDisposable
{
    public string ClientId { get; }
    public string Hostname { get; }
    public string IpAddress { get; }
    public string OsDescription { get; }
    public bool IsSimulated { get; }

    private readonly UdpDiscoveryAgent _udpAgent;
    private readonly TcpClientAgent _tcpAgent;
    private CancellationTokenSource? _cts;
    private Task? _runningTask;

    public bool IsConnected => _tcpAgent.IsConnected;
    public bool IsLocked => _tcpAgent.ControlExecutor.IsLocked;

    public WorkstationAgentInstance(
        string clientId,
        string hostname,
        string ipAddress,
        string osDescription,
        bool isSimulated,
        int themeSeed = 0,
        int udpPort = ProtocolConstants.DefaultUdpDiscoveryPort,
        int tcpPort = ProtocolConstants.DefaultTcpControlPort)
    {
        ClientId = clientId;
        Hostname = hostname;
        IpAddress = ipAddress;
        OsDescription = osDescription;
        IsSimulated = isSimulated;

        _tcpAgent = new TcpClientAgent(clientId, hostname, osDescription, ipAddress, isSimulated, themeSeed);
        _tcpAgent.UpdateDiscoveredServer("127.0.0.1", tcpPort);

        _udpAgent = new UdpDiscoveryAgent(
            clientId,
            hostname,
            osDescription,
            ipAddress,
            isSimulated,
            udpPort,
            () => _tcpAgent.ControlExecutor.IsLocked);

        _udpAgent.OnServerDiscovered += (serverIp, discoveredTcpPort) =>
        {
            _tcpAgent.UpdateDiscoveredServer(serverIp, discoveredTcpPort);
        };
    }

    public void SetTargetServer(string serverIp, int tcpPort = ProtocolConstants.DefaultTcpControlPort)
    {
        _tcpAgent.UpdateDiscoveredServer(serverIp, tcpPort);
    }

    public static WorkstationAgentInstance CreateHostAgent(
        int udpPort = ProtocolConstants.DefaultUdpDiscoveryPort,
        int tcpPort = ProtocolConstants.DefaultTcpControlPort)
    {
        bool maskIdentity = NegaRuntimeConfig.Current.MaskHostIdentity;
        string hostname = maskIdentity ? "HOST-MASTER-PC" : Environment.MachineName;
        string ip = SubnetBroadcastHelper.GetPrimaryLocalIpAddress();
        string os = $"{RuntimeInformation.OSDescription} ({RuntimeInformation.ProcessArchitecture})";
        string clientId = maskIdentity ? "host-master-pc" : $"host-{hostname.ToLowerInvariant()}";

        return new WorkstationAgentInstance(
            clientId,
            hostname,
            ip,
            os,
            isSimulated: false,
            themeSeed: 0,
            udpPort: udpPort,
            tcpPort: tcpPort);
    }

    public static WorkstationAgentInstance CreateSimulatedWorkstation(
        int index,
        int udpPort = ProtocolConstants.DefaultUdpDiscoveryPort,
        int tcpPort = ProtocolConstants.DefaultTcpControlPort)
    {
        string hostname = $"MAY-{index:D2}";
        string ip = $"10.10.0.{100 + index}";
        string os = index % 3 == 0 ? "Windows 11 Pro 23H2" : (index % 2 == 0 ? "Windows 10 Enterprise" : "macOS Sequoia 15");
        string clientId = $"sim-may-{index:D2}";

        return new WorkstationAgentInstance(
            clientId,
            hostname,
            ip,
            os,
            isSimulated: true,
            themeSeed: index,
            udpPort: udpPort,
            tcpPort: tcpPort);
    }

    public void Start()
    {
        if (_runningTask is not null)
        {
            return;
        }

        _cts = new CancellationTokenSource();
        CancellationToken ct = _cts.Token;
        _runningTask = Task.Run(async () =>
        {
            Task udpTask = _udpAgent.RunAsync(ct);
            Task tcpTask = _tcpAgent.RunAsync(ct);
            await Task.WhenAll(udpTask, tcpTask).ConfigureAwait(false);
        }, ct);
    }

    public async ValueTask DisposeAsync()
    {
        if (_cts is not null)
        {
            _cts.Cancel();
            if (_runningTask is not null)
            {
                try
                {
                    await _runningTask.ConfigureAwait(false);
                }
                catch
                {
                    // Ignore cancellation
                }
            }
            _cts.Dispose();
            _cts = null;
            _runningTask = null;
        }
    }
}
