using CZRWeighSystem.UI;

namespace CZRWeighSystem.Tests;

/// <summary>
/// 摄像头设置窗体构造冒烟测试：用户反馈打开该窗体时程序崩溃，
/// 用构造（不显示）复现，验证修复。本地网络直连改造后回归验证。
/// </summary>
public class CameraSettingsDialogTests
{
    [Fact]
    public void Construct_DoesNotThrow()
    {
        AppConfig.Current.Cameras =
        [
            new CameraConfig { Name = "磅前", Ip = "192.168.1.64", Port = 554, User = "admin", Password = "x" },
            new CameraConfig { Name = "", Ip = "" },
        ];

        var ex = Record.Exception(() =>
        {
            using var f = new CameraSettingsDialog();
            Assert.Equal("摄像头设置", f.Text);
        });

        Assert.Null(ex);
    }
}
