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
    private bool _savedNeedsZero;   // 上次保存后仪表尚未归零（归零拦截用）

    // ===== 通讯状态（首页通讯指示灯用）=====
    private DateTime? _lastDataTime;          // 最近一次收到仪表数据的时间
    private bool _errorSinceLastData = true;  // 启动时尚未通讯，视为异常（红色）
    private const int CommTimeoutSeconds = 5; // 超过该秒数无新数据视为通讯中断

    /// <summary>实时重量（千克），由数据源驱动更新</summary>
    public double CurrentKg { get; private set; }

    /// <summary>当前是否稳定（对应 PRD F2-02）</summary>
    public bool IsStable => _filter.IsStable;

    /// <summary>
    /// 是否达到"可保存"的稳定要求（综合设置开启"数据稳定 X 秒"时，
    /// 需在稳定基础上持续保持该秒数；未开启时与 IsStable 一致）。
    /// </summary>
    public bool IsStableForSave => _filter.IsStableForSave;

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
        _filter = new StabilityFilter(cfg.StableCount, cfg.StableToleranceKg, cfg.StableSeconds);
        _source = CreateSerialSource();
        AttachSource(_source);
    }

    /// <summary>
    /// 启动数据源，开始接收重量数据。
    /// </summary>
    public void Start() => _source.Start();

    /// <summary>
    /// 按 config.json 中的串口参数重建数据源并立即开始接收数据。
    /// 「串口参数设置」保存后调用，现场换 COM 口/波特率无需重启软件（2026-09-17 用户反馈）。
    /// 必须先停止并释放旧源，否则旧串口句柄未释放会导致新连接打开失败。
    /// </summary>
    public void RestartSerialSource()
    {
        _source.Stop();
        _source.Dispose();
        _source = CreateSerialSource();
        AttachSource(_source);

        _lastDataTime = null;
        _errorSinceLastData = true;   // 重连后尚未收到数据，通讯指示灯保持红色直至来数
        _source.Start();
    }

    /// <summary>当前数据源描述（状态栏显示用）。</summary>
    public string SourceDescription => _source.Describe();

    /// <summary>
    /// 通讯是否正常：收到过数据、错误已恢复、且 5 秒内有新数据。
    /// 尚未通讯过或通讯中断（超时/报错）时为 false。
    /// </summary>
    public bool IsCommAlive => !_errorSinceLastData
        && _lastDataTime.HasValue
        && (DateTime.Now - _lastDataTime.Value).TotalSeconds < CommTimeoutSeconds;

    /// <summary>
    /// 保存一次磅（对应 PRD 4.1：保存后进入"未完成"状态，等待二次磅配对）。
    /// </summary>
    /// <param name="vehicleNo">车号（必填）</param>
    /// <param name="deductKg">扣重（千克）：从净重中扣除的杂重</param>
    /// <param name="isTare">本次是否为空车去皮（皮重）流程：true 时一次磅重量存皮重列，false 存毛重列</param>
    /// <returns>(是否成功, 提示消息, 保存的记录)</returns>
    public (bool Ok, string Message, WeighRecord? Record) SaveFirstWeigh(string vehicleNo, string? goods,
        string? senderCompany, string? receiverCompany, string? spec, string? batchNo, string? remark,
        double? deductKg, string? operatorName, Dictionary<string, string>? ext = null,
        double? lockedWeightKg = null, bool isTare = false)
    {
        // ---- 入参校验 ----
        if (string.IsNullOrWhiteSpace(vehicleNo))
            return (false, "请选择车号", null);

        var (weight, err) = ResolveWeight(lockedWeightKg);
        if (weight == null) return (false, err!, null);

        if (IsOverload)
            return (false, "重量超出最大量程，禁止保存", null);

        // ---- 同车号未完成校验（PRD 4.1 规则 2）----
        var unfinished = _repo.GetUnfinishedList();
        if (unfinished.Any(r => r.VehicleNo == vehicleNo))
            return (false, $"车号 [{vehicleNo}] 存在未完成的二次磅，请先完成配对", null);

        // ---- 严格禁止 X 分钟内二次称重（综合设置：防止同车间隔过短重复过磅）----
        int forbidMin = AppConfig.Current.ForbidReweighMinutes;
        if (forbidMin > 0)
        {
            var lastFinished = _repo.GetLastFinishedTime(vehicleNo.Trim());
            if (lastFinished.HasValue &&
                (DateTime.Now - lastFinished.Value).TotalMinutes < forbidMin)
            {
                return (false,
                    $"严格禁止 {forbidMin} 分钟内二次称重：该车上次结算时间 " +
                    $"{lastFinished.Value:HH:mm:ss}，请稍后再称", null);
            }
        }

        var record = new WeighRecord
        {
            ScaleNo = AppConfig.Current.ScaleNo,
            VehicleNo = vehicleNo.Trim(),
            Goods = goods?.Trim(),
            SenderCompany = senderCompany?.Trim(),
            ReceiverCompany = receiverCompany?.Trim(),
            Spec = spec?.Trim(),
            BatchNo = batchNo?.Trim(),
            Remark = remark?.Trim(),
            // 未配对阶段按本次操作模式入列：空车去皮→皮重列、重车过磅→毛重列（对应业务规则）；
            // 二次磅结算时再依据数值大小归位，两种先后顺序均能正确区分毛重/皮重
            GrossKg = isTare ? null : weight.Value,
            TareKg = isTare ? weight.Value : null,
            DeductKg = deductKg,
            Operator = operatorName,
            ExtJson = WeighRecord.ExtToJson(ext),
        };
        _repo.InsertFirstWeigh(record);
        _savedNeedsZero = true;   // 保存后要求仪表归零后才能再次保存（归零拦截开启时）
        Log.Info($"保存一次磅：{record.SerialNo} 车号 {record.VehicleNo} 重量 {weight}kg");
        return (true, $"一次磅已保存：{record.SerialNo}", record);
    }

    /// <summary>
    /// 保存二次磅并结算净重（对应 PRD 4.1：匹配一次磅 → 计算净重 → 状态置为已完成）。
    /// </summary>
    /// <param name="first">待配对的一次磅记录</param>
    /// <returns>(是否成功, 提示消息, 结算后的记录)</returns>
    public (bool Ok, string Message, WeighRecord? Record) SaveSecondWeigh(
        WeighRecord first, double? deductKg, string? operatorName, double? lockedWeightKg = null)
    {
        var (weight, err) = ResolveWeight(lockedWeightKg);
        if (weight == null) return (false, err!, null);

        if (weight <= 0) return (false, "二次磅重量必须大于 0", null);

        try
        {
            // 按收费分段计算应收金额（净重 = |一次重量 - 二次重量|；未启用收费时为 null 不计费）
            // 一次磅重量可能暂存于毛重列（重车过磅先行）或皮重列（空车去皮先行），统一取 FirstWeightKg
            decimal? amountYuan = FeeCalculator.CalculateYuan(
                Math.Abs((first.FirstWeightKg ?? 0) - weight.Value), AppConfig.Current);

            var result = _repo.CompleteSecondWeigh(first.Id, weight.Value, deductKg, operatorName, amountYuan);
            _savedNeedsZero = true;
            Log.Info($"二次磅结算：{result.SerialNo} 车号 {result.VehicleNo} " +
                     $"毛 {result.GrossKg} 皮 {result.TareKg} 净 {result.NetKg}" +
                     (result.AmountYuan.HasValue ? $" 应收 {result.AmountYuan}元" : ""));
            string msg = $"结算完成：{result.VehicleNo} 净重 {AppConfig.Current.FormatWeight(result.NetKg ?? 0)}";
            if (result.AmountYuan.HasValue)
                msg += $"\n应收金额：{result.AmountYuan:0.##} 元";
            return (true, msg, result);
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
    /// 清理超期未配对的一次磅（管理员在「称重设置」中配置 FirstWeighExpireDays > 0 时启用）。
    /// 超期记录删除后，同车号再次称重按首次称重处理（不再提示配对）。
    /// 返回删除条数。
    /// </summary>
    public int CleanupExpiredUnfinished()
    {
        int days = AppConfig.Current.FirstWeighExpireDays;
        if (days <= 0) return 0;

        int n = _repo.DeleteExpiredUnfinished(days);
        if (n > 0)
            Log.Info($"已自动删除 {n} 条超过 {days} 天未配对的一次磅记录");
        return n;
    }

    /// <summary>
    /// 解析保存用重量：取当前稳定重量（扣重在录入区单独填写，不影响称量值）。
    /// 综合设置规则：
    /// 1) 数据稳定 X 秒——稳定须持续该秒数方可保存；
    /// 2) 仪表不归零不允许保存——上次保存后仪表读数须回到接近 0 才允许再次保存。
    /// lockedKg 为链式流程"确认"锁定的读数：确认时已校验稳定/非零/超载，
    /// 之后车辆下磅（归零）不影响保存，入库用该锁定值。
    /// </summary>
    private (double? Weight, string? Error) ResolveWeight(double? lockedKg = null)
    {
        var cfg = AppConfig.Current;

        // 归零拦截：保存过一次后必须等仪表回到零位（阈值放宽到 20kg 或稳定容差取大者，
        // 避免秤体微小漂移导致永远无法再保存）
        if (cfg.BlockSaveUntilZero && _savedNeedsZero)
        {
            double zeroTolerance = Math.Max(cfg.StableToleranceKg, 20);
            if (CurrentKg > zeroTolerance)
                return (null, "仪表未归零，不允许保存数据，防止重复称重");
            _savedNeedsZero = false;   // 已回到零位，解除拦截
        }

        if (lockedKg.HasValue)
            return (lockedKg, null);   // 确认锁定的读数：不再要求当前仍稳定/非零

        if (!IsStable)
            return (null, "重量未稳定，请等待稳定后再保存");
        if (cfg.StableSeconds > 0 && !IsStableForSave)
            return (null, $"数据已稳定，但未持续 {cfg.StableSeconds} 秒，请稍候再保存");
        if (StableKg <= 0)
            return (null, "当前重量为 0，请确认车辆已上磅");

        return (StableKg, null);
    }

    /// <summary>按配置创建串口数据源。</summary>
    private static SerialWeightSource CreateSerialSource()
    {
        var cfg = AppConfig.Current;
        return new SerialWeightSource(cfg.PortName, cfg.BaudRate, cfg.DataBits,
            Enum.TryParse<Parity>(cfg.Parity, out var p) ? p : Parity.None,
            Enum.TryParse<StopBits>(cfg.StopBits, out var s) ? s : StopBits.One,
            cfg.WeightDecimalDigits);
    }

    /// <summary>
    /// 挂接数据源事件（在构造/切换后调用）。
    /// </summary>
    private void AttachSource(IWeightSource source)
    {
        source.WeightReceived += OnWeightReceived;
        source.ErrorOccurred += msg =>
        {
            _errorSinceLastData = true;   // 通讯出错 → 指示灯立即变红，直到恢复收数
            Log.Error("数据源错误：" + msg);
            SourceError?.Invoke(msg);
        };
    }

    private void OnWeightReceived(WeightData data)
    {
        _filter.Push(data.ValueKg);

        _lastDataTime = data.Time;      // 收到数据 → 通讯恢复正常（指示灯变绿）
        _errorSinceLastData = false;

        CurrentKg = data.ValueKg;
        WeightUpdated?.Invoke();
    }

    /// <summary>注入一次重量采样（测试用：模拟仪表来数；生产代码走数据源事件）。</summary>
    internal void InjectWeight(double kg)
        => OnWeightReceived(new WeightData(kg, DateTime.Now));

    public void Dispose()
    {
        _source.Stop();
        _source.Dispose();
    }
}
