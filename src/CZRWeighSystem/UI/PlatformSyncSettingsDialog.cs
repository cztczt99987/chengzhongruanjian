using CZRWeighSystem.Sync;

namespace CZRWeighSystem.UI;

/// <summary>
/// 平台对接设置窗体（综合设置『平台对接』页签，限管理员）。
/// 填写管理端"计量站管理"生成的 AppKey/AppSecret 与平台地址；
/// "测试连接"调 ping 接口验证密钥、时钟偏差与网络（对接文档 §4.1）；
/// 下方展示本地推送队列状态，失败项修改配置后可"重推失败项"。
/// </summary>
public class PlatformSyncSettingsDialog : Form
{
    internal CheckBox ChkEnabled = null!;        // 启用平台对接
    internal TextBox TxtUrl = null!;             // 平台 API 地址
    internal TextBox TxtAppKey = null!;          // AppKey
    internal TextBox TxtAppSecret = null!;       // AppSecret
    internal CheckBox ChkBlackbox = null!;       // 同步黑匣子
    internal Label LblStats = null!;             // 队列状态行
    internal Button BtnTest = null!;
    internal Button BtnRequeue = null!;

    /// <summary>结果提示框（测试注入后记录消息不弹窗，同综合设置模式）。</summary>
    internal Action<string, string, bool> InfoBox = (msg, title, ok) =>
        MessageBox.Show(msg, title, MessageBoxButtons.OK,
            ok ? MessageBoxIcon.Information : MessageBoxIcon.Warning);

    private readonly PushQueueRepository _queueRepo = new();

    public PlatformSyncSettingsDialog()
    {
        Text = "平台对接设置";
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(560, 360);
        Font = new Font("Microsoft YaHei UI", 10F);

        BuildUi();
        RefreshStats();   // 构造即填充队列状态（内嵌页签不触发 Load）
    }

    private void BuildUi()
    {
        var cfg = AppConfig.Current;

        ChkEnabled = new CheckBox
        {
            Text = "启用平台对接（称重记录/抓拍照片/黑匣子/作废 自动同步）",
            AutoSize = true,
            Location = new Point(25, 20),
            Checked = cfg.PlatformSyncEnabled,
        };

        var lblUrl = new Label { Text = "平台地址：", AutoSize = true, Location = new Point(25, 62) };
        TxtUrl = new TextBox
        {
            Location = new Point(140, 58),
            Size = new Size(390, 28),
            Text = cfg.PlatformApiUrl,
        };

        var lblKey = new Label { Text = "AppKey：", AutoSize = true, Location = new Point(25, 102) };
        TxtAppKey = new TextBox
        {
            Location = new Point(140, 98),
            Size = new Size(390, 28),
            Text = cfg.PlatformAppKey,
        };

        var lblSecret = new Label { Text = "AppSecret：", AutoSize = true, Location = new Point(25, 142) };
        TxtAppSecret = new TextBox
        {
            Location = new Point(140, 138),
            Size = new Size(390, 28),
            Text = cfg.PlatformAppSecret,
        };

        ChkBlackbox = new CheckBox
        {
            Text = "同步黑匣子记录（重量+抓拍留痕，不进各端 App，仅平台管理端可查）",
            AutoSize = true,
            Location = new Point(25, 178),
            Checked = cfg.PlatformSyncBlackbox,
        };

        var lblHint = new Label
        {
            Text = "凭证由平台管理端「计量站管理」生成；密钥泄露可重置，旧密钥立即失效。",
            AutoSize = true,
            Location = new Point(25, 212),
            ForeColor = Color.Gray,
        };

        LblStats = new Label
        {
            Text = "",
            AutoSize = true,
            Location = new Point(25, 244),
            ForeColor = Color.Gray,
        };

        BtnTest = new Button
        {
            Text = "测试连接",
            Location = new Point(140, 278),
            Size = new Size(110, 34),
            FlatStyle = FlatStyle.Flat,
        };
        BtnRequeue = new Button
        {
            Text = "重推失败项",
            Location = new Point(265, 278),
            Size = new Size(110, 34),
            FlatStyle = FlatStyle.Flat,
        };
        var btnSave = new Button
        {
            Text = "保存设置",
            Location = new Point(140, 318),
            Size = new Size(110, 34),
            BackColor = Color.FromArgb(0, 122, 204),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
        };
        var btnCancel = new Button
        {
            Text = "取 消",
            Location = new Point(265, 318),
            Size = new Size(110, 34),
            FlatStyle = FlatStyle.Flat,
        };

        BtnTest.Click += (_, _) => TestConnection();
        BtnRequeue.Click += (_, _) => RequeueFailed();
        btnSave.Click += (_, _) => Save();
        btnCancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };

        Controls.AddRange(new Control[]
        {
            ChkEnabled, lblUrl, TxtUrl, lblKey, TxtAppKey, lblSecret, TxtAppSecret,
            ChkBlackbox, lblHint, LblStats, BtnTest, BtnRequeue, btnSave, btnCancel,
        });
    }

    /// <summary>测试连接：用当前填写值（未保存也生效）调 ping，验证密钥/时钟/网络。</summary>
    private async void TestConnection()
    {
        string url = TxtUrl.Text.Trim();
        string key = TxtAppKey.Text.Trim();
        string secret = TxtAppSecret.Text.Trim();
        if (url.Length == 0 || key.Length == 0 || secret.Length == 0)
        {
            InfoBox("请先填写平台地址、AppKey 与 AppSecret。", "提示", false);
            return;
        }

        BtnTest.Enabled = false;
        try
        {
            using var client = new OpenApiClient(url, key, secret);
            var ping = await client.PingAsync();
            InfoBox($"连接成功：{ping.StationName}（{ping.StationCode}）\n密钥、时钟与网络均正常。",
                "成功", true);
        }
        catch (PlatformAuthException)
        {
            InfoBox("鉴权失败：请检查 AppKey/AppSecret 是否正确，以及本机系统时间是否准确" +
                    "（与服务器偏差需在 5 分钟内）。", "鉴权失败", false);
        }
        catch (PlatformRateLimitException)
        {
            InfoBox("请求过于频繁（限流），请稍后再试。", "提示", false);
        }
        catch (PlatformBusinessException ex)
        {
            InfoBox("服务端返回业务错误：" + ex.Message, "失败", false);
        }
        catch (Exception ex)
        {
            InfoBox("连接失败：" + ex.Message, "错误", false);
        }
        finally
        {
            BtnTest.Enabled = true;
        }
    }

    /// <summary>失败项重置为待推送（重新按退避表推送；配合修改后的密钥使用）。</summary>
    private void RequeueFailed()
    {
        int n = _queueRepo.RequeueFailed();
        RefreshStats();
        InfoBox(n > 0 ? $"已将 {n} 条失败记录重新加入推送队列。"
                      : "当前没有失败记录。", "重推失败项", n > 0);
    }

    internal void RefreshStats()
    {
        var (pending, failed) = _queueRepo.GetStats();
        LblStats.Text = $"同步队列：待推送 {pending} 条，失败 {failed} 条" +
                        (failed > 0 ? "（失败原因见日志 logs/，处理后可重推）" : "");
    }

    /// <summary>保存配置（internal 供测试直调：必填校验 + 写入 AppConfig）。</summary>
    internal void Save()
    {
        string url = TxtUrl.Text.Trim().TrimEnd('/');
        if (ChkEnabled.Checked)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                InfoBox("平台地址格式不正确，应为 http(s):// 开头的完整地址。", "提示", false);
                return;
            }
            if (TxtAppKey.Text.Trim().Length == 0 || TxtAppSecret.Text.Trim().Length == 0)
            {
                InfoBox("启用平台对接时，AppKey 与 AppSecret 必填。", "提示", false);
                return;
            }
        }

        var cfg = AppConfig.Current;
        cfg.PlatformSyncEnabled = ChkEnabled.Checked;
        cfg.PlatformApiUrl = url;
        cfg.PlatformAppKey = TxtAppKey.Text.Trim();
        cfg.PlatformAppSecret = TxtAppSecret.Text.Trim();
        cfg.PlatformSyncBlackbox = ChkBlackbox.Checked;
        cfg.Save();
        Log.Info($"平台对接设置已保存：启用={cfg.PlatformSyncEnabled} 地址={cfg.PlatformApiUrl}");
        InfoBox("设置已保存，立即生效。", "成功", true);
        DialogResult = DialogResult.OK;
        Close();
    }
}
