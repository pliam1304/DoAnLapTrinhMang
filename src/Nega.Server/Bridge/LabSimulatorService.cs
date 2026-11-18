using Nega.Agent.Simulator;
using Nega.Common.Protocol;
using Nega.Common.Security;
using Nega.Server.State;

namespace Nega.Server.Bridge;

/// <summary>
/// Quản lý Máy trạm Host cục bộ và Cụm máy trạm giả lập (Simulator) kết nối qua UDP Broadcast :9999 &amp; TCP :8888 thật.
/// Giúp kiểm thử và trình diễn Demo toàn bộ phòng máy ngay trên 1 máy tính hoặc kết hợp với các máy thật khác trong LAN.
/// </summary>
public sealed class LabSimulatorService : BackgroundService
{
    private readonly TelemetryService _telemetry;
    private readonly int _udpPort;
    private readonly int _tcpPort;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private WorkstationAgentInstance? _hostAgent;
    private readonly List<WorkstationAgentInstance> _simulatedAgents = new();

    public int ActiveSimulatedCount => _simulatedAgents.Count;
    public bool IsHostAgentRunning => _hostAgent is not null;

    public LabSimulatorService(
        TelemetryService telemetry,
        int udpPort = ProtocolConstants.DefaultUdpDiscoveryPort,
        int tcpPort = ProtocolConstants.DefaultTcpControlPort)
    {
        _telemetry = telemetry;
        _udpPort = udpPort;
        _tcpPort = tcpPort;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Đợi 150ms để UDP Discovery & TCP Server bind xong cổng trước khi bật Agent
        await Task.Delay(150, stoppingToken).ConfigureAwait(false);

        await _lock.WaitAsync(stoppingToken).ConfigureAwait(false);
        try
        {
            _hostAgent = WorkstationAgentInstance.CreateHostAgent(_udpPort, _tcpPort);
            _hostAgent.Start();
            _telemetry.AddLog("INFO", $"Đã kích hoạt Agent máy chủ cục bộ: {_hostAgent.Hostname} ({_hostAgent.IpAddress})");
        }
        finally
        {
            _lock.Release();
        }

        // Khởi tạo cụm máy trạm giả lập mặc định theo cấu hình .env (mặc định 5 máy MAY-01..MAY-05)
        await SetSimulatedCountAsync(NegaRuntimeConfig.Current.DefaultSimulatedCount).ConfigureAwait(false);

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Shutting down
        }
        finally
        {
            await _lock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_hostAgent is not null)
                {
                    await _hostAgent.DisposeAsync().ConfigureAwait(false);
                    _hostAgent = null;
                }
                foreach (var sim in _simulatedAgents)
                {
                    await sim.DisposeAsync().ConfigureAwait(false);
                }
                _simulatedAgents.Clear();
            }
            finally
            {
                _lock.Release();
            }
        }
    }

    public async Task<int> SetSimulatedCountAsync(int targetCount)
    {
        targetCount = Math.Clamp(targetCount, 0, 16);
        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            while (_simulatedAgents.Count < targetCount)
            {
                int nextIndex = _simulatedAgents.Count + 1;
                var sim = WorkstationAgentInstance.CreateSimulatedWorkstation(nextIndex, _udpPort, _tcpPort);
                sim.Start();
                _simulatedAgents.Add(sim);
            }

            while (_simulatedAgents.Count > targetCount)
            {
                int lastIdx = _simulatedAgents.Count - 1;
                WorkstationAgentInstance removed = _simulatedAgents[lastIdx];
                _simulatedAgents.RemoveAt(lastIdx);
                await removed.DisposeAsync().ConfigureAwait(false);
            }

            _telemetry.AddLog("INFO", $"Cấu hình cụm Simulator: Đang chạy {_simulatedAgents.Count} máy trạm ảo qua UDP/TCP");
            return _simulatedAgents.Count;
        }
        finally
        {
            _lock.Release();
        }
    }
}
