using CZRWeighSystem.Comm;

namespace CZRWeighSystem.Tests;

/// <summary>
/// 黑匣子稳定驻留判定器测试。
/// 时间由调用方注入，纯逻辑验证：触发条件、单次触发、动态重置。
/// </summary>
public class StableDwellDetectorTests
{
    private static DateTime T(int seconds) => new DateTime(2026, 1, 1, 8, 0, 0).AddSeconds(seconds);

    /// <summary>构造判定器：触发重量 200kg，持续 10 秒。</summary>
    private static StableDwellDetector NewDetector() => new(minKg: 200, seconds: 10);

    [Fact]
    public void StableAboveMin_NotYetDwell_DoesNotFire()
    {
        var d = NewDetector();

        Assert.False(d.ShouldFire(5000, isStable: true, T(0)));
        Assert.False(d.ShouldFire(5000, isStable: true, T(5)));    // 未满 10 秒
        Assert.False(d.ShouldFire(5005, isStable: true, T(9)));    // 容差内微变仍在同一过程
    }

    [Fact]
    public void StableAboveMin_AfterDwell_FiresOnce()
    {
        var d = NewDetector();

        d.ShouldFire(5000, isStable: true, T(0));
        Assert.False(d.ShouldFire(5000, isStable: true, T(9)));    // 差 1 秒不触发
        Assert.True(d.ShouldFire(5000, isStable: true, T(10)));    // 满 10 秒 → 触发
        Assert.False(d.ShouldFire(5000, isStable: true, T(20)));   // 同一稳定过程不重复触发
        Assert.False(d.ShouldFire(5000, isStable: true, T(60)));
    }

    [Fact]
    public void BelowMinWeight_NeverFires()
    {
        var d = NewDetector();

        // 稳定但重量低于阈值（空磅/行人）→ 不触发、不计时
        for (int s = 0; s <= 30; s += 5)
            Assert.False(d.ShouldFire(150, isStable: true, T(s)));
    }

    [Fact]
    public void 车在磅上抖动_只记录一次()
    {
        var d = NewDetector();

        d.ShouldFire(5000, isStable: true, T(0));
        Assert.True(d.ShouldFire(5000, isStable: true, T(10)));    // 第一次触发

        // 车仍在磅上（重量未跌破阈值）：仪表短暂报动态后重新稳定，不再记录
        Assert.False(d.ShouldFire(3000, isStable: false, T(12)));
        Assert.False(d.ShouldFire(4800, isStable: true, T(20)));
        Assert.False(d.ShouldFire(4800, isStable: true, T(30)));
        Assert.False(d.ShouldFire(4800, isStable: true, T(120)));  // 长时间停留也不重复
    }

    [Fact]
    public void 重量跌破阈值_视为下磅_下一辆车再次记录()
    {
        var d = NewDetector();

        d.ShouldFire(5000, isStable: true, T(0));
        Assert.True(d.ShouldFire(5000, isStable: true, T(10)));

        // 车辆下磅：重量低于触发重量 → 本次过程结束
        Assert.False(d.ShouldFire(150, isStable: true, T(15)));

        // 第二辆车上磅并稳定满 10 秒 → 再次记录
        Assert.False(d.ShouldFire(4800, isStable: true, T(20)));
        Assert.True(d.ShouldFire(4800, isStable: true, T(30)));
    }

    [Fact]
    public void 未触发前抖动_重新计时()
    {
        var d = NewDetector();

        d.ShouldFire(5000, isStable: true, T(0));
        Assert.False(d.ShouldFire(5000, isStable: false, T(5)));   // 抖动 → 计时重置
        Assert.False(d.ShouldFire(5000, isStable: true, T(10)));   // 从 T(10) 重新起算
        Assert.True(d.ShouldFire(5000, isStable: true, T(20)));    // 满 10 秒 → 触发
    }

    [Fact]
    public void NewDetector_MinimumOneSecond()
    {
        // 秒数 < 1 按 1 秒处理（防误配 0 秒疯狂记录）
        var d = new StableDwellDetector(200, 0);

        Assert.False(d.ShouldFire(5000, isStable: true, T(0)));
        Assert.True(d.ShouldFire(5000, isStable: true, T(1)));
    }
}
