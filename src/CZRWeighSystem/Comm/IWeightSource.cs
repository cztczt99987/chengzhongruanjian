namespace CZRWeighSystem.Comm;

/// <summary>
/// 一次重量采样数据（内部统一使用千克）。
/// </summary>
/// <param name="ValueKg">重量值（千克）</param>
/// <param name="Time">采样时间</param>
public record WeightData(double ValueKg, DateTime Time);

/// <summary>
/// 称重数据源统一接口（对应 PRD 5.6 仪表数据采集）。
/// 串口仪表与模拟器都实现该接口，业务层不关心数据来源，
/// 后续扩展 TCP 透传采集（F6-04）时也实现本接口即可。
/// </summary>
public interface IWeightSource : IDisposable
{
    /// <summary>收到一次重量数据（注意：可能来自非 UI 线程）</summary>
    event Action<WeightData>? WeightReceived;

    /// <summary>数据源发生错误（断线、打开失败等）</summary>
    event Action<string>? ErrorOccurred;

    /// <summary>启动数据源</summary>
    void Start();

    /// <summary>停止数据源</summary>
    void Stop();

    /// <summary>数据源描述（用于状态栏显示）</summary>
    string Describe();
}
