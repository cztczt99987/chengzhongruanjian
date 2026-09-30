using CZRWeighSystem.UI;

namespace CZRWeighSystem.Tests;

/// <summary>
/// 摄像头设置窗体测试（固定三路卡片式改版）：
/// 1) 构造冒烟：用户反馈打开该窗体时程序崩溃，用构造（不显示）复现，验证修复；
/// 2) 卡片收集：IP 留空的卡片视为未启用（不保存），端口/用户名留空按 554/admin 处理；
/// 3) 已有配置回填：最多回填前 3 条，第 4 条起忽略。
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
            new CameraConfig { Name = "磅后", Ip = "192.168.1.65" },
            new CameraConfig { Name = "多余", Ip = "192.168.1.66" },   // 超过 3 路只回填前 3 张卡片
        ];

        var ex = Record.Exception(() =>
        {
            using var f = new CameraSettingsDialog();
            Assert.Equal("摄像头设置", f.Text);
            Assert.Equal(3, f.Cards.Count);
        });

        Assert.Null(ex);
    }

    [Fact]
    public void CollectCameras_BlankIpSkipped_PortAndUserDefaulted()
    {
        AppConfig.Current.Cameras = [];
        using var f = new CameraSettingsDialog();
        var cards = f.Cards;

        cards[0].TxtName.Text = " 磅前 ";
        cards[0].TxtIp.Text = " 192.168.1.64 ";
        cards[0].TxtPort.Text = "";                // 端口留空 → 554
        cards[0].TxtUser.Text = "";                // 用户名留空 → admin
        cards[1].TxtIp.Text = "";                  // IP 留空 → 未启用，不保存
        cards[2].TxtIp.Text = "192.168.1.66";
        cards[2].TxtPort.Text = "8000";
        cards[2].TxtUser.Text = "user1";
        cards[2].TxtPwd.Text = "pw";

        var cameras = f.CollectCameras();

        Assert.Equal(2, cameras.Count);
        Assert.Equal("磅前", cameras[0].Name);
        Assert.Equal("192.168.1.64", cameras[0].Ip);
        Assert.Equal(554, cameras[0].Port);
        Assert.Equal("admin", cameras[0].User);
        Assert.Equal("192.168.1.66", cameras[1].Ip);
        Assert.Equal(8000, cameras[1].Port);
        Assert.Equal("user1", cameras[1].User);
        Assert.Equal("pw", cameras[1].Password);
    }

    [Fact]
    public void Construct_FillsFirstThreeCardsFromConfig()
    {
        AppConfig.Current.Cameras =
        [
            new CameraConfig { Name = "磅前", Ip = "192.168.1.64", Port = 8000, User = "u1", Password = "p1" },
            new CameraConfig { Name = "磅后", Ip = "192.168.1.65" },
        ];

        using var f = new CameraSettingsDialog();
        var cards = f.Cards;

        Assert.Equal("磅前", cards[0].TxtName.Text);
        Assert.Equal("192.168.1.64", cards[0].TxtIp.Text);
        Assert.Equal("8000", cards[0].TxtPort.Text);
        Assert.Equal("u1", cards[0].TxtUser.Text);
        Assert.Equal("p1", cards[0].TxtPwd.Text);

        Assert.Equal("磅后", cards[1].TxtName.Text);
        Assert.Equal("192.168.1.65", cards[1].TxtIp.Text);
        Assert.Equal("554", cards[1].TxtPort.Text);   // 缺省端口回填 554

        Assert.Equal("", cards[2].TxtIp.Text);        // 第 3 张无配置 → 空白（视为未启用）
        Assert.True(cards[2].IsBlank);
    }

    /// <summary>
    /// 帮助文字必须完整可见：曾因第 3 张卡片（底部 486）盖住文字（顶部 472）、
    /// 且末行文案过长横向溢出窗口，用户反馈"底部文字被遮挡"。回归保护三方向边界。
    /// </summary>
    [Fact]
    public void Layout_HelpLabelBelowCardsAndInsideWindow()
    {
        using var f = new CameraSettingsDialog();
        var help = f.HelpLabel;

        Assert.True(help.Top >= f.Cards[2].Bottom,          // 不被第 3 张卡片遮挡
            $"帮助文字顶 {help.Top} 应 >= 卡片底 {f.Cards[2].Bottom}");
        Assert.True(help.Bottom <= f.ClientSize.Height,     // 不被窗口下缘裁切
            $"帮助文字底 {help.Bottom} 应 <= 窗口高 {f.ClientSize.Height}");
        Assert.True(help.Right <= f.ClientSize.Width,       // 不被窗口右缘裁切
            $"帮助文字右 {help.Right} 应 <= 窗口宽 {f.ClientSize.Width}");
    }
}