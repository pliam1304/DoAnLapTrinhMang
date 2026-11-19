using System.Net;
using Nega.Common.Networking;
using Nega.Common.Security;
using Nega.Server.Discovery;
using Nega.Server.State;
using Nega.Server.TcpServer;

namespace Nega.Server.Bridge;

public sealed record AuthLoginRequestDto(string? Username, string? Password, string? Pin, string? InviteToken);
public sealed record CreateAccountRequestDto(string Username, string Password, string Role, string? DisplayName);
public sealed record LockRequestDto(string TargetClientId, bool Locked, string? Message);
public sealed record AlertRequestDto(string TargetClientId, string Title, string Message, string? Severity);
public sealed record ShutdownRequestDto(string TargetClientId, string Action, int DelaySeconds, bool DryRun, string? Reason);
public sealed record StreamModeRequestDto(string ClientId, string Mode);
public sealed record SimulatorCountRequestDto(int Count);

/// <summary>
/// Định tuyến REST API, Quản lý Tài khoản Phân quyền (Host / CoAdmin / Viewer) và Chia sẻ Link / QR Code.
/// </summary>
public static class ServerApiEndpoints
{
    private const string SessionCookieName = "nega_session";

    private static bool IsLocalRequest(HttpContext ctx)
    {
        IPAddress? remoteIp = ctx.Connection.RemoteIpAddress;
        return remoteIp is not null && IPAddress.IsLoopback(remoteIp);
    }

    private static ActiveSessionInfo? ResolveCallerSession(HttpContext ctx, AccountManager accounts)
    {
        var cfg = NegaRuntimeConfig.Current;

        // 1. Kiểm tra Cookie phiên đăng nhập
        if (ctx.Request.Cookies.TryGetValue(SessionCookieName, out string? cookieSessionId))
        {
            ActiveSessionInfo? cookieSession = accounts.GetSession(cookieSessionId);
            if (cookieSession is not null)
            {
                return cookieSession;
            }
        }

        // 2. Nếu mở trực tiếp trên máy chủ (localhost) và không bắt buộc PIN trên localhost -> Quyền Host
        if (!cfg.RequirePinOnLocalhost && IsLocalRequest(ctx))
        {
            return new ActiveSessionInfo
            {
                SessionId = "local-host",
                Username = "host",
                Role = "Host",
                DisplayName = "Máy chủ Chính (Host)"
            };
        }

        // 3. Hỗ trợ Header X-Nega-Admin-Pin cho script tự động hóa
        if (ctx.Request.Headers.TryGetValue("X-Nega-Admin-Pin", out var headerPin) &&
            PacketAuthHelper.ConstantTimeEquals(headerPin.ToString(), cfg.AdminPin))
        {
            return new ActiveSessionInfo
            {
                SessionId = "header-admin",
                Username = "admin",
                Role = "Host",
                DisplayName = "Máy chủ Chính (Host)"
            };
        }

        return null;
    }

    private static bool CanControl(ActiveSessionInfo? session) =>
        session is not null && (session.Role == "Host" || session.Role == "CoAdmin");

    private static bool IsHost(ActiveSessionInfo? session) =>
        session is not null && session.Role == "Host";

    public static void MapNegaApi(this WebApplication app)
    {
        // 0. Kiểm tra trạng thái đăng nhập & vai trò hiện tại
        app.MapGet("/api/auth/status", (HttpContext ctx, AccountManager accounts) =>
        {
            var cfg = NegaRuntimeConfig.Current;
            bool isLocal = IsLocalRequest(ctx);
            ActiveSessionInfo? session = ResolveCallerSession(ctx, accounts);

            return Results.Ok(new
            {
                authenticated = session is not null,
                username = session?.Username ?? string.Empty,
                role = session?.Role ?? string.Empty,
                displayName = session?.DisplayName ?? string.Empty,
                canControl = CanControl(session),
                isHost = IsHost(session),
                isLocalhost = isLocal,
                requirePin = session is null,
                allowRealShutdown = cfg.AllowRealShutdown,
                clusterSecured = !string.IsNullOrWhiteSpace(cfg.ClusterKey)
            });
        });

        // Đăng nhập bằng Username + Password, Invite Token, hoặc Mã PIN
        Func<AuthLoginRequestDto, HttpContext, AccountManager, TelemetryService, IResult> loginHandler =
            (req, ctx, accounts, telemetry) =>
            {
                ActiveSessionInfo? session = null;
                if (!string.IsNullOrWhiteSpace(req.InviteToken))
                {
                    session = accounts.AuthenticateInviteToken(req.InviteToken);
                }
                else if (!string.IsNullOrWhiteSpace(req.Username) && !string.IsNullOrWhiteSpace(req.Password))
                {
                    session = accounts.AuthenticateCredentials(req.Username, req.Password);
                }
                else if (!string.IsNullOrWhiteSpace(req.Pin))
                {
                    session = accounts.AuthenticateCredentials("admin", req.Pin);
                }

                if (session is null)
                {
                    return Results.Json(
                        new { ok = false, message = "Tài khoản, mật khẩu hoặc link mời không hợp lệ." },
                        statusCode: 401);
                }

                ctx.Response.Cookies.Append(SessionCookieName, session.SessionId, new CookieOptions
                {
                    HttpOnly = true,
                    SameSite = SameSiteMode.Strict,
                    IsEssential = true,
                    MaxAge = TimeSpan.FromHours(8)
                });

                string remoteIp = ctx.Connection.RemoteIpAddress?.ToString() ?? "LAN";
                telemetry.AddLog("AUTH", $"[@{session.Username}] đã đăng nhập từ {remoteIp} (Quyền: {session.Role})");

                return Results.Ok(new
                {
                    ok = true,
                    username = session.Username,
                    role = session.Role,
                    displayName = session.DisplayName,
                    canControl = CanControl(session),
                    isHost = IsHost(session),
                    message = $"Đăng nhập thành công với quyền {session.Role}"
                });
            };

        app.MapPost("/api/auth/login", loginHandler);
        app.MapPost("/api/auth/verify", loginHandler);

        app.MapPost("/api/auth/logout", (HttpContext ctx, AccountManager accounts) =>
        {
            if (ctx.Request.Cookies.TryGetValue(SessionCookieName, out string? sid))
            {
                accounts.RevokeSession(sid);
            }
            ctx.Response.Cookies.Delete(SessionCookieName);
            return Results.Ok(new { ok = true });
        });

        // Nhóm API yêu cầu đã đăng nhập (Viewer, CoAdmin, hoặc Host đều xem được)
        var protectedApi = app.MapGroup("/api")
            .AddEndpointFilter(async (context, next) =>
            {
                var accounts = context.HttpContext.RequestServices.GetRequiredService<AccountManager>();
                ActiveSessionInfo? session = ResolveCallerSession(context.HttpContext, accounts);
                if (session is null)
                {
                    return Results.Json(
                        new { ok = false, requireAuth = true, message = "Vui lòng đăng nhập tài khoản được cấp để theo dõi phòng máy." },
                        statusCode: 401);
                }
                context.HttpContext.Items["NegaSession"] = session;
                return await next(context);
            });

        // 1. Tổng quan trạng thái toàn bộ phòng máy
        protectedApi.MapGet("/overview", (
            HttpContext ctx,
            WorkstationRegistry registry,
            TelemetryService telemetry,
            LabSimulatorService simulator) =>
        {
            var cfg = NegaRuntimeConfig.Current;
            var session = ctx.Items["NegaSession"] as ActiveSessionInfo;
            IReadOnlyList<WorkstationState> list = registry.GetAllSorted();
            double totalBandwidthKbps = Math.Round(list.Sum(w => w.BandwidthKbps), 1);
            double totalFps = Math.Round(list.Sum(w => w.CurrentFps), 1);
            int onlineCount = list.Count(w => w.StatusBadge != "Offline");
            int lockedCount = list.Count(w => w.IsLocked);

            return Results.Ok(new
            {
                serverName = cfg.MaskHostIdentity ? cfg.ServerDisplayName : Environment.MachineName,
                serverIp = SubnetBroadcastHelper.GetPrimaryLocalIpAddress(),
                httpPort = cfg.HttpPort,
                udpPort = cfg.UdpPort,
                tcpPort = cfg.TcpPort,
                simulatedCount = simulator.ActiveSimulatedCount,
                currentUser = new
                {
                    username = session?.Username ?? "viewer",
                    role = session?.Role ?? "Viewer",
                    displayName = session?.DisplayName ?? "Viewer",
                    canControl = CanControl(session),
                    isHost = IsHost(session)
                },
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

        // 2. Lấy frame ảnh JPEG mới nhất (0x02 SCREEN_FRAME)
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

        // 3. Chuyển đổi chế độ luồng ảnh JPEG Grid/Zoom (Viewer cũng được phép phóng to để quan sát)
        protectedApi.MapPost("/control/stream-mode", async (StreamModeRequestDto req, TcpControlServer tcpServer) =>
        {
            bool ok = await tcpServer.SetClientStreamModeAsync(req.ClientId, req.Mode);
            return Results.Ok(new { ok });
        });

        // 4. Phát gói UDP Broadcast DISCOVER thủ công (Chỉ CoAdmin & Host)
        protectedApi.MapPost("/discover", async (HttpContext ctx, UdpDiscoveryService udpService) =>
        {
            var session = ctx.Items["NegaSession"] as ActiveSessionInfo;
            if (!CanControl(session))
            {
                return Results.Json(new { ok = false, message = "Tài khoản Chỉ theo dõi (Viewer) không có quyền phát lệnh quét mạng." }, statusCode: 403);
            }

            await udpService.TriggerManualBroadcastAsync();
            return Results.Ok(new { ok = true, message = $"Đã phát gói UDP Broadcast DISCOVER trên cổng :{NegaRuntimeConfig.Current.UdpPort}" });
        });

        // 5. Gửi lệnh Khóa / Mở khóa chuột và bàn phím (Chỉ CoAdmin & Host)
        protectedApi.MapPost("/control/lock", async (HttpContext ctx, LockRequestDto req, TcpControlServer tcpServer) =>
        {
            var session = ctx.Items["NegaSession"] as ActiveSessionInfo;
            if (!CanControl(session))
            {
                return Results.Json(new { ok = false, message = "Tài khoản Chỉ theo dõi (Viewer) không có quyền khóa máy trạm." }, statusCode: 403);
            }

            string target = string.IsNullOrWhiteSpace(req.TargetClientId) ? "ALL" : req.TargetClientId;
            int affected = await tcpServer.SendLockInputCommandAsync(target, req.Locked, req.Message, session!.Username);
            return Results.Ok(new
            {
                ok = true,
                affected,
                message = req.Locked
                    ? $"[@{session.Username}] Đã gửi lệnh KHÓA tới {affected} máy trạm"
                    : $"[@{session.Username}] Đã gửi lệnh MỞ KHÓA tới {affected} máy trạm"
            });
        });

        // 6. Gửi lệnh Thông báo khẩn cấp (Chỉ CoAdmin & Host)
        protectedApi.MapPost("/control/alert", async (HttpContext ctx, AlertRequestDto req, TcpControlServer tcpServer) =>
        {
            var session = ctx.Items["NegaSession"] as ActiveSessionInfo;
            if (!CanControl(session))
            {
                return Results.Json(new { ok = false, message = "Tài khoản Chỉ theo dõi (Viewer) không có quyền phát thông báo khẩn." }, statusCode: 403);
            }

            if (string.IsNullOrWhiteSpace(req.Message))
            {
                return Results.BadRequest(new { ok = false, message = "Nội dung thông báo không được để trống." });
            }

            string target = string.IsNullOrWhiteSpace(req.TargetClientId) ? "ALL" : req.TargetClientId;
            var (alertId, sentCount) = await tcpServer.SendEmergencyAlertCommandAsync(
                target,
                req.Title,
                req.Message,
                req.Severity ?? "urgent",
                session!.Username);

            return Results.Ok(new
            {
                ok = true,
                alertId,
                affected = sentCount,
                message = $"[@{session.Username}] Đã phát thông báo khẩn tới {sentCount} máy trạm"
            });
        });

        // 7. Gửi lệnh Tắt máy từ xa hoặc Hủy tắt máy (Chỉ CoAdmin & Host)
        protectedApi.MapPost("/control/shutdown", async (HttpContext ctx, ShutdownRequestDto req, TcpControlServer tcpServer) =>
        {
            var session = ctx.Items["NegaSession"] as ActiveSessionInfo;
            if (!CanControl(session))
            {
                return Results.Json(new { ok = false, message = "Tài khoản Chỉ theo dõi (Viewer) không có quyền tắt máy trạm." }, statusCode: 403);
            }

            string target = string.IsNullOrWhiteSpace(req.TargetClientId) ? "ALL" : req.TargetClientId;
            string action = string.IsNullOrWhiteSpace(req.Action) ? "shutdown" : req.Action;
            int affected = await tcpServer.SendShutdownCommandAsync(
                target,
                action,
                req.DelaySeconds > 0 ? req.DelaySeconds : 15,
                req.DryRun,
                req.Reason ?? "Kết thúc ca thực hành phòng máy",
                session!.Username);

            return Results.Ok(new
            {
                ok = true,
                affected,
                message = action == "cancel"
                    ? $"[@{session.Username}] Đã gửi lệnh HỦY TẮT MÁY tới {affected} máy trạm"
                    : $"[@{session.Username}] Đã gửi lệnh TẮT MÁY ({req.DelaySeconds}s) tới {affected} máy trạm"
            });
        });

        // 8. Thay đổi số lượng máy trạm giả lập (Chỉ CoAdmin & Host)
        protectedApi.MapPost("/simulator", async (HttpContext ctx, SimulatorCountRequestDto req, LabSimulatorService simulator) =>
        {
            var session = ctx.Items["NegaSession"] as ActiveSessionInfo;
            if (!CanControl(session))
            {
                return Results.Json(new { ok = false, message = "Tài khoản Chỉ theo dõi (Viewer) không có quyền thay đổi cụm giả lập." }, statusCode: 403);
            }

            int count = await simulator.SetSimulatedCountAsync(req.Count);
            return Results.Ok(new
            {
                ok = true,
                count,
                message = $"[@{session!.Username}] Đang chạy {count} máy trạm giả lập qua UDP Broadcast & TCP"
            });
        });

        // 9. Sinh mã QR Code SVG offline cho đường dẫn chia sẻ (Chỉ dành cho Host)
        protectedApi.MapGet("/qr", (string? text, HttpContext ctx) =>
        {
            string svg = QrSvgGenerator.GenerateSvg(string.IsNullOrWhiteSpace(text) ? "http://localhost:5050" : text);
            ctx.Response.Headers.CacheControl = "no-store";
            return Results.Content(svg, "image/svg+xml; charset=utf-8");
        });

        // 10. Quản lý Tài khoản & Chia sẻ Link / QR Code (Chỉ dành riêng cho Máy chính Host)
        protectedApi.MapGet("/accounts", (HttpContext ctx, AccountManager accounts) =>
        {
            var session = ctx.Items["NegaSession"] as ActiveSessionInfo;
            if (!IsHost(session))
            {
                return Results.Json(new { ok = false, message = "Chỉ Máy chủ Chính (Host) mới được xem và cấp tài khoản." }, statusCode: 403);
            }

            string lanIp = SubnetBroadcastHelper.GetPrimaryLocalIpAddress();
            int port = NegaRuntimeConfig.Current.HttpPort;
            string baseUrl = $"http://{lanIp}:{port}";

            return Results.Ok(new
            {
                ok = true,
                lanBaseUrl = baseUrl,
                accounts = accounts.GetAllAccounts().Select(a => new
                {
                    a.Username,
                    a.Password,
                    a.Role,
                    a.DisplayName,
                    a.CreatedAtText,
                    a.InviteToken,
                    inviteUrl = $"{baseUrl}/?invite={a.InviteToken}"
                })
            });
        });

        protectedApi.MapPost("/accounts", (HttpContext ctx, CreateAccountRequestDto req, AccountManager accounts, TelemetryService telemetry) =>
        {
            var session = ctx.Items["NegaSession"] as ActiveSessionInfo;
            if (!IsHost(session))
            {
                return Results.Json(new { ok = false, message = "Chỉ Máy chủ Chính (Host) mới có quyền tạo/cấp tài khoản." }, statusCode: 403);
            }

            if (string.IsNullOrWhiteSpace(req.Username) || string.IsNullOrWhiteSpace(req.Password))
            {
                return Results.BadRequest(new { ok = false, message = "Vui lòng nhập đầy đủ Tên đăng nhập và Mật khẩu." });
            }

            UserAccount acc = accounts.CreateOrUpdateAccount(req.Username, req.Password, req.Role, req.DisplayName);
            telemetry.AddLog("AUTH", $"[@{session!.Username}] đã cấp tài khoản @{acc.Username} (Quyền: {acc.Role})");

            return Results.Ok(new
            {
                ok = true,
                message = $"Đã cấp tài khoản @{acc.Username} (Quyền: {acc.Role})"
            });
        });

        protectedApi.MapDelete("/accounts/{username}", (string username, HttpContext ctx, AccountManager accounts, TelemetryService telemetry) =>
        {
            var session = ctx.Items["NegaSession"] as ActiveSessionInfo;
            if (!IsHost(session))
            {
                return Results.Json(new { ok = false, message = "Chỉ Máy chủ Chính (Host) mới có quyền xóa tài khoản." }, statusCode: 403);
            }

            bool ok = accounts.RemoveAccount(username);
            if (!ok)
            {
                return Results.BadRequest(new { ok = false, message = "Không thể xóa tài khoản gốc hoặc tài khoản không tồn tại." });
            }

            telemetry.AddLog("AUTH", $"[@{session!.Username}] đã thu hồi tài khoản @{username}");
            return Results.Ok(new { ok = true, message = $"Đã thu hồi tài khoản @{username}" });
        });
    }
}
