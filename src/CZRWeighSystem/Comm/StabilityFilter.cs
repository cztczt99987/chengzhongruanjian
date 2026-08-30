namespace CZRWeighSystem.Comm;

/// <summary>
/// 重量稳定判定器（对应 PRD F2-02）。
/// 连续 N 次（可配置）采样差值小于容差时判定为"稳定"。
/// </summary>
public class StabilityFilter
{
    private readonly int _needCount;
    private readonly double _toleranceKg;
    private double _lastKg = double.NaN;
    private int _sameCount;

    /// <summary>当前是否处于稳定状态</summary>
    public bool IsStable { get; private set; }

    /// <summary>判定为稳定时的重量值（千克）</summary>
    public double StableKg { get; private set; }

    public StabilityFilter(int needCount, double toleranceKg)
    {
        _needCount = Math.Max(1, needCount);
        _toleranceKg = toleranceKg;
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
        }
        else if (wasStable)
        {
            // 由稳定转为动态时清空稳定值
            StableKg = 0;
        }
    }

    /// <summary>重置状态（如磅上重量归零后调用）。</summary>
    public void Reset()
    {
        _lastKg = double.NaN;
        _sameCount = 0;
        IsStable = false;
        StableKg = 0;
    }
}
