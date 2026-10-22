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

    /// <summary>构建并启动全部摄像头（按配置的摄像头列表，每行一路）。</summary>
    public void Start()
    {
        StopAll();

        var sources = BuildSources(AppConfig.Current);
        foreach (var s in sources) s.Start();
        _sources.AddRange(sources);
        Log.Info($"摄像头已启动 {_sources.Count} 路");
    }

    /// <summary>
    /// 按配置构建摄像头源（不启动）：列表每条一路画面，IP 已填用真实流
    /// （RtspCameraSource，海康 RTSP 地址自动生成）；IP 留空 = 该路未启用，直接跳过。
    /// 名称留空按启用顺序默认命名 CAM1/CAM2…
    /// </summary>
    internal static List<ICameraSource> BuildSources(AppConfig cfg)
    {
        var list = new List<ICameraSource>();
        foreach (var cam in cfg.Cameras)
        {
            if (cam.IsBlank) continue;
            string name = string.IsNullOrWhiteSpace(cam.Name) ? $"CAM{list.Count + 1}" : cam.Name.Trim();
            list.Add(new RtspCameraSource(name, cam.BuildRtspUrl()));
        }
        return list;
    }

    /// <summary>
    /// 保存称重时抓拍全部摄像头（对应 PRD F7-02 / F12-01 防作弊留证）。
    /// 皮重与毛重分文件保存，避免二次磅覆盖一次磅的留证。
    /// </summary>
    /// <param name="serialNo">称重流水号（存档文件名的一部分）</param>
    /// <param name="isTare">本次抓拍是否对应皮重（空车去皮）；false=毛重（重车过磅）</param>
    /// <returns>已保存的图片路径</returns>
    public List<string> CaptureAll(string serialNo, bool isTare)
    {
        var saved = new List<string>();
        string dir = Path.Combine(AppContext.BaseDirectory,
            "captures", DateTime.Now.ToString("yyyyMMdd"));
        string tag = isTare ? "tare" : "gross";

        for (int i = 0; i < _sources.Count; i++)
        {
            string file = Path.Combine(dir, $"{serialNo}_{tag}_cam{i + 1}.jpg");
            if (_sources[i].CaptureTo(file)) saved.Add(file);
        }
        return saved;
    }

    /// <summary>
    /// 按流水号查找皮重抓拍图片（对应 PRD F7-03）。
    /// </summary>
    public static List<string> FindTareCaptures(string serialNo)
        => FindCapturesByTag(serialNo, "tare");

    /// <summary>
    /// 按流水号查找毛重抓拍图片（对应 PRD F7-03）。
    /// </summary>
    public static List<string> FindGrossCaptures(string serialNo)
        => FindCapturesByTag(serialNo, "gross");

    /// <summary>
    /// 按流水号查找全部已存档抓拍图片（皮重 + 毛重，对应 PRD F7-03 / 平台同步）。
    /// </summary>
    public static List<string> FindCaptures(string serialNo)
    {
        var all = new List<string>();
        all.AddRange(FindTareCaptures(serialNo));
        all.AddRange(FindGrossCaptures(serialNo));
        return all;
    }

    /// <summary>
    /// 该流水号是否存在任意抓拍图片（皮重或毛重）。
    /// </summary>
    public static bool HasAnyCaptures(string serialNo)
        => FindTareCaptures(serialNo).Count > 0 || FindGrossCaptures(serialNo).Count > 0;

    private static List<string> FindCapturesByTag(string serialNo, string tag)
    {
        string dir = Path.Combine(AppContext.BaseDirectory,
            "captures", serialNo[..Math.Min(8, serialNo.Length)]);
        if (!Directory.Exists(dir)) return [];
        return Directory.GetFiles(dir, $"{serialNo}_{tag}_*.jpg").OrderBy(f => f).ToList();
    }

    /// <summary>
    /// 手动拍照（首页放大画面/全屏画面的右键菜单）：抓拍指定路当前画面存到
    /// manualCaptures/yyyyMMdd/HHmmss_fff_camN.jpg——与称重抓拍（captures，按流水号命名、
    /// 供记录回查）分目录，人工拍的照片不会混进过磅记录的留证里。
    /// </summary>
    /// <param name="cameraIndex">摄像头路数序号（从 0 起，与 Sources 下标一致）</param>
    /// <returns>已保存的图片路径；序号越界或暂无画面时返回 null</returns>
    public string? CaptureManual(int cameraIndex)
    {
        if (cameraIndex < 0 || cameraIndex >= _sources.Count) return null;
        string dir = Path.Combine(AppContext.BaseDirectory,
            "manualCaptures", DateTime.Now.ToString("yyyyMMdd"));
        string file = Path.Combine(dir, $"{DateTime.Now:HHmmss_fff}_cam{cameraIndex + 1}.jpg");
        return _sources[cameraIndex].CaptureTo(file) ? file : null;
    }

    /// <summary>
    /// 黑匣子抓拍：保存全部摄像头当前画面到 blackbox/yyyyMMdd/（与称重抓拍分目录存证），
    /// 文件名 HHmmss_fff_camN.jpg。返回已保存的图片路径。
    /// </summary>
    public List<string> CaptureBlackBox()
    {
        var saved = new List<string>();
        string dir = Path.Combine(AppContext.BaseDirectory,
            "blackbox", DateTime.Now.ToString("yyyyMMdd"));
        string prefix = DateTime.Now.ToString("HHmmss_fff");

        for (int i = 0; i < _sources.Count; i++)
        {
            string file = Path.Combine(dir, $"{prefix}_cam{i + 1}.jpg");
            if (_sources[i].CaptureTo(file)) saved.Add(file);
        }
        return saved;
    }

    private void StopAll()
    {
        foreach (var s in _sources) s.Dispose();
        _sources.Clear();
    }

    public void Dispose() => StopAll();
}
