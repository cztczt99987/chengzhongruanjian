namespace CZRWeighSystem.Comm;

/// <summary>
/// 模拟重量数据源（对应 PRD F6-05 重量数据模拟器）。
/// 无真实仪表时用于开发调试：模拟"车辆上磅 → 重量上升 → 稳定 → 离磅归零"全过程。
/// 使用 WinForms Timer，事件在 UI 线程触发，无需跨线程处理。
/// </summary>
public class SimulatedWeightSource : IWeightSource
{
    public event Action<WeightData>? WeightReceived;
    public event Action<string>? ErrorOccurred;

    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 200 };
    private readonly Random _random = new();

    // 模拟状态机：Idle 空磅 / Rising 重量上升 / Holding 稳定称量 / Falling 离磅
    private enum SimState { Idle, Rising, Holding, Falling }
    private SimState _state = SimState.Idle;
    private double _currentKg;      // 当前重量
    private double _targetKg;       // 本次过磅目标重量
    private int _holdTicks;         // 稳定保持计时

    public void Start()
    {
        _timer.Tick += OnTick;
        _timer.Start();
    }

    public void Stop() => _timer.Stop();

    public string Describe() => "模拟数据源（开发调试用）";

    private void OnTick(object? sender, EventArgs e)
    {
        switch (_state)
        {
            case SimState.Idle:
                // 空磅随机时长后"驶入"一辆车，目标重量 20~60 吨
                if (_random.Next(0, 100) < 6)
                {
                    _targetKg = _random.Next(20000, 60000);
                    _state = SimState.Rising;
                }
                break;

            case SimState.Rising:
                // 每次采样上升 200~1500 千克，模拟车辆缓慢上磅
                _currentKg += _random.Next(200, 1500);
                if (_currentKg >= _targetKg)
                {
                    _currentKg = _targetKg;
                    _state = SimState.Holding;
                    _holdTicks = 0;
                }
                break;

            case SimState.Holding:
                // 稳定期：重量在 ±3 千克内轻微抖动，保持约 8 秒后离磅
                _currentKg = _targetKg + _random.Next(-3, 3);
                _holdTicks++;
                if (_holdTicks > 40) _state = SimState.Falling;
                break;

            case SimState.Falling:
                // 离磅：快速回落到 0
                _currentKg -= _random.Next(2000, 4000);
                if (_currentKg <= 0)
                {
                    _currentKg = 0;
                    _state = SimState.Idle;
                }
                break;
        }

        WeightReceived?.Invoke(new WeightData(_currentKg, DateTime.Now));
    }

    public void Dispose()
    {
        _timer.Stop();
        _timer.Dispose();
    }
}
