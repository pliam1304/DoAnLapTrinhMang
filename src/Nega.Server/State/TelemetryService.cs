using System.Collections.Concurrent;
using Nega.Common.Models;

namespace Nega.Server.State;

public sealed class ActivityLogEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Category { get; set; } = "INFO"; // "UDP" | "TCP" | "LOCK" | "ALERT" | "SHUTDOWN"
    public string Message { get; set; } = string.Empty;
    public string TimestampText { get; set; } = DateTime.Now.ToString("HH:mm:ss");
}

public sealed class AlertBroadcastRecord
{
    public string AlertId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string Severity { get; set; } = "urgent";
    public string SentAtText { get; set; } = DateTime.Now.ToString("HH:mm:ss");
    public int TargetCount { get; set; }
    public ConcurrentDictionary<string, string> AcknowledgedWorkstations { get; } = new();
}

/// <summary>
/// Dịch vụ thống kê thông số mạng (Telemetry), theo dõi xác nhận đọc Thông báo khẩn (ACK) và Nhật ký hệ thống.
/// </summary>
public sealed class TelemetryService
{
    private readonly ConcurrentQueue<ActivityLogEntry> _logs = new();
    private readonly ConcurrentDictionary<string, AlertBroadcastRecord> _alerts = new();

    private long _udpPacketsCount;
    private long _tcpFramesCount;
    private long _commandsSentCount;

    public long UdpPacketsCount => Interlocked.Read(ref _udpPacketsCount);
    public long TcpFramesCount => Interlocked.Read(ref _tcpFramesCount);
    public long CommandsSentCount => Interlocked.Read(ref _commandsSentCount);

    public void IncrementUdpPackets() => Interlocked.Increment(ref _udpPacketsCount);
    public void IncrementTcpFrames() => Interlocked.Increment(ref _tcpFramesCount);
    public void IncrementCommandsSent() => Interlocked.Increment(ref _commandsSentCount);

    public void AddLog(string category, string message)
    {
        _logs.Enqueue(new ActivityLogEntry
        {
            Category = category,
            Message = message,
            TimestampText = DateTime.Now.ToString("HH:mm:ss")
        });

        while (_logs.Count > 60)
        {
            _logs.TryDequeue(out _);
        }
    }

    public IReadOnlyList<ActivityLogEntry> GetRecentLogs(int maxCount = 25)
    {
        return _logs.Reverse().Take(maxCount).ToList();
    }

    public void RegisterAlertBroadcast(EmergencyAlertPayload alert, int targetCount)
    {
        var record = new AlertBroadcastRecord
        {
            AlertId = alert.AlertId,
            Title = alert.Title,
            Message = alert.Message,
            Severity = alert.Severity,
            SentAtText = DateTime.Now.ToString("HH:mm:ss"),
            TargetCount = Math.Max(1, targetCount)
        };
        _alerts[alert.AlertId] = record;
    }

    public void RecordAlertAck(AckResponsePayload ack)
    {
        if (ack.CommandType == "ALERT_READ" && _alerts.TryGetValue(ack.CommandId, out AlertBroadcastRecord? record))
        {
            record.AcknowledgedWorkstations[ack.ClientId] = ack.Hostname;
        }
    }

    public IReadOnlyList<object> GetAlertSummaries()
    {
        return _alerts.Values
            .OrderByDescending(a => a.SentAtText)
            .Take(10)
            .Select(a => new
            {
                a.AlertId,
                a.Title,
                a.Message,
                a.Severity,
                a.SentAtText,
                a.TargetCount,
                AckCount = a.AcknowledgedWorkstations.Count,
                AckHostnames = a.AcknowledgedWorkstations.Values.OrderBy(x => x).ToList()
            })
            .ToList<object>();
    }
}
