using Nega.Server.Bridge;
using Nega.Server.State;

namespace Nega.Tests;

public sealed class AccountManagerAndQrTests
{
    [Fact]
    public void Constructor_InitializesHostCoAdminAndViewerAccounts()
    {
        var manager = new AccountManager();
        IReadOnlyList<UserAccount> accounts = manager.GetAllAccounts();

        Assert.Contains(accounts, a => a.Username == "admin" && a.Role == "Host");
        Assert.Contains(accounts, a => a.Username == "coadmin" && a.Role == "CoAdmin");
        Assert.Contains(accounts, a => a.Username == "viewer" && a.Role == "Viewer");
        Assert.All(accounts, a => Assert.False(string.IsNullOrWhiteSpace(a.InviteToken)));
    }

    [Fact]
    public void AuthenticateCredentialsAndInviteToken_AssignsExpectedRole()
    {
        var manager = new AccountManager();
        UserAccount created = manager.CreateOrUpdateAccount("giamthi2", "pass1234", "CoAdmin", "Giám thị 2");

        ActiveSessionInfo? wrongPass = manager.AuthenticateCredentials("giamthi2", "wrong");
        Assert.Null(wrongPass);

        ActiveSessionInfo? okPass = manager.AuthenticateCredentials("giamthi2", "pass1234");
        Assert.NotNull(okPass);
        Assert.Equal("CoAdmin", okPass!.Role);

        ActiveSessionInfo? okToken = manager.AuthenticateInviteToken(created.InviteToken);
        Assert.NotNull(okToken);
        Assert.Equal("giamthi2", okToken!.Username);
        Assert.Equal("CoAdmin", okToken.Role);
        Assert.NotNull(manager.GetSession(okToken.SessionId));
    }

    [Fact]
    public void RemoveAccount_ProtectsRootAdminAndRevokesCustomUserSessions()
    {
        var manager = new AccountManager();
        Assert.False(manager.RemoveAccount("admin"));

        manager.CreateOrUpdateAccount("tempviewer", "123456", "Viewer");
        ActiveSessionInfo? session = manager.AuthenticateCredentials("tempviewer", "123456");
        Assert.NotNull(session);

        Assert.True(manager.RemoveAccount("tempviewer"));
        Assert.Null(manager.GetSession(session!.SessionId));
    }

    [Theory]
    [InlineData("http://192.168.1.50:5050")]
    [InlineData("http://192.168.1.50:5050/?invite=ABCDEF0123456789ABCDEF0123456789")]
    [InlineData("https://nega.local/very-long-url-over-130-chars-0123456789-0123456789-0123456789-0123456789-0123456789-0123456789-0123456789-0123456789")]
    public void QrSvgGenerator_GeneratesOfflineSvgWithoutExceptions(string inputUrl)
    {
        string svg = QrSvgGenerator.GenerateSvg(inputUrl);

        Assert.StartsWith("<svg ", svg);
        Assert.Contains("xmlns=\"http://www.w3.org/2000/svg\"", svg);
        Assert.Contains("<path fill=\"#0F172A\" d=\"M", svg);
        Assert.EndsWith("</svg>", svg);
    }
}
