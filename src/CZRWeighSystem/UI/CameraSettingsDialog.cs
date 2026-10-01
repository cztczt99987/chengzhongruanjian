using CZRWeighSystem.Video;

namespace CZRWeighSystem.UI;

/// <summary>
/// 摄像头设置窗体（对应 PRD F7-01，限管理员）。
/// 固定最多 3 路画面：每路一张卡片，填 IP/端口/用户名/密码
/// （可点卡片内"搜索设备"自动发现局域网内海康摄像头并填入该卡片），RTSP 取流地址自动生成。
/// IP 留空的卡片 = 该路未启用（不保存、不取流）。
/// 保存到 config.json 后由主界面重启取流，立即生效。
/// </summary>
public class CameraSettingsDialog : Form
{
    /// <summary>固定支持的最大路数（页面按 3 张卡片布局，不再动态增删行）</summary>
    private const int MaxCameras = 3;

    private CheckBox _chkEnabled = null!;
    private readonly List<CameraCard> _cards = [];

    /// <summary>底部帮助文字（测试用：验证不被卡片遮挡、完整显示在窗口内）</summary>
    internal Label HelpLabel = null!;

    public CameraSettingsDialog()
    {
        Text = "摄像头设置";
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;   // 高 DPI 下避免文字截断
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(740, 622);
        Font = new Font("Microsoft YaHei UI", 10F);

        BuildUi();
    }

    /// <summary>三路摄像头卡片（测试用）</summary>
    internal IReadOnlyList<CameraCard> Cards => _cards;

    private void BuildUi()
    {
        var cfg = AppConfig.Current;

        // ---- 总开关 ----
        _chkEnabled = new CheckBox
        {
            Text = "启用视频监控与抓拍（关闭后不取流、保存称重时不抓拍）",
            AutoSize = true,
            Location = new Point(25, 18),
            Checked = cfg.CameraEnabled,
        };
        Controls.Add(_chkEnabled);

        // ---- 固定三张摄像头卡片：每张一路画面，第 N 张对应 CAM N ----
        for (int i = 0; i < MaxCameras; i++)
        {
            var cam = i < cfg.Cameras.Count ? cfg.Cameras[i] : null;
            var card = new CameraCard(i + 1, cam)
            {
                Location = new Point(25, 50 + i * 148),
            };
            card.SearchClicked += () => _ = SearchIntoAsync(card);
            card.TestClicked += () => _ = TestConnectionAsync(card);
            _cards.Add(card);
            Controls.Add(card);
        }

        // ---- 说明（位于第 3 张卡片下方：卡片 3 底部 = 50 + 2*148 + 140 = 486，留 8px 间距） ----
        HelpLabel = new Label
        {
            Text = "最多 3 路，填了 IP 的框才启用该路；端口一般填 RTSP 端口 554（8000 是 SDK 端口，不能取流）。\r\n" +
                   "密码：萤石消费系列（CS-* 等）填机身标签 6 位验证码；海康专业机填设备管理密码。\r\n" +
                   "每路可先点「测试连接」验证（区分 连不上/端口错/密码错）；密码明文保存在本机 config.json。",
            AutoSize = true,
            Location = new Point(25, 494),
            ForeColor = Color.Gray,
        };
        Controls.Add(HelpLabel);

        // ---- 保存 / 取消（位于帮助文字下方） ----
        var btnSave = new Button
        {
            Text = "保存设置",
            Location = new Point(480, 574),
            Size = new Size(110, 34),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(0, 122, 204),
            ForeColor = Color.White,
        };
        var btnCancel = new Button
        {
            Text = "取 消",
            Location = new Point(605, 574),
            Size = new Size(110, 34),
            FlatStyle = FlatStyle.Flat,
        };
        btnSave.Click += (_, _) => Save();
        btnCancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        Controls.AddRange(new Control[] { btnSave, btnCancel });

        AcceptButton = btnSave;
        CancelButton = btnCancel;
    }

    /// <summary>点某路"搜索设备"：后台搜索局域网设备，选中的设备填入该卡片。</summary>
    private async Task SearchIntoAsync(CameraCard card)
    {
        card.BtnSearch.Enabled = false;
        card.BtnSearch.Text = "搜索中…";
        try
        {
            // SADP 广播 + 端口扫描需等待数秒，放后台线程避免界面卡死
            var (devices, rtspDisabled) = await Task.Run(() => HikDeviceDiscovery.Search());
            using var dlg = new DeviceListDialog(devices, rtspDisabled);
            if (dlg.ShowDialog(this) == DialogResult.OK && dlg.SelectedDevice != null)
                FillDevice(card, dlg.SelectedDevice);
        }
        catch (Exception ex)
        {
            Log.Error("搜索摄像头设备失败", ex);
            MessageBox.Show("搜索摄像头设备失败：" + ex.Message, "提示",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            card.BtnSearch.Text = "搜索设备";
            card.BtnSearch.Enabled = true;
        }
    }

    /// <summary>
    /// 点某路"测试连接"：后台做 RTSP 握手探测，给出明确结论
    /// （连接成功 / 连不上 / 端口非 RTSP / 密码错 / 路径不存在），避免保存后才发现黑屏。
    /// </summary>
    private async Task TestConnectionAsync(CameraCard card)
    {
        if (card.IsBlank)
        {
            MessageBox.Show("请先填写 IP 地址再测试连接。", "提示",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var cfg = card.ToConfig();
        card.BtnTest.Enabled = false;
        card.BtnTest.Text = "测试中…";
        try
        {
            var result = await Task.Run(() => RtspProbe.Check(cfg.Ip, cfg.Port, cfg.User, cfg.Password));
            MessageBox.Show(this, result.Message,
                result.Status == RtspProbeStatus.Success ? "连接成功" : "连接失败",
                MessageBoxButtons.OK,
                result.Status == RtspProbeStatus.Success ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }
        catch (Exception ex)
        {
            Log.Error("测试连接失败", ex);
            MessageBox.Show("测试过程出错：" + ex.Message, "提示",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            card.BtnTest.Text = "测试连接";
            card.BtnTest.Enabled = true;
        }
    }

    /// <summary>把搜索到的设备填入该卡片（用户名留空补 admin；端口无效补 554）。</summary>
    private static void FillDevice(CameraCard card, HikDeviceInfo device)
    {
        card.TxtIp.Text = device.Ip;
        if (string.IsNullOrWhiteSpace(card.TxtUser.Text)) card.TxtUser.Text = "admin";
        if (!int.TryParse(card.TxtPort.Text, out int port) || port <= 0) card.TxtPort.Text = "554";
    }

    /// <summary>保存成功后触发（综合设置页签内嵌时，主界面据此重启取流）。</summary>
    internal event Action? Saved;

    /// <summary>把三张卡片内容写回配置并持久化（主界面收到 OK 后重启取流）。</summary>
    private void Save()
    {
        var cfg = AppConfig.Current;
        cfg.CameraEnabled = _chkEnabled.Checked;
        cfg.Cameras = CollectCameras();
        cfg.Save();
        Log.Info($"摄像头设置已保存：{cfg.Cameras.Count} 路，启用={cfg.CameraEnabled}");
        Saved?.Invoke();
        MessageBox.Show("摄像头设置已保存，画面将按新配置刷新。", "成功",
            MessageBoxButtons.OK, MessageBoxIcon.Information);
        DialogResult = DialogResult.OK;
        Close();
    }

    /// <summary>把三张卡片收集为摄像头列表（IP 留空的卡片视为未启用，不保存）。</summary>
    internal List<CameraConfig> CollectCameras() =>
        _cards.Where(c => !c.IsBlank).Select(c => c.ToConfig()).ToList();

    /// <summary>单路摄像头卡片：名称/IP/端口/用户名/密码 + "搜索设备"/"测试连接"按钮（固定 3 张，不再增删行）。</summary>
    internal sealed class CameraCard : GroupBox
    {
        internal TextBox TxtName = null!;
        internal TextBox TxtIp = null!;
        internal TextBox TxtPort = null!;
        internal TextBox TxtUser = null!;
        internal TextBox TxtPwd = null!;
        internal Button BtnSearch = null!;
        internal Button BtnTest = null!;

        /// <summary>点"搜索设备"（窗体负责搜索并把结果回填本卡片）</summary>
        internal event Action? SearchClicked;

        /// <summary>点"测试连接"（窗体负责做 RTSP 握手探测）</summary>
        internal event Action? TestClicked;

        internal CameraCard(int index, CameraConfig? cam)
        {
            Text = $"摄像头 {index}";
            Size = new Size(690, 140);

            // 第 1 行：名称 | IP 地址 | 端口
            TxtName = AddField("名称", 18, 30, 110);
            TxtIp = AddField("IP 地址", 212, 30, 220);
            TxtPort = AddField("端口", 516, 30, 70);

            // 第 2 行：用户名 | 密码
            TxtUser = AddField("用户名", 18, 68, 110);
            TxtPwd = AddField("密码", 212, 68, 220);

            // 第 3 行：搜索设备 | 测试连接
            BtnSearch = new Button
            {
                Text = "搜索设备",
                Location = new Point(18, 102),
                Size = new Size(130, 30),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(0, 122, 204),
                ForeColor = Color.White,
            };
            new ToolTip().SetToolTip(BtnSearch, "自动搜索局域网内的海康摄像头，填入本路");
            BtnSearch.Click += (_, _) => SearchClicked?.Invoke();
            Controls.Add(BtnSearch);

            BtnTest = new Button
            {
                Text = "测试连接",
                Location = new Point(156, 102),
                Size = new Size(130, 30),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(0, 122, 204),
                ForeColor = Color.White,
            };
            new ToolTip().SetToolTip(BtnTest, "按当前填写的 IP/端口/用户名/密码实际连接一次，验证能否取流");
            BtnTest.Click += (_, _) => TestClicked?.Invoke();
            Controls.Add(BtnTest);

            if (cam != null)
            {
                LoadFrom(cam);
            }
            else
            {
                // 未配置的卡片预填海康默认值，通常只需再填 IP 和密码
                TxtPort.Text = "554";
                TxtUser.Text = "admin";
            }
        }

        /// <summary>IP 留空 = 该路未启用</summary>
        internal bool IsBlank => string.IsNullOrWhiteSpace(TxtIp.Text);

        /// <summary>卡片内容转摄像头配置（端口/用户名留空按海康默认 554/admin 处理）。</summary>
        internal CameraConfig ToConfig() => new()
        {
            Name = TxtName.Text.Trim(),
            Ip = TxtIp.Text.Trim(),
            Port = int.TryParse(TxtPort.Text.Trim(), out int port) && port > 0 ? port : 554,
            User = string.IsNullOrWhiteSpace(TxtUser.Text) ? "admin" : TxtUser.Text.Trim(),
            Password = TxtPwd.Text,
        };

        /// <summary>用已有配置填充卡片。</summary>
        private void LoadFrom(CameraConfig cam)
        {
            TxtName.Text = cam.Name;
            TxtIp.Text = cam.Ip;
            TxtPort.Text = (cam.Port > 0 ? cam.Port : 554).ToString();
            TxtUser.Text = cam.User;
            TxtPwd.Text = cam.Password;
        }

        /// <summary>加一组"标签 + 输入框"（标签在输入框左侧，间距统一）。</summary>
        private TextBox AddField(string labelText, int x, int y, int boxWidth)
        {
            Controls.Add(new Label
            {
                Text = labelText,
                AutoSize = true,
                Location = new Point(x, y + 4),
            });
            var box = new TextBox
            {
                Location = new Point(x + 60, y),
                Size = new Size(boxWidth, 27),
            };
            Controls.Add(box);
            return box;
        }
    }

    /// <summary>
    /// 搜索结果选择对话框：列出搜索到的设备（SADP 应答或 RTSP 端口开放），
    /// 选中后"填入"（或双击）回填到设置卡片；
    /// 仅 8000 端口开放（RTSP/本地服务未开启）的疑似海康/萤石设备以提示行展示。
    /// </summary>
    private class DeviceListDialog : Form
    {
        /// <summary>用户选中的设备（点"填入"或双击行后非空）</summary>
        public HikDeviceInfo? SelectedDevice { get; private set; }

        public DeviceListDialog(List<HikDeviceInfo> devices, List<string> rtspDisabled)
        {
            Text = "搜索到的设备";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(560, 380);
            Font = new Font("Microsoft YaHei UI", 10F);

            var list = new ListView
            {
                Location = new Point(20, 15),
                Size = new Size(520, 270),
                View = View.Details,
                FullRowSelect = true,
                HideSelection = false,
            };
            list.Columns.Add("IP 地址", 150);
            list.Columns.Add("型号", 170);
            list.Columns.Add("MAC 地址", 110);
            list.Columns.Add("SDK 端口", 70);
            foreach (var d in devices)
                list.Items.Add(new ListViewItem(
                    [d.Ip, d.Model, d.Mac, d.SdkPort > 0 ? d.SdkPort.ToString() : "-"]));

            // 无可用设备时给出原因指引（萤石系消费摄像头默认关闭本地服务，不响应 SADP）
            if (devices.Count == 0)
            {
                if (rtspDisabled.Count > 0)
                {
                    AddHint(list, "发现疑似海康/萤石设备，但其 RTSP/本地服务未开启：");
                    foreach (var ip in rtspDisabled)
                        AddHint(list, $"    {ip}  （在萤石云视频App开启RTSP后重新搜索，或直接手动填入此IP）");
                }
                else
                {
                    AddHint(list, "未搜索到设备。请检查：");
                    AddHint(list, "1. 摄像头已通电并接入同一局域网；");
                    AddHint(list, "2. 萤石/海康消费系列默认关闭本地服务：萤石云视频App → 我的 → 工具 →");
                    AddHint(list, "    局域网设备预览，扫描到设备并开启 RTSP/本地直播后重新搜索；");
                    AddHint(list, "3. 仍搜不到可直接手动填写 IP/端口/用户名/密码。");
                }
            }

            var btnFill = new Button
            {
                Text = "填 入",
                Location = new Point(305, 305),
                Size = new Size(110, 34),
                Enabled = false,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(0, 122, 204),
                ForeColor = Color.White,
            };
            var btnCancel = new Button
            {
                Text = "取 消",
                Location = new Point(430, 305),
                Size = new Size(110, 34),
                FlatStyle = FlatStyle.Flat,
            };

            list.SelectedIndexChanged += (_, _) =>
                btnFill.Enabled = list.SelectedIndices.Count > 0 && devices.Count > 0;
            list.DoubleClick += (_, _) => { if (Pick(list)) Close(); };
            btnFill.Click += (_, _) => { if (Pick(list)) Close(); };
            btnCancel.Click += (_, _) => Close();
            Controls.Add(list);
            Controls.AddRange(new Control[] { btnFill, btnCancel });
            return;

            // 灰色提示行（不参与选择）
            static void AddHint(ListView lv, string text) =>
                lv.Items.Add(new ListViewItem(text) { ForeColor = Color.Gray });

            // 选中行转 DialogResult：无选中行时提示并保持打开
            bool Pick(ListView lv)
            {
                if (lv.SelectedItems.Count == 0 || devices.Count == 0) return false;
                string ip = lv.SelectedItems[0].SubItems[0].Text;
                SelectedDevice = devices.FirstOrDefault(d => d.Ip == ip);
                if (SelectedDevice == null)
                {
                    MessageBox.Show("请选择有效的设备行", "提示",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return false;
                }
                DialogResult = DialogResult.OK;
                return true;
            }
        }
    }
}