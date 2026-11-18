using System.Net;
using System.Security.Cryptography;
using Nega.Common.Networking;
using Nega.Common.Security;
using Nega.Server.Discovery;
using Nega.Server.State;
using Nega.Server.TcpServer;

namespace Nega.Server.Bridge;

public sealed record AuthVerifyRequestDto(string Pin);
public sealed record LockRequestDto(string TargetClientId, bool Locked, string? Message);
public sealed record AlertRequestDto(string TargetClientId, string Title, string Message, string? Severity);
public sealed record ShutdownRequestDto(string TargetClientId, string Action, int DelaySeconds, bool DryRun, string? Reason);
public sealed record StreamModeRequestDto(string ClientId, string Mode);
public sealed record SimulatorCountRequestDto(int Count);

/// <summary>
/// Định tuyến các REST API &amp; Endpoint truyền luồng ảnh JPEG giữa Lõi Mạng (UDP/TCP) và Giao diện Nega Dashboard.
/// Có lớp bảo vệ xác thực PIN quản trị cho các yêu cầu từ LAN (tránh máy lạ trong cùng Wi-Fi điều khiển trái phép).
/// </summary>
public static class ServerApiEndpoints
{
    private const string SessionCookieName = "nega_session";
    private static readonly string SessionSecretToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

    private static bool IsLocalRequest(HttpContext ctx)
    {
        IPAddress? remoteIp = ctx.Connection.RemoteIpAddress;
        return remoteIp is not null && IPAddress.IsLoopback(remoteIp);
    }

    private static bool IsAuthorized(HttpContext ctx)
    {
        var cfg = NegaRuntimeConfig.Current;

        // Cho phép thao tác trực tiếp trên máy chủ localhost (trừ khi cấu hình NEGA_REQUIRE_PIN_ON_LOCALHOST=true)
        if (!cfg.RequirePinOnLocalhost && IsLocalRequest(ctx))
        {
            return true;
        }

        // Kiểm tra Cookie phiên quản trị hợp lệ (được cấp sau khi nhập đúng PIN)
        if (ctx.Request.Cookies.TryGetValue(SessionCookieName, out string? cookieToken) &&
            PacketAuthHelper.ConstantTimeEquals(cookieToken, SessionSecretToken))
        {
            return true;
        }

        // Hoặc kiểm tra Header X-Nega-Admin-Pin (dùng cho API / automation)
        if (ctx.Request.Headers.TryGetValue("X-Nega-Admin-Pin", out var headerPin) &&
            PacketAuthHelper.ConstantTimeEquals(headerPin.ToString(), cfg.AdminPin))
        {
            return true;
        }

        return false;
    }

    public static void MapNegaApi(this WebApplication app)
    {
        // 0. Kiểm tra trạng thái xác thực & chính sách an toàn hiện tại
        app.MapGet("/api/auth/status", (HttpContext ctx) =>
        {
            var cfg = NegaRuntimeConfig.Current;
            bool isLocal = IsLocalRequest(ctx);
            bool authenticated = IsAuthorized(ctx);

            return Results.Ok(new
            {
                authenticated,
                isLocalhost = isLocal,
                requirePin = !authenticated,
                allowRealShutdown = cfg.AllowRealShutdown,
                clusterSecured = !string.IsNullOrWhiteSpace(cfg.ClusterKey)
            });
        });

        app.MapPost("/api/auth/verify", (AuthVerifyRequestDto req, HttpContext ctx) =>
        {
            var cfg = NegaRuntimeConfig.Current;
            if (!PacketAuthHelper.ConstantTimeEquals(req.Pin?.Trim() ?? string.Empty, cfg.AdminPin))
            {
                return Results.Json(new { ok = false, message = "Mã PIN quản trị không chính xác." }, statusCode: 401);
            }

            ctx.Response.Cookies.Append(SessionCookieName, SessionSecretToken, new CookieOptions
            {
                HttpOnly = true,
                SameSite = SameSiteMode.Strict,
                IsEssential = true,
                MaxAge = TimeSpan.FromHours(8)
            });

            return Results.Ok(new { ok = true, message = "Xác thực quản trị viên thành công." });
        });

        app.MapPost("/api/auth/logout", (HttpContext ctx) =>
        {
            ctx.Response.Cookies.Delete(SessionCookieName);
            return Results.Ok(new { ok = true });
        });

        // Nhóm API được bảo vệ (yêu cầu localhost hoặc phiên đã xác thực PIN)
        var protectedApi = app.MapGroup("/api")
            .AddEndpointFilter(async (context, next) =>
            {
                if (!IsAuthorized(context.HttpContext))
                {
                    return Results.Json(
                        new { ok = false, requireAuth = true, message = "Yêu cầu xác thực mã PIN quản trị khi truy cập qua LAN." },
                        statusCode: 401);
                }
                return await next(context);
            });

        // 1. Tổng quan trạng thái toàn bộ phòng máy, băng thông, danh sách ACK và nhật ký
        protectedApi.MapGet("/overview", (
            WorkstationRegistry registry,
            TelemetryService telemetry,
            LabSimulatorService simulator) =>
        {
            var cfg = NegaRuntimeConfig.Current;
            IReadOnlyList<WorkstationState> list = registry.GetAllSorted();
            double totalBandwidthKbps = Math.Round(list.Sum(w => w.BandwidthKbps), 1);
            double totalFps = Math.Round(list.Sum(w => w.CurrentFps), 1);
            int onlineCount = list.Count(w => w.StatusBadge != "Offline");
            int lockedCount = list.Count(w => w.IsLocked);

            return Results.Ok(new
            {
                serverName = cfg.MaskHostIdentity ? cfg.ServerDisplayName : Environment.MachineName,
                serverIp = SubnetBroadcastHelper.GetPrimaryLocalIpAddress(),
                udpPort = cfg.UdpPort,
                tcpPort = cfg.TcpPort,
                simulatedCount = simulator.ActiveSimulatedCount,
                security = new
                {
                    clusterSecured = !string.IsNullOrWhiteSpace(cfg.ClusterKey),
                    allowRealShutdown = cfg.AllowRealShutdown,
                    maskHostIdentity = cfg.MaskHostIdentity
                },
                metrics = new
                {
                    totalWorkstations = list.Count,
                    onlineCount,
                    lockedCount,
                    offlineCount = list.Count - onlineCount,
                    totalBandwidthKbps,
                    totalFps,
                    udpPackets = telemetry.UdpPacketsCount,
                    tcpFrames = telemetry.TcpFramesCount,
                    commandsSent = telemetry.CommandsSentCount
                },
                workstations = list.Select(w => new
                {
                    w.ClientId,
                    w.Hostname,
                    w.IpAddress,
                    w.OsDescription,
                    w.Username,
                    w.ScreenWidth,
                    w.ScreenHeight,
                    w.IsSimulated,
                    w.IsTcpConnected,
                    w.IsLocked,
                    w.IsShutdownPending,
                    w.StreamMode,
                    status = w.StatusBadge,
                    w.PingMs,
                    w.CurrentFps,
                    w.LastFrameBytes,
                    w.BandwidthKbps,
                    w.TotalFramesReceived,
                    w.LastAckDetail,
                    hasFrame = w.LatestJpegFrame is { Length: > 0 }
                }),
                alerts = telemetry.GetAlertSummaries(),
                logs = telemetry.GetRecentLogs(30)
            });
        });

        // 2. Lấy frame ảnh JPEG mới nhất (0x02 SCREEN_FRAME) của 1 máy trạm cụ thể
        protectedApi.MapGet("/frame/{clientId}", (string clientId, WorkstationRegistry registry, HttpContext ctx) =>
        {
            WorkstationState? state = registry.GetById(clientId);
            if (state?.LatestJpegFrame is null || state.LatestJpegFrame.Length == 0)
            {
                return Results.NotFound();
            }

            ctx.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
            return Results.File(state.LatestJpegFrame, "image/jpeg");
        });

        // 3. Phát gói UDP Broadcast DISCOVER thủ công ("Quét LAN ngay")
        protectedApi.MapPost("/discover", async (UdpDiscoveryService udpService) =>
        {
            await udpService.TriggerManualBroadcastAsync();
            return Results.Ok(new { ok = true, message = $"Đã phát gói UDP Broadcast DISCOVER trên cổng :{NegaRuntimeConfig.Current.UdpPort}" });
        });

        // 4. Gửi lệnh Khóa / Mở khóa chuột và bàn phím (0x03 CMD_LOCK_INPUT)
        protectedApi.MapPost("/control/lock", async (LockRequestDto req, TcpControlServer tcpServer) =>
        {
            string target = string.IsNullOrWhiteSpace(req.TargetClientId) ? "ALL" : req.TargetClientId;
            int affected = await tcpServer.SendLockInputCommandAsync(target, req.Locked, req.Message);
            return Results.Ok(new
            {
                ok = true,
                affected,
                message = req.Locked
                    ? $"Đã gửi lệnh KHÓA tới {affected} máy trạm"
                    : $"Đã gửi lệnh MỞ KHÓA tới {affected} máy trạm"
            });
        });

        // 5. Gửi lệnh Thông báo khẩn cấp (0x05 CMD_ALERT)
        protectedApi.MapPost("/control/alert", async (AlertRequestDto req, TcpControlServer tcpServer) =>
        {
            if (string.IsNullOrWhiteSpace(req.Message))
            {
                return Results.BadRequest(new { ok = false, message = "Nội dung thông báo không được để trống." });
            }

            string target = string.IsNullOrWhiteSpace(req.TargetClientId) ? "ALL" : req.TargetClientId;
            var (alertId, sentCount) = await tcpServer.SendEmergencyAlertCommandAsync(
                target,
                req.Title,
                req.Message,
                req.Severity ?? "urgent");

            return Results.Ok(new
            {
                ok = true,
                alertId,
                affected = sentCount,
                message = $"Đã phát thông báo khẩn tới {sentCount} máy trạm"
            });
        });

        // 6. Gửi lệnh Tắt máy từ xa hoặc Hủy tắt máy (0x04 CMD_SHUTDOWN)
        protectedApi.MapPost("/control/shutdown", async (ShutdownRequestDto req, TcpControlServer tcpServer) =>
        {
            string target = string.IsNullOrWhiteSpace(req.TargetClientId) ? "ALL" : req.TargetClientId;
            string action = string.IsNullOrWhiteSpace(req.Action) ? "shutdown" : req.Action;
            int affected = await tcpServer.SendShutdownCommandAsync(
                target,
                action,
                req.DelaySeconds > 0 ? req.DelaySeconds : 15,
                req.DryRun,
                req.Reason ?? "Kết thúc ca thực hành phòng máy");

            return Results.Ok(new
            {
                ok = true,
                affected,
                message = action == "cancel"
                    ? $"Đã gửi lệnh HỦY TẮT MÁY tới {affected} máy trạm"
                    : $"Đã gửi lệnh TẮT MÁY ({req.DelaySeconds}s) tới {affected} máy trạm"
            });
        });

        // 7. Chuyển đổi chế độ luồng ảnh JPEG: Grid View (480x270) vs Zoom View HD (960x540) (0x06 CMD_STREAM_CONFIG)
        protectedApi.MapPost("/control/stream-mode", async (StreamModeRequestDto req, TcpControlServer tcpServer) =>
        {
            bool ok = await tcpServer.SetClientStreamModeAsync(req.ClientId, req.Mode);
            return Results.Ok(new { ok });
        });

        // 8. Thay đổi số lượng máy trạm giả lập trong phòng máy (Lab Simulator)
        protectedApi.MapPost("/simulator", async (SimulatorCountRequestDto req, LabSimulatorService simulator) =>
        {
            int count = await simulator.SetSimulatedCountAsync(req.Count);
            return Results.Ok(new
            {
                ok = true,
                count,
                message = $"Đang chạy {count} máy trạm giả lập qua UDP Broadcast & TCP"
            });
        });
    }
}
