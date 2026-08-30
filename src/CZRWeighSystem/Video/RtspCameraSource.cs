using System.Diagnostics;

namespace CZRWeighSystem.Video;

/// <summary>
/// RTSP 网络摄像头源（真实摄像头接入占位，对应 PRD F7-01/F7-04）。
///
/// 接入指引（V1.1 后续迭代）：
/// 1. NuGet 引入 LibVLCSharp + VideoLAN.LibVLC.Windows（或 FFmpeg.AutoGen）
/// 2. 在 Start() 中建立 RTSP 拉流（rtsp://用户:密码@IP:554/...）
/// 3. 解码回调中把帧写入 _frame 位图并触发 PreviewFrameUpdated
/// 4. 抓拍沿用 CaptureTo 的位图保存逻辑
///
/// 当前版本为占位实现：Start 后立即报"未接入 SDK"，不影响程序运行。
/// </summary>
public class RtspCameraSource : ICameraSource
{
    public string Name { get; }
    private readonly string _rtspUrl;

    public Image? CurrentFrame => null;

#pragma warning disable CS0067
    public event Action? PreviewFrameUpdated;
#pragma warning restore CS0067

    public RtspCameraSource(string name, string rtspUrl)
    {
        Name = name;
        _rtspUrl = rtspUrl;
    }

    public void Start()
    {
        Log.Info($"[{Name}] RTSP 源尚未接入解码 SDK（{_rtspUrl}），该路无画面");
    }

    public void Stop() { }

    public bool CaptureTo(string filePath)
    {
        Log.Info($"[{Name}] 无可用画面，抓拍跳过");
        return false;
    }

    public void Dispose() { }
}
