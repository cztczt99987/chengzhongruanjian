using CZRWeighSystem.Database;
using CZRWeighSystem.Sync;
using CZRWeighSystem.UI;

namespace CZRWeighSystem.Tests;

/// <summary>
/// 平台对接设置窗体测试：
/// 1) 构造回显 config.json 已保存的对接配置；
/// 2) 保存校验：启用时地址/AppKey/AppSecret 必填且地址需 http(s)；
/// 3) 保存写入 AppConfig 并立即生效；
/// 4) 失败明细列表展示（类型/引用号/原因/时间）。
/// </summary>
[Collection("数据库顺序")]
public class PlatformSyncSettingsDialogTests
{
    public PlatformSyncSettingsDialogTests() => Db.Initialize();

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
    public void 失败明细列表_展示失败项类型引用号原因与时间()
    {
        var repo = new PushQueueRepository();
        using (var conn = Db.CreateConnection())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "DELETE FROM t_push_queue";
            cmd.ExecuteNonQuery();
        }

        string photoRef = "captures/20261025/20261025-01-0001_tare_cam1";
        Assert.True(repo.Enqueue(PushQueueRepository.KindPhoto, photoRef, "{}"));
        long id = repo.GetDue(PushQueueRepository.KindPhoto, 100).First(i => i.ClientRef == photoRef).Id;
        repo.MarkFailed(id, "平台拒收：图片损坏");

        using var f = new PlatformSyncSettingsDialog();
        f.InfoBox = (_, _, _) => { };   // 重推成功提示不弹真实 MessageBox
        Assert.Equal(1, f.GridFailed.Rows.Count);
        Assert.Equal("照片", f.GridFailed.Rows[0].Cells["Kind"].Value);
        Assert.Equal(photoRef, f.GridFailed.Rows[0].Cells["Ref"].Value);
        Assert.Equal("平台拒收：图片损坏", f.GridFailed.Rows[0].Cells["Error"].Value);
        Assert.NotEqual("", f.GridFailed.Rows[0].Cells["Time"].Value);

        // 重推后列表清空（项回到待推送）；未显示窗体时 PerformClick 无效，需反射触发 OnClick
        typeof(Control).GetMethod("OnClick", System.Reflection.BindingFlags.NonPublic
            | System.Reflection.BindingFlags.Instance)!.Invoke(f.BtnRequeue, [EventArgs.Empty]);
        Assert.Empty(f.GridFailed.Rows.Cast<DataGridViewRow>());
        repo.GetDue(PushQueueRepository.KindPhoto, 100);   // 无异常即为待推送可再次取到

        using var clean = Db.CreateConnection();
        using var del = clean.CreateCommand();
        del.CommandText = "DELETE FROM t_push_queue";
        del.ExecuteNonQuery();
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
        f.HistoryBackfill = () => { };   // 不启动后台补推，避免影响共享测试库

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
