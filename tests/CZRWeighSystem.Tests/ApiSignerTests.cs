using CZRWeighSystem.Sync;

namespace CZRWeighSystem.Tests;

/// <summary>
/// 平台接口签名测试（对接文档 §3.2）：
/// SHA-256 / HMAC-SHA256 采用公开标准测试向量（FIPS 180-1 示例、RFC 4231 Case 2），
/// 签名串四要素按 appKey\n时间戳\nnonce\nbodyHash 换行拼接，multipart 元数据串按约定拼接。
/// </summary>
public class ApiSignerTests
{
    [Fact]
    public void Sha256Hex_标准向量()
    {
        // SHA-256("abc")（FIPS 示例）
        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad",
            ApiSigner.Sha256Hex("abc"));
    }

    [Fact]
    public void HmacSha256Hex_RFC4231标准向量()
    {
        // RFC 4231 Test Case 2：key="Jefe"，data="what do ya want for nothing?"
        Assert.Equal("5bdcc146bf60754e6a042426089575c75a003f089d2739839dec58b964ec3843",
            ApiSigner.HmacSha256Hex("Jefe", "what do ya want for nothing?"));
    }

    [Fact]
    public void BuildSignStr_四要素按换行拼接()
    {
        Assert.Equal("key1\n1700000000000\nabc123\nhash00",
            ApiSigner.BuildSignStr("key1", "1700000000000", "abc123", "hash00"));
    }

    [Fact]
    public void MultipartBodyHash_按appKey时间戳大小哈希拼接()
    {
        string expected = ApiSigner.Sha256Hex("KEY" + "1700000000000" + "1024" + "f00d");
        Assert.Equal(expected, ApiSigner.MultipartBodyHash("KEY", "1700000000000", 1024, "f00d"));
    }

    [Fact]
    public void NewNonce_16位十六进制且不重复()
    {
        var nonces = Enumerable.Range(0, 100).Select(_ => ApiSigner.NewNonce()).ToList();
        Assert.All(nonces, n =>
        {
            Assert.Equal(16, n.Length);
            Assert.Matches("^[0-9a-f]{16}$", n);
        });
        Assert.Equal(nonces.Count, nonces.Distinct().Count());   // 100 次不重复
    }

    [Fact]
    public void NowTimestamp_为毫秒级时间戳()
    {
        long ts = ApiSigner.NowTimestamp();
        // 与当前时间偏差应在 1 分钟内（毫秒级 Unix 时间）
        long expected = DateTimeOffset.Now.ToUnixTimeMilliseconds();
        Assert.InRange(ts, expected - 60_000, expected + 60_000);
    }
}
