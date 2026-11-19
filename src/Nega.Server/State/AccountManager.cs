using System.Collections.Concurrent;
using System.Security.Cryptography;
using Nega.Common.Security;

namespace Nega.Server.State;

public sealed class UserAccount
{
    public string Username { get; init; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string Role { get; set; } = "Viewer"; // "Host" | "CoAdmin" | "Viewer"
    public string DisplayName { get; set; } = string.Empty;
    public string InviteToken { get; set; } = string.Empty;
    public string CreatedAtText { get; init; } = DateTime.Now.ToString("HH:mm:ss");
}

public sealed class ActiveSessionInfo
{
    public string SessionId { get; init; } = string.Empty;
    public string Username { get; init; } = string.Empty;
    public string Role { get; init; } = "Viewer";
    public string DisplayName { get; init; } = string.Empty;
    public DateTimeOffset ExpiresAtUtc { get; init; } = DateTimeOffset.UtcNow.AddHours(8);
}

/// <summary>
/// Quản lý danh sách Tài khoản truy cập từ xa (Host / CoAdmin / Viewer) và Phiên đăng nhập (Session).
/// Cho phép Máy chính (Host) cấp tài khoản + mật khẩu hoặc gửi Link mời nhanh cho các máy khác vào theo dõi / điều khiển.
/// </summary>
public sealed class AccountManager
{
    private readonly ConcurrentDictionary<string, UserAccount> _accounts = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, ActiveSessionInfo> _sessions = new(StringComparer.Ordinal);

    public AccountManager()
    {
        var cfg = NegaRuntimeConfig.Current;

        // 1. Tài khoản Host chính (lấy mật khẩu từ NEGA_ADMIN_PIN trong .env hoặc tự sinh)
        CreateOrUpdateAccount("admin", cfg.AdminPin, "Host", "Máy chủ Chính (Host)");

        // 2. Tạo sẵn 2 tài khoản mẫu để Host có thể gửi ngay cho đồng đội hoặc giảng viên
        string coAdminPass = RandomNumberGenerator.GetInt32(100000, 999999).ToString();
        string viewerPass = RandomNumberGenerator.GetInt32(100000, 999999).ToString();

        CreateOrUpdateAccount("coadmin", coAdminPass, "CoAdmin", "Đồng Quản trị (Nhóm)");
        CreateOrUpdateAccount("viewer", viewerPass, "Viewer", "Khách / Giảng viên (Chỉ xem)");
    }

    public UserAccount CreateOrUpdateAccount(string username, string password, string role, string? displayName = null)
    {
        username = username.Trim().ToLowerInvariant();
        string normalizedRole = NormalizeRole(role);
        string inviteToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));

        var account = new UserAccount
        {
            Username = username,
            Password = password.Trim(),
            Role = normalizedRole,
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? username : displayName.Trim(),
            InviteToken = inviteToken,
            CreatedAtText = DateTime.Now.ToString("HH:mm:ss")
        };

        _accounts[username] = account;
        return account;
    }

    public bool RemoveAccount(string username)
    {
        if (string.Equals(username, "admin", StringComparison.OrdinalIgnoreCase))
        {
            return false; // Không cho xóa tài khoản gốc
        }

        bool removed = _accounts.TryRemove(username.Trim(), out _);
        if (removed)
        {
            foreach (var kv in _sessions.Where(s => string.Equals(s.Value.Username, username, StringComparison.OrdinalIgnoreCase)).ToList())
            {
                _sessions.TryRemove(kv.Key, out _);
            }
        }
        return removed;
    }

    public IReadOnlyList<UserAccount> GetAllAccounts()
    {
        return _accounts.Values
            .OrderBy(a => a.Role == "Host" ? 0 : (a.Role == "CoAdmin" ? 1 : 2))
            .ThenBy(a => a.Username)
            .ToList();
    }

    public ActiveSessionInfo? AuthenticateCredentials(string username, string password)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            return null;
        }

        // Hỗ trợ đăng nhập nhanh nếu chỉ nhập mã PIN vào ô mật khẩu với username = "admin"
        if (_accounts.TryGetValue(username.Trim(), out UserAccount? account))
        {
            if (PacketAuthHelper.ConstantTimeEquals(password.Trim(), account.Password))
            {
                return CreateSession(account);
            }
        }

        return null;
    }

    public ActiveSessionInfo? AuthenticateInviteToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        UserAccount? match = _accounts.Values.FirstOrDefault(a =>
            PacketAuthHelper.ConstantTimeEquals(token.Trim(), a.InviteToken));

        return match is not null ? CreateSession(match) : null;
    }

    public ActiveSessionInfo? GetSession(string? sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return null;
        }

        if (_sessions.TryGetValue(sessionId, out ActiveSessionInfo? session))
        {
            if (session.ExpiresAtUtc > DateTimeOffset.UtcNow)
            {
                return session;
            }
            _sessions.TryRemove(sessionId, out _);
        }
        return null;
    }

    public void RevokeSession(string? sessionId)
    {
        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            _sessions.TryRemove(sessionId, out _);
        }
    }

    private ActiveSessionInfo CreateSession(UserAccount account)
    {
        string sessionId = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        var session = new ActiveSessionInfo
        {
            SessionId = sessionId,
            Username = account.Username,
            Role = account.Role,
            DisplayName = account.DisplayName,
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(8)
        };
        _sessions[sessionId] = session;
        return session;
    }

    private static string NormalizeRole(string? role)
    {
        if (string.Equals(role, "Host", StringComparison.OrdinalIgnoreCase)) return "Host";
        if (string.Equals(role, "CoAdmin", StringComparison.OrdinalIgnoreCase)) return "CoAdmin";
        return "Viewer";
    }
}
