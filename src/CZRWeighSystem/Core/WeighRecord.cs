namespace CZRWeighSystem.Core;

/// <summary>
/// 称重记录模型（对应 PRD 第 8 章 t_weigh_record）。
/// 重量字段统一以千克存储，显示时由 AppConfig.FormatWeight 换算。
/// </summary>
public class WeighRecord
{
    public long Id { get; set; }
    /// <summary>流水号：yyyyMMdd-磅号-序号（对应 PRD F3-02）</summary>
    public string SerialNo { get; set; } = "";
    public string ScaleNo { get; set; } = "01";
    public string VehicleNo { get; set; } = "";
    public string? Goods { get; set; }
    /// <summary>发货单位（原"单位"拆分）</summary>
    public string? SenderCompany { get; set; }
    /// <summary>收货单位（原"单位"拆分）</summary>
    public string? ReceiverCompany { get; set; }
    public string? Spec { get; set; }
    public string? BatchNo { get; set; }
    public string? Remark { get; set; }

    public double? GrossKg { get; set; }    // 毛重
    public double? TareKg { get; set; }     // 皮重
    public double? NetKg { get; set; }      // 净重
    /// <summary>扣重（千克）：从净重中扣除的杂重，实收 = 净重 - 扣重</summary>
    public double? DeductKg { get; set; }

    /// <summary>应收金额（元）：二次磅结算时按收费分段自动计算；null = 未计费（未启用收费或未结算）</summary>
    public decimal? AmountYuan { get; set; }

    public DateTime? FirstTime { get; set; }
    public DateTime? SecondTime { get; set; }
    public string? Operator { get; set; }

    /// <summary>状态：未完成 / 已完成 / 已作废</summary>
    public string Status { get; set; } = "未完成";
    /// <summary>是否手工补录</summary>
    public bool IsManual { get; set; }

    /// <summary>磅单已打印次数（综合设置开启"打印次数"累计后每次打印 +1）</summary>
    public int PrintCount { get; set; }

    /// <summary>自定义字段值（JSON 对象，键=字段名；对应 t_weigh_record.ext_json）</summary>
    public string? ExtJson { get; set; }

    /// <summary>一次磅保存时的重量（未配对阶段即当前称量值）</summary>
    public double? FirstWeightKg => FirstTime.HasValue ? GrossKg : null;

    /// <summary>实收重量 = 净重 - 扣重（无扣重时即净重）</summary>
    public double? ReceivedKg => NetKg.HasValue ? NetKg - (DeductKg ?? 0) : null;

    /// <summary>解析自定义字段值（JSON 无效时返回空字典）。</summary>
    public Dictionary<string, string> ParseExt()
    {
        if (string.IsNullOrWhiteSpace(ExtJson)) return [];
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(ExtJson)
                   ?? [];
        }
        catch { return []; }
    }

    /// <summary>自定义字段字典序列化（空字典返回 null，不占存储）。</summary>
    public static string? ExtToJson(Dictionary<string, string>? ext)
        => ext is { Count: > 0 } ? System.Text.Json.JsonSerializer.Serialize(ext) : null;
}
