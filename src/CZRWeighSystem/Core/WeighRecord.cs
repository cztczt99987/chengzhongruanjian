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
    public string? Company { get; set; }
    public string? Spec { get; set; }
    public string? BatchNo { get; set; }
    public string? Remark { get; set; }

    public double? GrossKg { get; set; }    // 毛重
    public double? TareKg { get; set; }     // 皮重
    public double? NetKg { get; set; }      // 净重

    public DateTime? FirstTime { get; set; }
    public DateTime? SecondTime { get; set; }
    public string? Operator { get; set; }

    /// <summary>状态：未完成 / 已完成 / 已作废</summary>
    public string Status { get; set; } = "未完成";
    /// <summary>是否手工补录</summary>
    public bool IsManual { get; set; }

    /// <summary>一次磅保存时的重量（未配对阶段即当前称量值）</summary>
    public double? FirstWeightKg => FirstTime.HasValue ? GrossKg : null;
}
