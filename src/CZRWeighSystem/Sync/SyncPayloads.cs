using System.Text.Json;
using System.Text.Json.Serialization;
using CZRWeighSystem.Core;

namespace CZRWeighSystem.Sync;

/// <summary>称重记录推送载荷（字段名与平台契约一致；净重由服务端计算不传，对接文档 §4.3）。</summary>
public sealed record RecordPayload(
    [property: JsonPropertyName("clientRef")] string ClientRef,
    [property: JsonPropertyName("weighTime")] string WeighTime,
    [property: JsonPropertyName("plateNo")] string? PlateNo,
    [property: JsonPropertyName("grossWeight")] double GrossWeight,
    [property: JsonPropertyName("tareWeight")] double TareWeight,
    [property: JsonPropertyName("landownerName")] string? LandownerName,
    [property: JsonPropertyName("landownerPhone")] string LandownerPhone,
    [property: JsonPropertyName("driverPhone")] string? DriverPhone,
    [property: JsonPropertyName("cargoOwnerName")] string? CargoOwnerName,
    [property: JsonPropertyName("variety")] string? Variety,
    [property: JsonPropertyName("weigher")] string? Weigher,
    [property: JsonPropertyName("photos")] List<string> Photos);

/// <summary>黑匣子推送载荷（photoId 无照片时为 null，序列化时省略，对接文档 §4.4）。</summary>
public sealed record BlackboxPayload(
    [property: JsonPropertyName("clientRef")] string ClientRef,
    [property: JsonPropertyName("weight")] double Weight,
    [property: JsonPropertyName("weighedAt")] string WeighedAt,
    [property: JsonPropertyName("photoId")] string? PhotoId);

/// <summary>作废推送载荷（对接文档 §4.5）。</summary>
public sealed record VoidPayload(
    [property: JsonPropertyName("clientRef")] string ClientRef,
    [property: JsonPropertyName("reason")] string Reason);

/// <summary>队列内照片项元数据（文件路径 + 抓拍时间）。</summary>
public sealed record PhotoPayload(string File, DateTime? ShotAt);

/// <summary>
/// 推送载荷构建：本地记录 → 平台接口载荷（纯函数，便于单测）。
/// 照片引用号 = 相对 exe 目录的路径去扩展名（正斜杠，≤64 字符、确定性唯一），
/// 上传照片返回的 photoId 按"引用号 → photoId"缓存在队列 result 列。
/// </summary>
public static class SyncPayloads
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>称重记录 → 推送载荷（地主/司机/货主存于 ext_json 固定键；毛/皮重结算时已按大者为毛）。</summary>
    public static RecordPayload FromRecord(WeighRecord r, List<string> photoRefs)
    {
        var ext = r.ParseExt();
        return new RecordPayload(
            ClientRef: r.SerialNo,
            WeighTime: (r.SecondTime ?? r.FirstTime ?? DateTime.Now).ToString("yyyy-MM-dd HH:mm:ss"),
            PlateNo: NullIfEmpty(r.VehicleNo),
            GrossWeight: Math.Round(r.GrossKg ?? 0, 2),
            TareWeight: Math.Round(r.TareKg ?? 0, 2),
            LandownerName: NullIfEmpty(ext.GetValueOrDefault("地主姓名", "")),
            LandownerPhone: ext.GetValueOrDefault("地主电话", "").Trim(),
            DriverPhone: NullIfEmpty(ext.GetValueOrDefault("司机电话", "")),
            CargoOwnerName: NullIfEmpty(ext.GetValueOrDefault("货主姓名", "")),
            Variety: NullIfEmpty(r.Goods ?? ""),
            Weigher: NullIfEmpty(r.Operator ?? ""),
            Photos: photoRefs);
    }

    /// <summary>黑匣子记录 → 推送载荷（多路抓拍只上第一张的 photoId；无照片为 null）。</summary>
    public static BlackboxPayload FromBlackbox(BlackBoxRecord r, string? photoRef) =>
        new(ClientRef: $"BX{r.Id}",
            Weight: Math.Round(r.WeightKg, 2),
            WeighedAt: r.Time.ToString("yyyy-MM-dd HH:mm:ss"),
            PhotoId: photoRef);

    /// <summary>照片文件 → 引用号（相对 exe 目录、去扩展名、正斜杠；如 captures/20260930/20260930-01-0001_cam1）。</summary>
    public static string PhotoRefOf(string filePath) =>
        Path.ChangeExtension(Path.GetRelativePath(AppContext.BaseDirectory, filePath), null)
            .Replace('\\', '/');

    /// <summary>
    /// 照片文件 → 拍摄时间：按文件名中的 tare/gross 标记取各自称重时刻，
    /// 不能统一用二次磅时间——否则先皮后毛时皮重照片会被标成毛重时间、先毛后皮则相反。
    /// 旧版无标记文件（{流水号}_camN.jpg）退回二次磅时间（该记录最终称重时刻，只能保守取值）。
    /// </summary>
    public static DateTime? ShotAtOf(string filePath, WeighRecord r)
    {
        string name = Path.GetFileName(filePath);
        if (name.Contains("_tare_", StringComparison.OrdinalIgnoreCase))
            return r.TareTime ?? r.SecondTime;
        if (name.Contains("_gross_", StringComparison.OrdinalIgnoreCase))
            return r.GrossTime ?? r.SecondTime;
        return r.SecondTime;
    }

    private static string? NullIfEmpty(string s) => s.Trim().Length == 0 ? null : s.Trim();
}
