using System.IO.Ports;
using CZRWeighSystem.Comm;
using CZRWeighSystem.Database;

namespace CZRWeighSystem.Core;

/// <summary>
/// 称重业务管理器（对应 PRD 4.1 标准两次称重流程、5.3 记录管理）。
/// 职责：驱动数据源、执行稳定判定、完成一次磅/二次磅的保存与结算。
/// </summary>
public class WeighManager : IDisposable
{
    private readonly WeighRecordRepository _repo = new();
    private IWeightSource _source;
    private StabilityFilter _filter;

    /// <summary>实时重量（千克），由数据源驱动更新</summary>
    public double CurrentKg { get; private set; }

    /// <summary>当前是否稳定（对应 PRD F2-02）</summary>
    public bool IsStable => _filter.IsStable;

    /// <summary>稳定重量值（千克），未稳定时为 0</summary>
    public double StableKg => _filter.StableKg;

    /// <summary>是否超载（对应 PRD F2-03）</summary>
    public bool IsOverload => CurrentKg > AppConfig.Current.MaxCapacityKg;

    /// <summary>重量数据更新（注意：可能来自非 UI 线程，UI 层需 Invoke）</summary>
    public event Action? WeightUpdated;

    /// <summary>数据源发生错误</summary>
    public event Action<string>? SourceError;

    public WeighManager()
    {
        var cfg = AppConfig.Current;
        _filter = new StabilityFilter(cfg.StableCount, cfg.StableToleranceKg);
        _source = CreateSource(cfg.DataSourceMode);
        AttachSource(_source);
    }

    /// <summary>
    /// 启动数据源，开始接收重量数据。
    /// </summary>
    public void Start() => _source.Start();

    /// <summary>
    /// 切换数据源（模拟器 / 串口）。
    /// </summary>
    public void SwitchSource(string mode)
    {
        _source.Stop();
        _source.Dispose();

        AppConfig.Current.DataSourceMode = mode;
        AppConfig.Current.Save();

        _filter.Reset();
        CurrentKg = 0;
        _source = CreateSource(mode);
        AttachSource(_source);
        _source.Start();
    }

    /// <summary>当前数据源描述（状态栏显示用）。</summary>
    public string SourceDescription => _source.Describe();

    /// <summary>
    /// 保存一次磅（对应 PRD 4.1：保存后进入"未完成"状态，等待二次磅配对）。
    /// </summary>
    /// <param name="vehicleNo">车号（必填）</param>
    /// <param name="manualKg">手工补录重量；为空时取当前稳定重量</param>
    /// <returns>(是否成功, 提示消息, 保存的记录)</returns>
    public (bool Ok, string Message, WeighRecord? Record) SaveFirstWeigh(string vehicleNo, string? goods,
        string? company, string? spec, string? batchNo, string? remark,
        double? manualKg, string? operatorName)
    {
        // ---- 入参校验 ----
        if (string.IsNullOrWhiteSpace(vehicleNo))
            return (false, "请输入车号", null);

        var (weight, err) = ResolveWeight(manualKg);
        if (weight == null) return (false, err!, null);

        if (IsOverload)
            return (false, "重量超出最大量程，禁止保存", null);

        // ---- 同车号未完成校验（PRD 4.1 规则 2）----
        var unfinished = _repo.GetUnfinishedList();
        if (unfinished.Any(r => r.VehicleNo == vehicleNo))
            return (false, $"车号 [{vehicleNo}] 存在未完成的二次磅，请先完成配对", null);

        var record = new WeighRecord
        {
            ScaleNo = AppConfig.Current.ScaleNo,
            VehicleNo = vehicleNo.Trim(),
            Goods = goods?.Trim(),
            Company = company?.Trim(),
            Spec = spec?.Trim(),
            BatchNo = batchNo?.Trim(),
            Remark = remark?.Trim(),
            GrossKg = weight.Value,          // 未配对阶段暂存一次磅重量
            Operator = operatorName,
            IsManual = manualKg.HasValue,
        };
        _repo.InsertFirstWeigh(record);
        Log.Info($"保存一次磅：{record.SerialNo} 车号 {record.VehicleNo} 重量 {weight}kg");
        return (true, $"一次磅已保存：{record.SerialNo}", record);
    }

    /// <summary>
    /// 保存二次磅并结算净重（对应 PRD 4.1：匹配一次磅 → 计算净重 → 状态置为已完成）。
    /// </summary>
    /// <param name="first">待配对的一次磅记录</param>
    /// <returns>(是否成功, 提示消息, 结算后的记录)</returns>
    public (bool Ok, string Message, WeighRecord? Record) SaveSecondWeigh(
        WeighRecord first, double? manualKg, string? operatorName)
    {
        var (weight, err) = ResolveWeight(manualKg);
        if (weight == null) return (false, err!, null);

        if (weight <= 0) return (false, "二次磅重量必须大于 0", null);

        try
        {
            var result = _repo.CompleteSecondWeigh(first.Id, weight.Value, operatorName);
            Log.Info($"二次磅结算：{result.SerialNo} 车号 {result.VehicleNo} " +
                     $"毛 {result.GrossKg} 皮 {result.TareKg} 净 {result.NetKg}");
            return (true,
                $"结算完成：{result.VehicleNo} 净重 {AppConfig.Current.FormatWeight(result.NetKg ?? 0)}",
                result);
        }
        catch (Exception ex)
        {
            Log.Error("二次磅结算失败", ex);
            return (false, "结算失败：" + ex.Message, null);
        }
    }

    /// <summary>查询未完成一次磅列表。</summary>
    public List<WeighRecord> GetUnfinishedList() => _repo.GetUnfinishedList();

    /// <summary>
    /// 解析保存用重量：优先手工补录值；否则要求当前稳定。
    /// </summary>
    private (double? Weight, string? Error) ResolveWeight(double? manualKg)
    {
        if (manualKg.HasValue)
        {
            if (manualKg.Value <= 0) return (null, "补录重量必须大于 0");
            return (manualKg.Value, null);
        }

        if (!IsStable)
            return (null, "重量未稳定，请等待稳定后再保存（或使用补录）");
        if (StableKg <= 0)
            return (null, "当前重量为 0，请确认车辆已上磅");

        return (StableKg, null);
    }

    /// <summary>按配置创建数据源。</summary>
    private static IWeightSource CreateSource(string mode)
    {
        var cfg = AppConfig.Current;
        if (mode == "Serial")
        {
            return new SerialWeightSource(cfg.PortName, cfg.BaudRate, cfg.DataBits,
                Enum.TryParse<Parity>(cfg.Parity, out var p) ? p : Parity.None,
                Enum.TryParse<StopBits>(cfg.StopBits, out var s) ? s : StopBits.One);
        }
        return new SimulatedWeightSource();
    }

    /// <summary>
    /// 挂接数据源事件（在构造/切换后调用）。
    /// </summary>
    private void AttachSource(IWeightSource source)
    {
        source.WeightReceived += OnWeightReceived;
        source.ErrorOccurred += msg =>
        {
            Log.Error("数据源错误：" + msg);
            SourceError?.Invoke(msg);
        };
    }

    private void OnWeightReceived(WeightData data)
    {
        // 稳定判定：小于 20kg 视为空磅抖动，直接重置
        if (data.ValueKg < 20)
            _filter.Reset();
        else
            _filter.Push(data.ValueKg);

        CurrentKg = data.ValueKg;
        WeightUpdated?.Invoke();
    }

    public void Dispose()
    {
        _source.Stop();
        _source.Dispose();
    }
}
