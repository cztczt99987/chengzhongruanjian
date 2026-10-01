using CZRWeighSystem.Core;

namespace CZRWeighSystem.Tests;

/// <summary>
/// 收费计算测试（按重量区分固定金额，仅此一种模式）。
/// 默认配置与参考软件一致：0-3吨3元、3-5吨5元、5-10吨10元、10-22吨20元、
/// 22 吨以上按"其余 1 元/吨"计费；分段含起点、不含终点。
/// </summary>
public class FeeCalculatorTests
{
    private static AppConfig Cfg() => new();   // 默认配置即参考分段

    [Theory]
    [InlineData(2500, 3)]      // 2.5 吨 → 0-3 段固定 3 元
    [InlineData(3000, 5)]      // 3 吨整 → 含起点归 3-5 段固定 5 元
    [InlineData(4999, 5)]      // 4.999 吨 → 3-5 段
    [InlineData(5000, 10)]     // 5 吨整 → 5-10 段固定 10 元
    [InlineData(15000, 20)]    // 15 吨 → 10-22 段固定 20 元
    public void CalculateYuan_净重命中分段_取该段固定金额(double netKg, decimal expected)
    {
        Assert.Equal(expected, FeeCalculator.CalculateYuan(netKg, Cfg()));
    }

    [Theory]
    [InlineData(22000, 22)]      // 22 吨（默认段金额 0）→ 22 × 1 元/吨
    [InlineData(30500, 30.5)]    // 30.5 吨 → 30.5 元
    public void CalculateYuan_分段金额为0或未命中_按其余每吨标准(double netKg, decimal expected)
    {
        Assert.Equal(expected, FeeCalculator.CalculateYuan(netKg, Cfg()));
    }

    [Fact]
    public void CalculateYuan_未启用收费或无净重_不计费()
    {
        var disabled = Cfg();
        disabled.FeeEnabled = false;
        Assert.Null(FeeCalculator.CalculateYuan(5000, disabled));   // 关闭收费
        Assert.Null(FeeCalculator.CalculateYuan(null, Cfg()));      // 无净重（未结算）
    }

    [Fact]
    public void CalculateYuan_自定义其余单价_按吨数乘单价()
    {
        var cfg = Cfg();
        cfg.FeeFallbackPerTon = 2.5m;
        // 25 吨落在金额为 0 的尾段 → 25 × 2.5 = 62.5 元
        Assert.Equal(62.5m, FeeCalculator.CalculateYuan(25000, cfg));
    }
}
