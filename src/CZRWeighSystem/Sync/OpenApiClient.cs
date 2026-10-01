using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CZRWeighSystem.Sync;

/// <summary>鉴权失败（401/code=1000）：停止重试，检查密钥与本机系统时间（对接文档 §6）。</summary>
public class PlatformAuthException : Exception
{
    public PlatformAuthException(string message) : base(message) { }
}

/// <summary>业务失败（code=5000 或单条 results.failed）：需要修正数据后再推。</summary>
public class PlatformBusinessException : Exception
{
    public PlatformBusinessException(string message) : base(message) { }
}

/// <summary>限流（429）：短延迟后重试即可。</summary>
public class PlatformRateLimitException : Exception
{
    public PlatformRateLimitException() : base("请求过于频繁（限流）") { }
}

/// <summary>
/// HMAC-SHA256 签名工具（对接文档 §3.2）。
/// JSON 接口 bodyHash = SHA256(原始请求体)；multipart 以元数据串代替请求体哈希；
/// signStr = appKey\n timestamp\n nonce\n bodyHash，签名 = HMAC-SHA256(appSecret, signStr) 小写十六进制。
/// </summary>
public static class ApiSigner
{
    /// <summary>字节流 SHA-256（小写十六进制）。</summary>
    public static string Sha256Hex(byte[] data) =>
        Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    /// <summary>字符串 SHA-256（UTF-8，小写十六进制）。</summary>
    public static string Sha256Hex(string text) => Sha256Hex(Encoding.UTF8.GetBytes(text));

    /// <summary>毫秒时间戳（与服务器偏差 ±300 秒内有效，异常时按"检查系统时间"提示）。</summary>
    public static long NowTimestamp() => DateTimeOffset.Now.ToUnixTimeMilliseconds();

    /// <summary>随机串：16 位十六进制（满足 ≥16 位、5 分钟内不重复要求）。</summary>
    public static string NewNonce() =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant();

    /// <summary>待签名串：appKey + 换行 + 时间戳 + 换行 + nonce + 换行 + bodyHash。</summary>
    public static string BuildSignStr(string appKey, string timestamp, string nonce, string bodyHash) =>
        $"{appKey}\n{timestamp}\n{nonce}\n{bodyHash}";

    /// <summary>HMAC-SHA256（小写十六进制）。</summary>
    public static string HmacSha256Hex(string secret, string data) =>
        Convert.ToHexString(new HMACSHA256(Encoding.UTF8.GetBytes(secret))
            .ComputeHash(Encoding.UTF8.GetBytes(data))).ToLowerInvariant();

    /// <summary>multipart 签名元数据串：SHA256(appKey + timestamp + fileSize + fileSha256)。</summary>
    public static string MultipartBodyHash(string appKey, string timestamp, long fileSize, string fileSha256) =>
        Sha256Hex(appKey + timestamp + fileSize + fileSha256);
}

// ==================== 响应 DTO（字段名与平台契约一致） ====================

public sealed record PingData(
    [property: JsonPropertyName("stationCode")] string StationCode,
    [property: JsonPropertyName("stationName")] string StationName);

/// <summary>
/// 宽松字符串转换：服务端偶有把字符串字段返回为数字的情况（如 photoId:3），
/// 数字/字符串 token 都接受并统一为字符串，避免反序列化失败。
/// </summary>
public class FlexibleStringConverter : JsonConverter<string>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType switch
        {
            JsonTokenType.String => reader.GetString(),
            JsonTokenType.Number => reader.TryGetInt64(out var l) ? l.ToString() : reader.GetDouble().ToString(),
            JsonTokenType.Null => null,
            _ => throw new JsonException($"无法将 {reader.TokenType} 读取为字符串"),
        };

    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
    {
        if (value is null) writer.WriteNullValue();
        else writer.WriteStringValue(value);
    }
}

public sealed record PhotoData(
    [property: JsonPropertyName("photoId")]
    [property: JsonConverter(typeof(FlexibleStringConverter))] string PhotoId,
    [property: JsonPropertyName("url")] string? Url);

/// <summary>批量推送逐条结果（记录/黑匣子/作废共用：created/duplicate/failed/voided…）。</summary>
public sealed record BatchResult(
    [property: JsonPropertyName("clientRef")] string ClientRef,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("recordNo")]
    [property: JsonConverter(typeof(FlexibleStringConverter))] string? RecordNo,
    [property: JsonPropertyName("reason")] string? Reason);

public sealed record BatchData(
    [property: JsonPropertyName("accepted")] int Accepted,
    [property: JsonPropertyName("results")] List<BatchResult>? Results);

public sealed class OpenApiClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly string _appKey;
    private readonly string _appSecret;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public OpenApiClient(string baseUrl, string appKey, string appSecret)
    {
        _appKey = appKey;
        _appSecret = appSecret;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        _http.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
    }

    /// <summary>连通性自检：验证密钥、时钟偏差与网络（称重软件"测试连接"按钮）。</summary>
    public async Task<PingData> PingAsync(CancellationToken ct = default)
    {
        var data = await SendAsync(BuildSignedRequest(HttpMethod.Get, "api/open/weighing/ping",
            ApiSigner.Sha256Hex(string.Empty)), ct);
        return JsonSerializer.Deserialize<PingData>(data.GetRawText(), JsonOpts)
               ?? throw new PlatformBusinessException("ping 响应为空");
    }

    /// <summary>
    /// 上传抓拍照片（multipart）。签名 bodyHash = SHA256(appKey+timestamp+fileSize+fileSha256)，
    /// fileSha256 由客户端对文件计算后随表单上送（对接文档 §3.2/§4.2）。
    /// </summary>
    public async Task<PhotoData> UploadPhotoAsync(string filePath, string clientRef,
        DateTime? shotAt, CancellationToken ct = default)
    {
        if (!File.Exists(filePath))
            throw new PlatformBusinessException("照片文件不存在：" + filePath);

        long fileSize = new FileInfo(filePath).Length;
        string fileSha256 = ApiSigner.Sha256Hex(await File.ReadAllBytesAsync(filePath, ct));

        // multipart 无法对请求体整体哈希，按文档以元数据串参与签名
        string timestamp = ApiSigner.NowTimestamp().ToString();
        string nonce = ApiSigner.NewNonce();
        string bodyHash = ApiSigner.MultipartBodyHash(_appKey, timestamp, fileSize, fileSha256);
        string signature = ApiSigner.HmacSha256Hex(_appSecret,
            ApiSigner.BuildSignStr(_appKey, timestamp, nonce, bodyHash));

        using var content = new MultipartFormDataContent();
        var fileContent = new StreamContent(File.OpenRead(filePath));   // 随 content 释放
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        content.Add(fileContent, "file", Path.GetFileName(filePath));
        content.Add(new StringContent(fileSha256), "fileSha256");
        content.Add(new StringContent(clientRef), "clientRef");
        if (shotAt.HasValue)
            content.Add(new StringContent(shotAt.Value.ToString("yyyy-MM-dd HH:mm:ss")), "shotAt");

        using var req = new HttpRequestMessage(HttpMethod.Post, "api/open/weighing/photos")
        {
            Content = content,
        };
        AddHeaders(req, timestamp, nonce, signature);
        var data = await SendAsync(req, ct);
        return JsonSerializer.Deserialize<PhotoData>(data.GetRawText(), JsonOpts)
               ?? throw new PlatformBusinessException("照片上传响应为空");
    }

    /// <summary>批量推送称重记录（1~50 条/次，逐条结果见 results）。</summary>
    public async Task<BatchData> PushRecordsAsync(
        IReadOnlyList<RecordPayload> records, CancellationToken ct = default)
    {
        var data = await PostJsonAsync("api/open/weighing/records", new { records }, ct);
        return JsonSerializer.Deserialize<BatchData>(data.GetRawText(), JsonOpts)
               ?? throw new PlatformBusinessException("记录推送响应为空");
    }

    /// <summary>批量推送黑匣子记录（1~200 条/次；photoId 可空——无照片的记录照常入库）。</summary>
    public async Task<BatchData> PushBlackboxAsync(
        IReadOnlyList<BlackboxPayload> records, CancellationToken ct = default)
    {
        var data = await PostJsonAsync("api/open/weighing/blackbox", new { records }, ct);
        return JsonSerializer.Deserialize<BatchData>(data.GetRawText(), JsonOpts)
               ?? throw new PlatformBusinessException("黑匣子推送响应为空");
    }

    /// <summary>作废称重记录，返回平台侧状态（voided / not_found / already_voided）。</summary>
    public async Task<BatchResult> VoidAsync(string clientRef, string reason,
        CancellationToken ct = default)
    {
        var data = await PostJsonAsync("api/open/weighing/void",
            new { clientRef, reason }, ct);
        return JsonSerializer.Deserialize<BatchResult>(data.GetRawText(), JsonOpts)
               ?? throw new PlatformBusinessException("作废响应为空");
    }

    public void Dispose() => _http.Dispose();

    // ---------- 内部 ----------

    private async Task<JsonElement> PostJsonAsync(string path, object payload, CancellationToken ct)
    {
        string body = JsonSerializer.Serialize(payload, JsonOpts);
        return await SendAsync(BuildSignedRequest(HttpMethod.Post, path,
            ApiSigner.Sha256Hex(body), body), ct);
    }

    /// <summary>构建带签名头的请求（JSON 接口：bodyHash = SHA256(请求体)，GET 传空串哈希）。</summary>
    private HttpRequestMessage BuildSignedRequest(HttpMethod method, string path,
        string bodyHash, string? body = null)
    {
        string timestamp = ApiSigner.NowTimestamp().ToString();
        string nonce = ApiSigner.NewNonce();
        string signature = ApiSigner.HmacSha256Hex(_appSecret,
            ApiSigner.BuildSignStr(_appKey, timestamp, nonce, bodyHash));

        var req = new HttpRequestMessage(method, path);
        if (body != null)
            req.Content = new StringContent(body, Encoding.UTF8, "application/json");
        AddHeaders(req, timestamp, nonce, signature);
        return req;
    }

    private void AddHeaders(HttpRequestMessage req, string timestamp, string nonce, string signature)
    {
        req.Headers.Add("X-Api-Key", _appKey);
        req.Headers.Add("X-Timestamp", timestamp);
        req.Headers.Add("X-Nonce", nonce);
        req.Headers.Add("X-Signature", signature);
    }

    /// <summary>
    /// 统一发送与响应包裹解析：HTTP 401/429 直接映射异常；
    /// code=0000 成功返回 data，1000 鉴权失败，其余业务失败（对接文档 §4）。
    /// </summary>
    private async Task<JsonElement> SendAsync(HttpRequestMessage req, CancellationToken ct)
    {
        using var resp = await _http.SendAsync(req, ct);
        if (resp.StatusCode == HttpStatusCode.Unauthorized)
            throw new PlatformAuthException("鉴权失败（401）");
        if (resp.StatusCode == HttpStatusCode.TooManyRequests)
            throw new PlatformRateLimitException();
        resp.EnsureSuccessStatusCode();   // 其余 HTTP 错误按可重试处理（网络/5xx）

        var json = await resp.Content.ReadAsStringAsync(ct);
        var env = JsonSerializer.Deserialize<Envelope>(json, JsonOpts);
        if (env?.Code == null)
            throw new PlatformBusinessException("响应格式错误：" + json);

        return env.Code switch
        {
            "0000" when env.Data.ValueKind is JsonValueKind.Object or JsonValueKind.Array => env.Data,
            "0000" => throw new PlatformBusinessException("成功响应缺少 data"),
            "1000" => throw new PlatformAuthException(
                string.IsNullOrWhiteSpace(env.Msg) ? "鉴权失败" : env.Msg),
            _ => throw new PlatformBusinessException(
                string.IsNullOrWhiteSpace(env.Msg) ? "业务失败" : env.Msg),
        };
    }

    private sealed record Envelope(
        [property: JsonPropertyName("code")] string? Code,
        [property: JsonPropertyName("msg")] string? Msg,
        [property: JsonPropertyName("data")] JsonElement Data);
}
