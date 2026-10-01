namespace CZRWeighSystem.Core;

/// <summary>
/// 收费计算（按重量区分固定金额，仅此一种模式）：
/// 1) 净重落入某分段（含起点、不含终点）→ 取该段固定金额；
/// 2) 分段固定金额配置为 0 → 该段按"其余收费每吨"标准计费（吨数 × 每吨单价）；
/// 3) 未落入任何分段 → 同样按"其余收费每吨"标准；
/// 4) 未启用收费或无净重 → 不计费（返回 null）。
/// </summary>
public static class FeeCalculator
{
    /// <summary>
    /// 按净重（千克）计算应收金额（元）。
    /// 未启用收费、配置为关闭或无净重时返回 null（记录不写金额）。
    /// </summary>
    public static decimal? CalculateYuan(double? netKg, AppConfig cfg)
    {
        if (!cfg.FeeEnabled || netKg is not { } kg) return null;

        double tons = kg / 1000.0;
        foreach (var b in cfg.FeeBrackets)
        {
            if (tons >= b.FromTon && (b.ToTon <= 0 || tons < b.ToTon))
                return b.AmountYuan > 0 ? b.AmountYuan : Fallback(tons, cfg);
        }
        return Fallback(tons, cfg);
    }

    /// <summary>其余收费标准：吨数 × 每吨单价（四舍五入到分）。</summary>
    private static decimal Fallback(double tons, AppConfig cfg) =>
        Math.Round((decimal)tons * cfg.FeeFallbackPerTon, 2);
}
