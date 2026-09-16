using CZRWeighSystem.UI;
using CZRWeighSystem.Video;

namespace CZRWeighSystem.Tests;

/// <summary>
/// RtspCameraSource 无流状态行为测试（不加载 VLC 原生库，不起真实取流）：
/// 未启动取流时应无画面、抓拍安全返回 false，不影响程序运行。
/// </summary>
public class RtspCameraSourceTests
{
    [Fact]
    public void CaptureTo_WithoutStart_ReturnsFalseAndCreatesNoFile()
    {
        using var src = new RtspCameraSource("CAM1", "http://example.invalid/live.flv");
        string file = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".jpg");

        try
        {
            Assert.Null(src.CurrentFrame);
            Assert.False(src.CaptureTo(file));
            Assert.False(File.Exists(file));
        }
        finally
        {
            if (File.Exists(file)) File.Delete(file);
        }
    }

    [Fact]
    public void StartStop_WithInvalidUrl_DoesNotThrow()
    {
        var src = new RtspCameraSource("CAM1", "http://example.invalid/live.flv");
        src.Start();
        src.Stop();
        src.Dispose();
        Assert.Null(src.CurrentFrame);
    }

    [Fact]
    public void LastFrameUtc_WithoutStart_IsNull()
    {
        using var src = new RtspCameraSource("CAM1", "http://example.invalid/live.flv");
        Assert.Null(src.LastFrameUtc);
    }

    /// <summary>
    /// 启动取流前必须绑定媒体：未绑定时 MediaPlayer.Play() 是空操作（不报错、不出画），
    /// 界面会一直黑屏且无任何日志——曾因此排查困难。
    /// </summary>
    [Fact]
    public void Start_BindsMediaBeforePlay()
    {
        using var src = new RtspCameraSource("CAM1", "http://example.invalid/live.flv");
        src.Start();
        try
        {
            Assert.True(src.IsMediaBound);
        }
        finally
        {
            src.Stop();
        }
    }

    /// <summary>
    /// 无画面超时判定（首页黑屏提示的依据）：
    /// 从未收到画面时以绑定时间起算，收到过画面则以最后一帧时间起算。
    /// </summary>
    [Fact]
    public void IsVideoStalled_JudgesByBoundTimeOrLastFrame()
    {
        var bound = new DateTime(2026, 9, 16, 12, 0, 0, DateTimeKind.Utc);
        var timeout = TimeSpan.FromSeconds(20);

        // 从未收到画面：恰好到阈值不算，超过才算
        Assert.False(MainForm.IsVideoStalled(null, bound, bound.AddSeconds(20), timeout));
        Assert.True(MainForm.IsVideoStalled(null, bound, bound.AddSeconds(21), timeout));

        // 收到过画面：以最后一帧时间起算
        Assert.False(MainForm.IsVideoStalled(bound.AddSeconds(15), bound, bound.AddSeconds(30), timeout));
        Assert.True(MainForm.IsVideoStalled(bound.AddSeconds(5), bound, bound.AddSeconds(30), timeout));
    }
}
