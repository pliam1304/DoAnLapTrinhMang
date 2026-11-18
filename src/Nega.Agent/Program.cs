using Nega.Agent.Simulator;
using Nega.Common.Security;

namespace Nega.Agent;

public static class Program
{
    public static async Task Main(string[] args)
    {
        var cfg = NegaRuntimeConfig.Current;
        int simulatedCount = 0;
        int udpPort = cfg.UdpPort;
        int tcpPort = cfg.TcpPort;
        string? manualServerIp = null;
        string? customHostname = null;
        string? customClientIp = null;

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--simulate" && i + 1 < args.Length && int.TryParse(args[i + 1], out int count))
            {
                simulatedCount = Math.Clamp(count, 1, 30);
                i++;
            }
            else if (args[i] == "--server" && i + 1 < args.Length)
            {
                manualServerIp = args[i + 1].Trim();
                i++;
            }
            else if (args[i] == "--name" && i + 1 < args.Length)
            {
                customHostname = args[i + 1].Trim();
                i++;
            }
            else if (args[i] == "--ip" && i + 1 < args.Length)
            {
                customClientIp = args[i + 1].Trim();
                i++;
            }
        }

        var instances = new List<WorkstationAgentInstance>();
        if (simulatedCount > 0)
        {
            Console.WriteLine($"[Nega.Agent] Đang khởi chạy {simulatedCount} máy trạm giả lập (UDP :{udpPort}, TCP :{tcpPort})...");
            for (int idx = 1; idx <= simulatedCount; idx++)
            {
                var sim = WorkstationAgentInstance.CreateSimulatedWorkstation(idx, udpPort, tcpPort);
                if (!string.IsNullOrWhiteSpace(manualServerIp))
                {
                    sim.SetTargetServer(manualServerIp, tcpPort);
                }
                sim.Start();
                instances.Add(sim);
            }
        }
        else if (!string.IsNullOrWhiteSpace(customHostname))
        {
            string clientIp = string.IsNullOrWhiteSpace(customClientIp) ? "10.10.0.250" : customClientIp;
            string clientId = $"lan-{customHostname.ToLowerInvariant()}";
            var lanAgent = new WorkstationAgentInstance(
                clientId,
                customHostname,
                clientIp,
                "Windows 11 Pro (LAN Workstation)",
                isSimulated: true,
                themeSeed: Math.Abs(customHostname.GetHashCode() % 4),
                udpPort: udpPort,
                tcpPort: tcpPort);

            if (!string.IsNullOrWhiteSpace(manualServerIp))
            {
                lanAgent.SetTargetServer(manualServerIp, tcpPort);
            }
            Console.WriteLine($"[Nega.Agent] Đang khởi chạy Máy trạm LAN: {customHostname} ({clientIp})");
            lanAgent.Start();
            instances.Add(lanAgent);
        }
        else
        {
            var hostAgent = WorkstationAgentInstance.CreateHostAgent(udpPort, tcpPort);
            if (!string.IsNullOrWhiteSpace(manualServerIp))
            {
                hostAgent.SetTargetServer(manualServerIp, tcpPort);
                Console.WriteLine($"[Nega.Agent] Đang khởi chạy Máy trạm thật: {hostAgent.Hostname} ({hostAgent.IpAddress}) -> Kết nối tới Server {manualServerIp}:{tcpPort}");
            }
            else
            {
                Console.WriteLine($"[Nega.Agent] Đang khởi chạy Máy trạm thật: {hostAgent.Hostname} ({hostAgent.IpAddress}) -> Tự động tìm Server qua UDP Broadcast :{udpPort}");
            }
            hostAgent.Start();
            instances.Add(hostAgent);
        }

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        try
        {
            await Task.Delay(Timeout.Infinite, cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Exiting
        }
        finally
        {
            foreach (var inst in instances)
            {
                await inst.DisposeAsync();
            }
        }
    }
}
