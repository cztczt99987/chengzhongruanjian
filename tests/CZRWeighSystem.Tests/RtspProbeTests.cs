using CZRWeighSystem.Video;

namespace CZRWeighSystem.Tests;

/// <summary>
/// RTSP 取流探测（"测试连接"功能）单元测试：
/// 状态行解析、Digest 质询解析、Digest 认证头黄金向量（预先用与实机 200 OK 同路径的 MD5 算好）、
/// 以及不可达端口的整体探测结果。不依赖真实摄像头。
/// </summary>
public class RtspProbeTests
{
    private const string Realm = "f47018d1cee8";
    private const string Nonce = "0123456789abcdef0123456789abcdef";
    private const string Uri = "rtsp://192.168.0.100:554/Streaming/Channels/101";

    // ---- 状态行解析 ----

    [Theory]
    [InlineData("RTSP/1.0 200 OK\r\nCSeq: 2\r\n\r\n", "200")]
    [InlineData("RTSP/1.0 401 Unauthorized\r\nCSeq: 2\r\n\r\n", "401")]
    [InlineData("RTSP/1.0 404 Not Found\r\n\r\n", "404")]
    [InlineData("", null)]
    [InlineData("garbage data not rtsp", null)]
    public void ParseStatusCode_Cases(string response, string? expected)
    {
        Assert.Equal(expected, RtspProbe.ParseStatusCode(response));
    }

    // ---- Digest 质询解析 ----

    [Fact]
    public void ParseDigestChallenge_RealDevice401Sample_Parsed()
    {
        // 实机（CS-H5）401 应答样例：Digest 行 + 附带的 Basic 行不应干扰解析
        const string sample =
            "RTSP/1.0 401 Unauthorized\r\n" +
            "CSeq: 1\r\n" +
            "WWW-Authenticate: Digest realm=\"f47018d1cee8\", nonce=\"db2a561d634b9d83c8dc85f6deb0b477\", stale=\"FALSE\"\r\n" +
            "WWW-Authenticate: Basic realm=\"f47018d1cee8\"\r\n" +
            "Date: Wed, Sep 30 2026 23:43:26 GMT\r\n\r\n";

        var c = RtspProbe.ParseDigestChallenge(sample);

        Assert.NotNull(c);
        Assert.Equal("f47018d1cee8", c.Realm);
        Assert.Equal("db2a561d634b9d83c8dc85f6deb0b477", c.Nonce);
        Assert.Null(c.Qop);   // 无 qop → 按 RFC 2617 老式摘要
    }

    [Fact]
    public void ParseDigestChallenge_WithQop_Parsed()
    {
        const string sample =
            "RTSP/1.0 401 Unauthorized\r\n" +
            "WWW-Authenticate: Digest realm=\"IP Camera\", nonce=\"abc123\", qop=\"auth\"\r\n\r\n";

        var c = RtspProbe.ParseDigestChallenge(sample);

        Assert.NotNull(c);
        Assert.Equal("IP Camera", c.Realm);
        Assert.Equal("abc123", c.Nonce);
        Assert.Equal("auth", c.Qop);
    }

    [Fact]
    public void ParseDigestChallenge_BasicOnly_ReturnsNull()
    {
        const string sample = "RTSP/1.0 401 Unauthorized\r\nWWW-Authenticate: Basic realm=\"x\"\r\n\r\n";
        Assert.Null(RtspProbe.ParseDigestChallenge(sample));
        Assert.Null(RtspProbe.ParseDigestChallenge("RTSP/1.0 200 OK\r\n\r\n"));
    }

    // ---- Digest 认证头（黄金向量：预先用与实机 200 OK 同路径的 MD5 算好）----

    [Fact]
    public void BuildDigestAuthorization_NoQop_MatchesGoldenVector()
    {
        var c = new DigestChallenge(Realm, Nonce, null);

        string authz = RtspProbe.BuildDigestAuthorization("admin", "abcdef", "DESCRIBE", Uri, c);

        Assert.Equal(
            "Digest username=\"admin\", realm=\"f47018d1cee8\", nonce=\"0123456789abcdef0123456789abcdef\", " +
            "uri=\"rtsp://192.168.0.100:554/Streaming/Channels/101\", response=\"e8345d3739cd58dfc62e7a04c663f2e1\"",
            authz);
    }

    [Fact]
    public void BuildDigestAuthorization_WithQopAuth_MatchesGoldenVector()
    {
        var c = new DigestChallenge(Realm, Nonce, "auth");

        string authz = RtspProbe.BuildDigestAuthorization("admin", "abcdef", "DESCRIBE", Uri, c);

        Assert.Equal(
            "Digest username=\"admin\", realm=\"f47018d1cee8\", nonce=\"0123456789abcdef0123456789abcdef\", " +
            "uri=\"rtsp://192.168.0.100:554/Streaming/Channels/101\", response=\"799ee80c4362b49cc44f99420f41f9fa\", " +
            "qop=auth, nc=00000001, cnonce=\"0a4f113b\"",
            authz);
    }

    [Fact]
    public void Md5Hex_KnownValue()
    {
        Assert.Equal("110e62d73682419b356b8df9db0e1fab", RtspProbe.Md5Hex("admin:f47018d1cee8:abcdef"));
    }

    // ---- 整体探测 ----

    [Fact]
    public void Check_ClosedPort_ReportsCannotConnect()
    {
        // 本机高位端口通常无监听：应快速返回"无法连接"而不是抛异常或误判其他状态
        var r = RtspProbe.Check("127.0.0.1", 59999, "admin", "x", timeoutMs: 1000);

        Assert.Equal(RtspProbeStatus.CannotConnect, r.Status);
        Assert.Contains("无法连接", r.Message);
    }
}