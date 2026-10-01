namespace CZRWeighSystem.Comm;

/// <summary>
/// 黑匣子稳定驻留判定器（黑匣子功能核心逻辑）。
/// 重量处于稳定状态且不小于触发重量，持续达到设定秒数 → 触发一次记录；
/// 同一稳定过程只触发一次，进入动态（或重量低于阈值）后重新计时。
/// 纯逻辑无副作用，时间由调用方传入以便测试。
/// </summary>
public class StableDwellDetector
{
    private readonly double _minKg;
    private readonly TimeSpan _dwell;

    private DateTime? _stableSince;   // 本次稳定过程的开始时间
    private bool _fired;              // 本次稳定过程是否已触发

    public StableDwellDetector(double minKg, int seconds)
    {
        _minKg = minKg;
        _dwell = TimeSpan.FromSeconds(Math.Max(1, seconds));
    }

    /// <summary>
    /// 输入一次采样，返回本次是否应触发记录（每次稳定过程最多返回一次 true）。
    /// </summary>
    /// <param name="kg">当前稳定重量（千克）</param>
    /// <param name="isStable">当前是否稳定（来自 StabilityFilter）</param>
    /// <param name="now">当前时间（调用方传入，测试可注入固定时钟）</param>
    public bool ShouldFire(double kg, bool isStable, DateTime now)
    {
        if (!isStable || kg < _minKg)
        {
            // 进入动态或重量不够 → 结束当前稳定过程（下次稳定重新计时）
            _stableSince = null;
            _fired = false;
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
