using CZRWeighSystem.Comm;

namespace CZRWeighSystem.Tests;

/// <summary>
/// 稳定判定器"数据稳定 X 秒"测试（综合设置-基本设置：数据稳定 X 秒绿灯亮方可保存）。
/// 时间源注入，不依赖真实等待。
/// </summary>
public class StabilityFilterStableSecondsTests
{
    [Fact]
    public void RequiredSeconds_GatesSaveReady()
    {
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var filter = new StabilityFilter(3, 5, requiredSeconds: 10, utcNow: () => now);

        // 3 次一致采样 → 稳定，但持续时长为 0，尚不可保存
        filter.Push(100);
        filter.Push(100);
        filter.Push(100);
        Assert.True(filter.IsStable);
        Assert.False(filter.IsStableForSave);

        // 推进 9.9 秒：仍不足
        now = now.AddSeconds(9.9);
        Assert.False(filter.IsStableForSave);

        // 推进到 10 秒：达到要求（无需新采样）
        now = now.AddSeconds(0.1);
        Assert.True(filter.IsStableForSave);

        // 数据跳动 → 退出稳定并清零计时，恢复稳定后需重新计时
        filter.Push(500);
        Assert.False(filter.IsStable);
        Assert.False(filter.IsStableForSave);
        filter.Push(500);
        filter.Push(500);
        Assert.True(filter.IsStable);
        Assert.False(filter.IsStableForSave);   // 刚重新进入稳定，时长清零
        now = now.AddSeconds(10);
        Assert.True(filter.IsStableForSave);
    }

    [Fact]
    public void RequiredSeconds_Zero_MatchesStableDirectly()
    {
        var filter = new StabilityFilter(2, 5);
        filter.Push(80);
        Assert.False(filter.IsStableForSave);
        filter.Push(80);
        Assert.True(filter.IsStable);
        Assert.True(filter.IsStableForSave);   // 未启用时长要求时与 IsStable 一致
    }
}
