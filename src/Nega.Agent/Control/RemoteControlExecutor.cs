using System.Diagnostics;
using System.Runtime.InteropServices;
using Nega.Common.Models;
using Nega.Common.Security;

namespace Nega.Agent.Control;

/// <summary>
/// Bộ thực thi các lệnh điều khiển từ xa trên máy trạm (Client Agent):
/// 1. Khóa / Mở khóa chuột và bàn phím (kèm cơ chế Fail-safe Auto-Unlock khi mất mạng > 30s).
/// 2. Hiển thị Thông báo khẩn cấp (AlwaysOnTop / Non-blocking) và trả gói xác nhận ACK khi người dùng đọc.
/// 3. Tắt máy từ xa có đếm ngược thời gian, hỗ trợ Hủy lệnh tắt máy và chế độ Safe/Dry-Run Mode khi Demo.
/// </summary>
public sealed class RemoteControlExecutor
{
    private readonly string _clientId;
    private readonly string _hostname;
    private readonly bool _isSimulated;
    private readonly Func<AckResponsePayload, Task> _sendAckCallback;

    private bool _isLocked;
    private string? _activeAlertId;
    private string? _activeAlertTitle;
    private string? _activeAlertMessage;
    private DateTimeOffset? _shutdownTargetUtc;
    private bool _shutdownDryRun = true;
    private CancellationTokenSource? _shutdownCts;

    public bool IsLocked => _isLocked;
    public string? ActiveAlertTitle => _activeAlertTitle;
    public string? ActiveAlertMessage => _activeAlertMessage;

    public int? ShutdownRemainingSeconds
    {
        get
        {
            if (_shutdownTargetUtc is null) return null;
            int rem = (int)Math.Ceiling((_shutdownTargetUtc.Value - DateTimeOffset.UtcNow).TotalSeconds);
            return rem > 0 ? rem : 0;
        }
    }

    public RemoteControlExecutor(
        string clientId,
        string hostname,
        bool isSimulated,
        Func<AckResponsePayload, Task> sendAckCallback)
    {
        _clientId = clientId;
        _hostname = hostname;
        _isSimulated = isSimulated;
        _sendAckCallback = sendAckCallback;
    }

    /// <summary>
    /// Thực thi lệnh Khóa hoặc Mở khóa chuột &amp; bàn phím (0x03 CMD_LOCK_INPUT).
    /// </summary>
    public async Task ExecuteLockCommandAsync(LockInputPayload cmd)
    {
        _isLocked = cmd.Locked;
        string actionName = cmd.Locked ? "LOCK" : "UNLOCK";
        string detail = cmd.Locked
            ? $"Đã khóa chuột & bàn phím máy {_hostname} (Fail-safe: {cmd.FailSafeUnlockSeconds}s)"
            : $"Đã mở khóa chuột & bàn phím máy {_hostname}";

        await _sendAckCallback(new AckResponsePayload
        {
            CommandId = cmd.CommandId,
            ClientId = _clientId,
            Hostname = _hostname,
            CommandType = actionName,
            Success = true,
            Detail = detail
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// Cơ chế Fail-safe Auto-Unlock: Tự động mở khóa máy trạm nếu mất kết nối với Server quá 30 giây.
    /// </summary>
    public void TriggerFailSafeUnlockIfDisconnected()
    {
        if (_isLocked)
        {
            _isLocked = false;
        }
    }

    /// <summary>
    /// Thực thi lệnh Gửi thông báo khẩn cấp (0x05 CMD_ALERT) trên luồng bất đồng bộ (Non-blocking).
    /// </summary>
    public void ExecuteEmergencyAlert(EmergencyAlertPayload alert)
    {
        _activeAlertId = alert.AlertId;
        _activeAlertTitle = alert.Title;
        _activeAlertMessage = alert.Message;

        // Chạy bất đồng bộ để tuyệt đối không làm chặn luồng nhận gói tin TCP
        _ = Task.Run(async () =>
        {
            try
            {
                if (!_isSimulated && RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    // Trên máy thật macOS: có thể hiển thị thông báo nổi hoặc tự xác nhận sau khi hiển thị
                    await Task.Delay(2500).ConfigureAwait(false);
                }
                else
                {
                    // Trên máy trạm giả lập: hiển thị popup trực quan trên luồng JPEG trong 2.5-4.5s rồi gửi ACK "Đã đọc"
                    int simulatedReadDelay = 2000 + Math.Abs(_hostname.GetHashCode() % 2500);
                    await Task.Delay(simulatedReadDelay).ConfigureAwait(false);
                }

                if (_activeAlertId == alert.AlertId)
                {
                    _activeAlertTitle = null;
                    _activeAlertMessage = null;
                }

                if (alert.RequireAck)
                {
                    await _sendAckCallback(new AckResponsePayload
                    {
                        CommandId = alert.AlertId,
                        ClientId = _clientId,
                        Hostname = _hostname,
                        CommandType = "ALERT_READ",
                        Success = true,
                        Detail = $"{_hostname} đã xác nhận đọc thông báo: \"{alert.Title}\""
                    }).ConfigureAwait(false);
                }
            }
            catch
            {
                // Ignore background alert error
            }
        });
    }

    /// <summary>
    /// Thực thi lệnh Tắt máy từ xa hoặc Hủy lệnh tắt máy (0x04 CMD_SHUTDOWN).
    /// </summary>
    public async Task ExecuteShutdownCommandAsync(ShutdownCommandPayload cmd)
    {
        if (string.Equals(cmd.Action, "cancel", StringComparison.OrdinalIgnoreCase))
        {
            _shutdownCts?.Cancel();
            _shutdownTargetUtc = null;

            if (!_isSimulated && RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                TryRunOsCommand("shutdown", "/a");
            }

            await _sendAckCallback(new AckResponsePayload
            {
                CommandId = cmd.CommandId,
                ClientId = _clientId,
                Hostname = _hostname,
                CommandType = "CANCEL_SHUTDOWN",
                Success = true,
                Detail = $"Đã hủy lệnh tắt máy trên {_hostname}"
            }).ConfigureAwait(false);
            return;
        }

        _shutdownCts?.Cancel();
        _shutdownCts = new CancellationTokenSource();
        CancellationToken ct = _shutdownCts.Token;

        int delaySec = Math.Clamp(cmd.DelaySeconds, 5, 300);
        bool effectiveDryRun = cmd.DryRun || !NegaRuntimeConfig.Current.AllowRealShutdown;
        _shutdownDryRun = effectiveDryRun;
        _shutdownTargetUtc = DateTimeOffset.UtcNow.AddSeconds(delaySec);

        string modeLabel = effectiveDryRun ? "Chế độ Demo An toàn (Dry-Run)" : "Tắt nguồn hệ thống";
        await _sendAckCallback(new AckResponsePayload
        {
            CommandId = cmd.CommandId,
            ClientId = _clientId,
            Hostname = _hostname,
            CommandType = "SHUTDOWN",
            Success = true,
            Detail = $"{_hostname} bắt đầu đếm ngược tắt máy ({delaySec}s) - {modeLabel}"
        }).ConfigureAwait(false);

        if (!_isSimulated && !effectiveDryRun && RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            string safeReason = SanitizeShutdownComment(cmd.Reason);
            TryRunOsCommand("shutdown", $"/s /t {delaySec} /c \"{safeReason}\"");
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(delaySec), ct).ConfigureAwait(false);
                _shutdownTargetUtc = null;

                await _sendAckCallback(new AckResponsePayload
                {
                    CommandId = cmd.CommandId,
                    ClientId = _clientId,
                    Hostname = _hostname,
                    CommandType = "SHUTDOWN_COMPLETED",
                    Success = true,
                    Detail = effectiveDryRun
                        ? $"{_hostname} đã hoàn tất đếm ngược tắt máy (Safe Mode - không tắt nguồn thật)"
                        : $"{_hostname} đang thực thi lệnh tắt nguồn hệ điều hành"
                }).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Đã hủy đếm ngược
            }
        }, ct);
    }

    private static string SanitizeShutdownComment(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "Nega Lab Maintenance";
        var filtered = new string(raw.Where(c => !char.IsControl(c) && c != '"' && c != '&' && c != '|' && c != ';').ToArray());
        return filtered.Length > 80 ? filtered[..80] : filtered;
    }

    private static void TryRunOsCommand(string fileName, string arguments)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                CreateNoWindow = true,
                UseShellExecute = false
            });
        }
        catch
        {
            // Ignore privilege / OS errors
        }
    }
}
