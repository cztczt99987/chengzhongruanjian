namespace CZRWeighSystem.Comm;

/// <summary>
/// 重量稳定判定器（对应 PRD F2-02）。
/// 连续 N 次（可配置）采样差值小于容差时判定为"稳定"；
/// 可选要求稳定持续 RequiredSeconds 秒后方可保存（综合设置-基本设置：数据稳定 X 秒绿灯亮）。
/// </summary>
public class StabilityFilter
{
    private readonly int _needCount;
    private readonly double _toleranceKg;
    private readonly int _requiredSeconds;
    private readonly Func<DateTime> _utcNow;   // 时间源（测试注入；默认系统 UTC 时间）
    private double _lastKg = double.NaN;
    private int _sameCount;
    private DateTime? _stableSinceUtc;   // 进入稳定状态的时刻（UTC），动态期间为 null

    /// <summary>当前是否处于稳定状态</summary>
    public bool IsStable { get; private set; }

    /// <summary>判定为稳定时的重量值（千克）</summary>
    public double StableKg { get; private set; }

    /// <summary>
    /// 是否达到"可保存"的稳定要求：稳定且（未启用时长要求 或 稳定已持续 RequiredSeconds 秒）。
    /// </summary>
    public bool IsStableForSave => IsStable && (_requiredSeconds <= 0
        || (_stableSinceUtc.HasValue
            && (_utcNow() - _stableSinceUtc.Value).TotalSeconds >= _requiredSeconds));

    public StabilityFilter(int needCount, double toleranceKg, int requiredSeconds = 0,
        Func<DateTime>? utcNow = null)
    {
        _needCount = Math.Max(1, needCount);
        _toleranceKg = toleranceKg;
        _requiredSeconds = Math.Max(0, requiredSeconds);
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
    }

    /// <summary>
    /// 输入一次采样，更新稳定状态。
    /// </summary>
    public void Push(double kg)
    {
        if (double.IsNaN(_lastKg) || Math.Abs(kg - _lastKg) <= _toleranceKg)
            _sameCount++;
        else
            _sameCount = 1;

        _lastKg = kg;
        bool wasStable = IsStable;
        IsStable = _sameCount >= _needCount;

        if (IsStable)
        {
            StableKg = kg;
            if (!wasStable) _stableSinceUtc = _utcNow();   // 刚进入稳定 → 开始计时
        }
        else
        {
            _stableSinceUtc = null;
            if (wasStable)
            {
                // 由稳定转为动态时清空稳定值
                StableKg = 0;
            }
        }
    }
}
