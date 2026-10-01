using CZRWeighSystem.Sync;
using CZRWeighSystem.UI;

namespace CZRWeighSystem.Tests;

/// <summary>
/// 平台对接设置窗体测试：
/// 1) 构造回显 config.json 已保存的对接配置；
/// 2) 保存校验：启用时地址/AppKey/AppSecret 必填且地址需 http(s)；
/// 3) 保存写入 AppConfig 并立即生效。
/// </summary>
public class PlatformSyncSettingsDialogTests
{
    [Fact]
    public void Construct_回显已保存配置()
    {
        var cfg = AppConfig.Current;
        cfg.PlatformSyncEnabled = true;
        cfg.PlatformApiUrl = "https://plat.example.com";
        cfg.PlatformAppKey = "k-construct";
        cfg.PlatformAppSecret = "s-construct";
        cfg.PlatformSyncBlackbox = false;

        using var f = new PlatformSyncSettingsDialog();
        Assert.True(f.ChkEnabled.Checked);
        Assert.Equal("https://plat.example.com", f.TxtUrl.Text);
        Assert.Equal("k-construct", f.TxtAppKey.Text);
        Assert.Equal("s-construct", f.TxtAppSecret.Text);
        Assert.False(f.ChkBlackbox.Checked);
        Assert.Contains("同步队列", f.LblStats.Text);   // 队列状态行已填充

        // 还原，避免影响其他测试
        cfg.PlatformSyncEnabled = false;
        cfg.PlatformApiUrl = "";
        cfg.PlatformAppKey = "";
        cfg.PlatformAppSecret = "";
        cfg.PlatformSyncBlackbox = true;
        cfg.Save();
    }

    [Fact]
    public void Save_启用时校验必填与地址格式()
    {
        using var f = new PlatformSyncSettingsDialog();
        var messages = new List<(string Msg, string Title, bool Ok)>();
        f.InfoBox = (msg, title, ok) => messages.Add((msg, title, ok));

        f.ChkEnabled.Checked = true;
        f.TxtAppKey.Text = "k1";
        f.TxtAppSecret.Text = "s1";

        f.TxtUrl.Text = "不是地址";
        f.Save();
        Assert.Single(messages);                       // 拦截：未写入配置
        Assert.False(AppConfig.Current.PlatformSyncEnabled);

        messages.Clear();
        f.TxtUrl.Text = "  https://plat.example.com/  ";
        f.TxtAppKey.Text = "";
        f.Save();
        Assert.Single(messages);                       // AppKey 缺失拦截

        // 还原
        AppConfig.Current.PlatformSyncEnabled = false;
        AppConfig.Current.Save();
    }

    [Fact]
    public void Save_写入配置并规范化地址()
    {
        using var f = new PlatformSyncSettingsDialog();
        var messages = new List<(string Msg, string Title, bool Ok)>();
        f.InfoBox = (msg, title, ok) => messages.Add((msg, title, ok));

        f.ChkEnabled.Checked = true;
        f.TxtUrl.Text = "https://plat.example.com/";
        f.TxtAppKey.Text = "k-save";
        f.TxtAppSecret.Text = "s-save";
        f.ChkBlackbox.Checked = false;

        f.Save();

        var cfg = AppConfig.Current;
        Assert.True(cfg.PlatformSyncEnabled);
        Assert.Equal("https://plat.example.com", cfg.PlatformApiUrl);   // 尾斜杠去除
        Assert.Equal("k-save", cfg.PlatformAppKey);
        Assert.Equal("s-save", cfg.PlatformAppSecret);
        Assert.False(cfg.PlatformSyncBlackbox);
        Assert.True(messages.Single().Ok);

        // 还原，避免影响其他测试
        cfg.PlatformSyncEnabled = false;
        cfg.PlatformApiUrl = "";
        cfg.PlatformAppKey = "";
        cfg.PlatformAppSecret = "";
        cfg.PlatformSyncBlackbox = true;
        cfg.Save();
    }
}
