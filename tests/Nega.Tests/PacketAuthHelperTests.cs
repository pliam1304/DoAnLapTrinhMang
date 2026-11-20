using Nega.Common.Security;

namespace Nega.Tests;

public sealed class PacketAuthHelperTests
{
    [Fact]
    public void ComputeTag_WhenClusterKeyEmpty_ReturnsEmptyString()
    {
        string tag = PacketAuthHelper.ComputeTag("", "may-01", "MAY-01", "1000");
        Assert.Equal(string.Empty, tag);
    }

    [Fact]
    public void VerifyTag_WhenClusterKeyEmpty_AllowsDevModeWithoutTag()
    {
        bool ok = PacketAuthHelper.VerifyTag("", null, 0, "may-01", "MAY-01");
        Assert.True(ok);
    }

    [Fact]
    public void VerifyTag_WithValidKeyAndTimestamp_ReturnsTrue()
    {
        const string key = "nega-lab-secret-2026";
        long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        string tag = PacketAuthHelper.ComputeTag(key, "may-01", "MAY-01", nowMs.ToString());

        Assert.True(PacketAuthHelper.VerifyTag(key, tag.ToLowerInvariant(), nowMs, "may-01", "MAY-01", nowMs.ToString()));
    }

    [Fact]
    public void VerifyTag_WhenFieldTamperedOrExpired_ReturnsFalse()
    {
        const string key = "nega-lab-secret-2026";
        long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        string tag = PacketAuthHelper.ComputeTag(key, "may-01", "MAY-01", nowMs.ToString());

        // Tampered hostname
        Assert.False(PacketAuthHelper.VerifyTag(key, tag, nowMs, "may-01", "EVIL-PC", nowMs.ToString()));

        // Expired timestamp (> 120s window)
        long expiredMs = nowMs - 150_000;
        string expiredTag = PacketAuthHelper.ComputeTag(key, "may-01", "MAY-01", expiredMs.ToString());
        Assert.False(PacketAuthHelper.VerifyTag(key, expiredTag, expiredMs, "may-01", "MAY-01", expiredMs.ToString()));
    }

    [Fact]
    public void ConstantTimeEquals_ComparesAccurately()
    {
        Assert.True(PacketAuthHelper.ConstantTimeEquals("317927", "317927"));
        Assert.False(PacketAuthHelper.ConstantTimeEquals("317927", "317928"));
        Assert.False(PacketAuthHelper.ConstantTimeEquals("317927", "31792"));
    }
}
