using CZRWeighSystem.Core;

namespace CZRWeighSystem.Video;

/// <summary>
/// 摄像头管理器（对应 PRD F7-01 多路预览、F7-02 保存自动抓拍）。
/// 管理全部摄像头源的生命周期，并提供"按流水号抓拍存档"能力。
/// 存档约定：captures/yyyyMMdd/流水号_camN.jpg（免改表即可按流水号回查）。
/// </summary>
public class CameraManager : IDisposable
{
    private readonly List<ICameraSource> _sources = [];

    /// <summary>全部摄像头源</summary>
    public IReadOnlyList<ICameraSource> Sources => _sources;

    /// <summary>构建并启动全部摄像头（按配置的路数）。</summary>
    public void Start()
    {
        StopAll();

        int count = AppConfig.Current.CameraCount;
        // RTSP 地址列表（正式接入时按索引对应；为空则用模拟画面源）
        var rtspUrls = AppConfig.Current.CameraRtspUrls
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        for (int i = 0; i < count; i++)
        {
            string name = $"CAM{i + 1}";
            ICameraSource source = i < rtspUrls.Length && rtspUrls[i].Length > 0
                ? new RtspCameraSource(name, rtspUrls[i])   // 真实摄像头（待接入 SDK 后可用）
                : new SimulatedCameraSource(name);          // 模拟画面
            source.Start();
            _sources.Add(source);
        }
        Log.Info($"摄像头已启动 {count} 路");
    }

    /// <summary>
    /// 保存称重时抓拍全部摄像头（对应 PRD F7-02 / F12-01 防作弊留证）。
    /// </summary>
    /// <param name="serialNo">称重流水号（存档文件名的一部分）</param>
    /// <returns>已保存的图片路径</returns>
    public List<string> CaptureAll(string serialNo)
    {
        var saved = new List<string>();
        string dir = Path.Combine(AppContext.BaseDirectory,
            "captures", DateTime.Now.ToString("yyyyMMdd"));

        for (int i = 0; i < _sources.Count; i++)
        {
            string file = Path.Combine(dir, $"{serialNo}_cam{i + 1}.jpg");
            if (_sources[i].CaptureTo(file)) saved.Add(file);
        }
        return saved;
    }

    /// <summary>
    /// 按流水号查找已存档的抓拍图片（对应 PRD F7-03）。
    /// </summary>
    public static List<string> FindCaptures(string serialNo)
    {
        string dir = Path.Combine(AppContext.BaseDirectory,
            "captures", serialNo[..Math.Min(8, serialNo.Length)]);
        if (!Directory.Exists(dir)) return [];
        return Directory.GetFiles(dir, serialNo + "_*.jpg").OrderBy(f => f).ToList();
    }

    private void StopAll()
    {
        foreach (var s in _sources) s.Dispose();
        _sources.Clear();
    }

    public void Dispose() => StopAll();
}
