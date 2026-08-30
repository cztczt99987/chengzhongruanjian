namespace CZRWeighSystem.Core;

/// <summary>
/// 报表汇总行（对应 PRD 5.4 报表统计）。
/// Key 为分组维度值（日期/货物/单位/车号/司磅员），重量单位千克。
/// </summary>
public class ReportRow
{
    /// <summary>分组键（日期、货物名、单位名、车号或司磅员）</summary>
    public string Key { get; set; } = "";
    /// <summary>车数（已完成记录条数）</summary>
    public int Count { get; set; }
    /// <summary>毛重合计（千克）</summary>
    public double GrossKg { get; set; }
    /// <summary>皮重合计（千克）</summary>
    public double TareKg { get; set; }
    /// <summary>净重合计（千克）</summary>
    public double NetKg { get; set; }
}
