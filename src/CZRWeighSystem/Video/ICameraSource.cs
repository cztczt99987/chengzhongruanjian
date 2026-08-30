namespace CZRWeighSystem.Video;

/// <summary>
/// 单路摄像头抽象接口（对应 PRD 5.7 视频监控与抓拍）。
/// 实现类：
///   SimulatedCameraSource —— 模拟画面（开发调试用）
///   RtspCameraSource      —— 真实网络摄像头（V1.1 后续接入 LibVLCSharp/FFmpeg）
/// </summary>
public interface ICameraSource : IDisposable
{
    /// <summary>摄像头名称（显示在画面左上角）</summary>
    string Name { get; }

    /// <summary>当前预览帧（直接绘制用，勿释放；随 PreviewFrameUpdated 更新）</summary>
    Image? CurrentFrame { get; }

    /// <summary>新帧到达（注意：可能来自非 UI 线程）</summary>
    event Action? PreviewFrameUpdated;

    /// <summary>启动取流</summary>
    void Start();

    /// <summary>停止取流</summary>
    void Stop();

    /// <summary>
    /// 抓拍一帧并保存为 JPEG。
    /// </summary>
    /// <param name="filePath">保存路径</param>
    /// <returns>是否成功</returns>
    bool CaptureTo(string filePath);
}
