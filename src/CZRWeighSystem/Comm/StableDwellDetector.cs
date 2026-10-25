namespace CZRWeighSystem.Comm;

/// <summary>
/// 黑匣子稳定驻留判定器（黑匣子功能核心逻辑）。
/// 重量处于稳定状态且不小于触发重量，持续达到设定秒数 → 触发一次记录。
/// 过程边界以"车辆是否在磅上"为准：重量回落到触发重量以下才算一次过磅结束；
/// 车辆在磅上期间即使仪表短暂报动态或重量变化，也只记录一次——
/// 否则磅上车辆轻微晃动就会被反复记录，产生大量无用数据。
/// 纯逻辑无副作用，时间由调用方传入以便测试。
/// </summary>
public class StableDwellDetector
{
    private readonly double _minKg;
    private readonly TimeSpan _dwell;

    private DateTime? _stableSince;   // 本次稳定计时起点
    private bool _fired;              // 本次压磅过程是否已触发

    public StableDwellDetector(double minKg, int seconds)
    {
        _minKg = minKg;
        _dwell = TimeSpan.FromSeconds(Math.Max(1, seconds));
    }

    /// <summary>
    /// 输入一次采样，返回本次是否应触发记录（一次压磅过程最多返回一次 true）。
    /// </summary>
    /// <param name="kg">当前稳定重量（千克）</param>
    /// <param name="isStable">当前是否稳定（来自 StabilityFilter）</param>
    /// <param name="now">当前时间（调用方传入，测试可注入固定时钟）</param>
    public bool ShouldFire(double kg, bool isStable, DateTime now)
    {
        if (kg < _minKg)
        {
            // 重量回落到触发重量以下 = 车辆已下磅 → 本次过程结束，下次上磅重新计时
            _stableSince = null;
            _fired = false;
            return false;
        }

        if (!isStable)
        {
            // 动态期间重新计时，但不清除 _fired：车辆仍在磅上，不算新的一次过磅
            _stableSince = null;
            return false;
        }

        _stableSince ??= now;
        if (!_fired && now - _stableSince.Value >= _dwell)
        {
            _fired = true;
            return true;
        }
        return false;
    }
}
