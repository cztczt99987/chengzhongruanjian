using CZRWeighSystem.Comm;
using CZRWeighSystem.Database;

namespace CZRWeighSystem.Core;

/// <summary>
/// 黑匣子监控器：挂接称重数据流，重量稳定且达到触发重量后持续设定秒数，
/// 自动把该重量 + 全部摄像头抓拍画面保存为黑匣子记录（t_black_box）。
/// 保存动作在后台线程执行（抓拍存盘/入库不阻塞串口数据接收）；
/// 同一稳定过程只记录一次，动态后再次稳定会重新计时并记录。
/// </summary>
public class BlackBoxMonitor : IDisposable
{
    private readonly WeighManager _manager;
    private readonly Func<Video.CameraManager?> _cameras;   // 摄像头设置保存后会整体重建，取最新实例
    private readonly BlackBoxRepository _repo = new();
    private StableDwellDetector _detector;

    /// <summary>一条黑匣子记录保存完成（后台线程触发，UI 侧需自行转发到 UI 线程）</summary>
    public event Action<BlackBoxRecord>? RecordSaved;

    public BlackBoxMonitor(WeighManager manager, Func<Video.CameraManager?> cameras)
    {
        _manager = manager;
        _cameras = cameras;
        _detector = CreateDetector();
        _manager.WeightUpdated += OnWeightUpdated;
    }

    /// <summary>按最新配置重建判定器（「称重设置」保存后调用，立即生效）。</summary>
    public void Reload()
    {
        _detector = CreateDetector();
        var cfg = AppConfig.Current;
        Log.Info($"黑匣子参数已生效：启用={cfg.BlackBoxEnabled}，" +
                 $"触发重量≥{cfg.BlackBoxMinKg:0.#}kg，持续 {cfg.BlackBoxSeconds} 秒");
    }

    private static StableDwellDetector CreateDetector()
    {
        var cfg = AppConfig.Current;
        return new StableDwellDetector(cfg.BlackBoxMinKg, cfg.BlackBoxSeconds);
    }

    private void OnWeightUpdated()
    {
        if (!AppConfig.Current.BlackBoxEnabled) return;

        double kg = _manager.StableKg;
        bool fire;
        lock (_detector)
        {
            fire = _detector.ShouldFire(kg, _manager.IsStable, DateTime.Now);
        }
        if (!fire) return;

        double weightKg = kg;
        Task.Run(() => Save(weightKg));
    }

    /// <summary>抓拍 + 入库（后台线程执行；失败仅记日志，不影响称重）。</summary>
    private void Save(double weightKg)
    {
        try
        {
            var images = AppConfig.Current.CameraEnabled
                ? _cameras()?.CaptureBlackBox() ?? []
                : [];

            var record = _repo.Insert(new BlackBoxRecord
            {
                Time = DateTime.Now,
                WeightKg = weightKg,
                ImagesJson = BlackBoxRecord.ImagesToJson(images),
            });
            Log.Info($"黑匣子记录：{record.Time:HH:mm:ss} {weightKg:0.##}kg（抓拍 {images.Count} 张）");
            RecordSaved?.Invoke(record);
        }
        catch (Exception ex)
        {
            Log.Error("黑匣子记录保存失败", ex);
        }
    }

    public void Dispose() => _manager.WeightUpdated -= OnWeightUpdated;
}
