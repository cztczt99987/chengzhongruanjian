using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace CZRWeighSystem.Video;

/// <summary>取流探测结果类型</summary>
public enum RtspProbeStatus
{
    /// <summary>认证与取流路径均正常</summary>
    Success,
    /// <summary>TCP 连接失败（IP/网络不通）</summary>
    CannotConnect,
    /// <summary>端口能连上但不是 RTSP 服务（如误填 SDK 端口 8000）</summary>
    NotRtsp,
    /// <summary>认证失败（用户名/密码错误）</summary>
    AuthFailed,
    /// <summary>认证通过但取流路径不存在</summary>
    PathNotFound,
    /// <summary>其他异常</summary>
    Error,
}

/// <summary>探测结果（界面可直接展示的结论与说明）</summary>
public record RtspProbeResult(RtspProbeStatus Status, string Message);

/// <summary>
/// RTSP 取流连通性探测（摄像头设置"测试连接"按钮用）。
/// 与 VLC 取流流程一致：在同一个 TCP 连接内先发无认证 DESCRIBE 拿到 Digest 质询，
/// 再带认证重发 DESCRIBE，按应答精确区分：连不上 / 端口非 RTSP / 密码错 / 路径不存在 / 成功。
/// 实测要点：海康/萤石设备把 Digest nonce 绑定在 TCP 连接上，必须同连接重发，换连接会一直 401。
/// </summary>
public static class RtspProbe
{
    /// <summary>
    /// 探测指定摄像头的取流可用性（阻塞数秒，UI 调用须放后台线程）。
    /// </summary>
    public static RtspProbeResult Check(string ip, int port, string user, string pass,
        string path = CameraConfig.DefaultRtspPath, int timeoutMs = 4000)
    {
        using var client = new TcpClient();
        try
        {
            var ar = client.BeginConnect(ip, port, null, null);
            if (!ar.AsyncWaitHandle.WaitOne(timeoutMs)) return CannotConnect(ip, port);
            client.EndConnect(ar);
        }
        catch (Exception ex)
        {
            Log.Info($"测试连接 {ip}:{port} 网络不通：{ex.Message}");
            return CannotConnect(ip, port);
        }

        try
        {
            var stream = client.GetStream();
            stream.ReadTimeout = timeoutMs;
            stream.WriteTimeout = timeoutMs;
            string uri = $"rtsp://{ip}:{port}{path}";

            // ① 无认证 DESCRIBE：正常应得到 401 + Digest 质询（也兼容设备直接放行返回 200）
            if (!Send(stream, BuildRequest("DESCRIBE", uri, 1, null))) return NotRtsp(ip, port);
            string resp1 = Read(stream);
            string? status1 = ParseStatusCode(resp1);
            if (status1 == null) return NotRtsp(ip, port);
            if (status1 == "200") return Ok(ip, port);
            if (status1 != "401")
                return new RtspProbeResult(RtspProbeStatus.Error, $"设备返回 {status1}，无法继续测试。");

            var challenge = ParseDigestChallenge(resp1);
            if (challenge == null)
                return new RtspProbeResult(RtspProbeStatus.Error, "设备要求的认证方式不受支持（非 Digest 认证）。");

            // ② 带 Digest 认证重发 DESCRIBE（同一 TCP 连接）
            string authz = BuildDigestAuthorization(user, pass, "DESCRIBE", uri, challenge);
            if (!Send(stream, BuildRequest("DESCRIBE", uri, 2, authz))) return NotRtsp(ip, port);
            string resp2 = Read(stream);
            string? status2 = ParseStatusCode(resp2);
            Log.Info($"测试连接 {ip}:{port}{path} => {status2 ?? "无应答"}");
            return status2 switch
            {
                "200" => Ok(ip, port),
                "401" => new RtspProbeResult(RtspProbeStatus.AuthFailed,
                    "用户名或密码错误。\r\n\r\n" +
                    "萤石消费系列（CS-* 等）：密码填机身标签上的 6 位大写字母验证码，用户名填 admin；\r\n" +
                    "海康专业系列：填设备激活时设置的管理密码。"),
                "404" => new RtspProbeResult(RtspProbeStatus.PathNotFound,
                    "用户名密码正确，但设备的取流路径不存在（固件路径格式不同）。"),
                null => NotRtsp(ip, port),
                _ => new RtspProbeResult(RtspProbeStatus.Error, $"设备返回 {status2}，取流失败。"),
            };
        }
        catch (Exception ex)
        {
            Log.Error($"测试连接 {ip}:{port} 异常", ex);
            return new RtspProbeResult(RtspProbeStatus.Error, "测试过程出错：" + ex.Message);
        }
    }

    private static RtspProbeResult CannotConnect(string ip, int port) => new(RtspProbeStatus.CannotConnect,
        $"无法连接 {ip}:{port}。\r\n\r\n请检查：IP 是否正确、摄像头是否通电联网、电脑与摄像头是否在同一局域网。");

    private static RtspProbeResult NotRtsp(string ip, int port) => new(RtspProbeStatus.NotRtsp,
        $"{ip}:{port} 能连通，但对方未响应 RTSP 协议。\r\n\r\n" +
        "若端口填的不是 554：海康/萤石摄像头的 RTSP 端口是 554（8000 是 SDK 端口，不能用于取流）。");

    private static RtspProbeResult Ok(string ip, int port) => new(RtspProbeStatus.Success,
        $"连接成功！{ip}:{port} 取流地址有效，保存设置后画面即可显示。");

    /// <summary>拼一条 RTSP 请求（可选带认证头）。</summary>
    private static string BuildRequest(string method, string uri, int cseq, string? authorization)
    {
        var sb = new StringBuilder();
        sb.Append($"{method} {uri} RTSP/1.0\r\n");
        sb.Append($"CSeq: {cseq}\r\n");
        if (authorization != null) sb.Append($"Authorization: {authorization}\r\n");
        sb.Append("User-Agent: CZRWeighSystem\r\n");
        sb.Append("\r\n");
        return sb.ToString();
    }

    private static bool Send(NetworkStream stream, string text)
    {
        try
        {
            byte[] bytes = Encoding.ASCII.GetBytes(text);
            stream.Write(bytes, 0, bytes.Length);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>读一条 RTSP 应答（读到响应头结束即返回；超时/无应答返回空串）。</summary>
    private static string Read(NetworkStream stream)
    {
        var sb = new StringBuilder();
        byte[] buf = new byte[4096];
        try
        {
            while (!sb.ToString().Contains("\r\n\r\n"))
            {
                int n = stream.Read(buf, 0, buf.Length);
                if (n <= 0) break;
                sb.Append(Encoding.ASCII.GetString(buf, 0, n));
                if (sb.Length > 65536) break;
            }
        }
        catch (IOException) { }             // 读超时：返回已读内容（空串 = 无应答）
        catch (ObjectDisposedException) { }
        return sb.ToString();
    }

    /// <summary>取应答首行的状态码（"RTSP/1.0 200 OK" → "200"；非 RTSP 应答返回 null）。</summary>
    internal static string? ParseStatusCode(string response)
    {
        if (string.IsNullOrEmpty(response)) return null;
        int eol = response.IndexOf('\n');
        string firstLine = eol >= 0 ? response[..eol] : response;
        var m = Regex.Match(firstLine, @"^RTSP/\d\.\d\s+(\d{3})");
        return m.Success ? m.Groups[1].Value : null;
    }

    /// <summary>从 401 应答中解析 Digest 质询（取不到 realm/nonce 返回 null）。</summary>
    internal static DigestChallenge? ParseDigestChallenge(string response)
    {
        var m = Regex.Match(response, "WWW-Authenticate:\\s*Digest\\s+(.+)", RegexOptions.IgnoreCase);
        if (!m.Success) return null;

        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match kv in Regex.Matches(m.Groups[1].Value, @"(\w+)\s*=\s*""([^""]*)"""))
            fields[kv.Groups[1].Value] = kv.Groups[2].Value;
        if (!fields.TryGetValue("realm", out var realm) || !fields.TryGetValue("nonce", out var nonce))
            return null;
        fields.TryGetValue("qop", out var qop);
        return new DigestChallenge(realm, nonce, string.IsNullOrWhiteSpace(qop) ? null : qop);
    }

    /// <summary>按 RFC 2617 计算 Digest 认证头（兼容 qop=auth 与无 qop 两种质询）。</summary>
    internal static string BuildDigestAuthorization(string user, string pass, string method, string uri, DigestChallenge c)
    {
        string ha1 = Md5Hex($"{user}:{c.Realm}:{pass}");
        string ha2 = Md5Hex($"{method}:{uri}");
        if (string.IsNullOrEmpty(c.Qop))
        {
            string response = Md5Hex($"{ha1}:{c.Nonce}:{ha2}");
            return $"Digest username=\"{user}\", realm=\"{c.Realm}\", nonce=\"{c.Nonce}\", uri=\"{uri}\", response=\"{response}\"";
        }

        const string cnonce = "0a4f113b";
        string resp = Md5Hex($"{ha1}:{c.Nonce}:00000001:{cnonce}:{c.Qop}:{ha2}");
        return $"Digest username=\"{user}\", realm=\"{c.Realm}\", nonce=\"{c.Nonce}\", uri=\"{uri}\", " +
               $"response=\"{resp}\", qop={c.Qop}, nc=00000001, cnonce=\"{cnonce}\"";
    }

    /// <summary>MD5 十六进制小写（Digest 认证摘要）</summary>
    internal static string Md5Hex(string text) =>
        Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
}

/// <summary>Digest 质询参数（realm/nonce 必需，qop 可选）</summary>
public record DigestChallenge(string Realm, string Nonce, string? Qop);