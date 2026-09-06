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
}
