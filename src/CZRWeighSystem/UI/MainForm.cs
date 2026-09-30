using CZRWeighSystem.Core;

namespace CZRWeighSystem.UI;

/// <summary>
/// 称重主界面（2026-09-30 按目标版式二次改版）：
/// 顶部为「空车去皮 / 重车过磅」手动选择按钮 + 大屏实时重量显示 + 稳定/通讯指示，
/// 点击按钮弹出称重录入窗（首页不再放置信息录入区）；
/// 中部为 3 个摄像头画面槽位（最多显示 3 路），最右侧右上角为今日称重统计面板（点击可收起/展开）；
/// 底部为称重记录列表（可选显示条数 20/50/70/100，双击未完成行带车号打开录入窗结算）。
/// 布局采用纯代码方式构建。
/// </summary>
public class MainForm : Form
{
    private readonly WeighManager _manager = new();
    private readonly AppConfig _cfg = AppConfig.Current;

    // ===== 顶部称重条 =====
    private Label _lblWeight = null!;        // 大字实时重量
    private Label _lblStable = null!;        // 稳定/动态指示
    private Label _lblComm = null!;          // 通讯状态指示（绿=正常，红=未通讯/中断）
    private Button _btnTare = null!;         // 空车去皮（弹出具重录入窗）
    private Button _btnGross = null!;        // 重车过磅（弹出毛重录入窗）

    // ===== 视频监控（首页中部固定 3 槽位；大屏弹窗见菜单「视频监控」；抓拍由保存称重触发）=====
    // 摄像头设置保存后整体重建，故不可 readonly
    private Video.CameraManager _cameras = new();
    private GroupBox _videoPanel = null!;                                // 中部画面区
    private readonly List<(Video.ICameraSource Src, Action Handler)> _videoBindings = [];
    private readonly List<(Video.ICameraSource Src, PictureBox Pic, Label Hint)> _videoTiles = [];
    private readonly System.Windows.Forms.Timer _videoStallTimer = new() { Interval = 2000 };
    private DateTime _videoBoundUtc = DateTime.UtcNow;   // 本批画面的绑定时间（判断"从未出过画面"）
    private static readonly TimeSpan VideoStallTimeout = TimeSpan.FromSeconds(30);

    /// <summary>首页画面槽位数量（最多同时显示 3 路摄像头画面）。</summary>
    internal const int MaxVideoTiles = 3;

    // ===== 右侧今日称重统计（点击可收起/展开）=====
    private const int StatsW = 252;          // 展开宽度
    private const int StatsCollapsedW = 30;  // 收起后窄条宽度
    private bool _statsExpanded = true;
    private GroupBox _statsPanel = null!;
    private Label _lblStatsCollapsed = null!;  // 收起状态竖排文字
    private Label _lblStatTotal = null!;     // 全部记录
    private Label _lblStatCount = null!;     // 今日磅单
    private Label _lblStatGross = null!;     // 今日毛重
    private Label _lblStatTare = null!;      // 今日皮重
    private Label _lblStatNet = null!;       // 今日净重
    private Label _lblTodaySummary = null!;  // 视频区下方今日汇总行

    // ===== 底部称重记录列表 =====
    private GroupBox _capturePanel = null!;
    private DataGridView _captureGrid = null!;
    private ComboBox _cboPageSize = null!;   // 显示条数：20/50/70/100
    private readonly Database.WeighRecordRepository _recordRepo = new();

    // ===== 状态栏 =====
    private StatusStrip _status = null!;
    private ToolStripStatusLabel _tsslSource = null!;
    private ToolStripStatusLabel _tsslState = null!;
    private ToolStripStatusLabel _tsslTime = null!;

    // 布局参数：中部视频区、底部记录列表（顶部称重条下方直接是视频区）
    private const int CameraY = 192;     // 视频监控区 y（顶部称重条底边 172 + 20）
    private const int CameraH = 320;     // 视频监控区高度（固定）
    private int _gridY = CameraY + CameraH + 34;   // 底部记录列表 y（中间留今日汇总行）

    // ===== 界面构建 =====
    public MainForm()
    {
        Text = $"精锐计量称重软件 V{Application.ProductVersion} - 磅号 {_cfg.ScaleNo} - {AppSession.DisplayName}";
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(1380, 880);
        MinimumSize = new Size(1240, 780);
        Font = new Font("Microsoft YaHei UI", 10F);
        // 高 DPI（125%/150%）下按 DPI 缩放布局，避免文字截断与控件错位
        AutoScaleMode = AutoScaleMode.Dpi;

        BuildMenu();
        BuildTopBar();
        BuildVideoPanel();
        BuildStatsPanel();
        BuildCapturePanel();
        BuildStatusBar();
        LayoutStatsPanel();      // 初始化统计面板展开态并同步视频区宽度
        Resize += (_, _) => LayoutCameraWidth();   // 窗口缩放时视频区宽度跟随统计面板

        // 订阅业务事件（串口数据来自非 UI 线程，统一转发到 UI 线程）
        _manager.WeightUpdated += () => SafeInvoke(OnWeightUpdated);
        _manager.SourceError += msg => SafeInvoke(() =>
        {
            SetState("数据源异常：" + msg, false);
            UpdateCommIndicator();   // 通讯出错 → 指示灯立即变红
        });

        Load += (_, _) =>
        {
            _manager.Start();
            _manager.CleanupExpiredUnfinished();   // 启动时清理超期未配对的一次磅（首次称重有效天数）
            RefreshGrid();
            RefreshStats();
            SetState("就绪", true);

            // 启动摄像头取流（首页内嵌预览 + 保存称重时自动抓拍用）
            if (_cfg.CameraEnabled)
            {
                _cameras.Start();
            }
            FillVideoPanel();   // 摄像头源就绪后绑定首页画面（未启用时显示提示）

            // 启动时及每 6 小时检查一次自动备份（对应 PRD F11-02）
            Utils.BackupService.AutoBackupIfNeeded();
            var backupTimer = new System.Windows.Forms.Timer { Interval = 6 * 60 * 60 * 1000 };
            backupTimer.Tick += (_, _) => Utils.BackupService.AutoBackupIfNeeded();
            backupTimer.Start();

            // 每小时清理一次超期未配对的一次磅（有删除时刷新记录列表）
            var cleanupTimer = new System.Windows.Forms.Timer { Interval = 60 * 60 * 1000 };
            cleanupTimer.Tick += (_, _) =>
            {
                if (_manager.CleanupExpiredUnfinished() > 0) RefreshGrid();
            };
            cleanupTimer.Start();
        };
        FormClosing += (_, _) =>
        {
            UnbindVideo();
            _videoStallTimer.Dispose();
            _manager.Dispose();
            _cameras.Dispose();
        };
    }

    // ---------- 顶部称重条：空车去皮 | 大屏重量 | 重车过磅 | 稳定/通讯指示 ----------
    private void BuildTopBar()
    {
        // 手动选择本次称重类型：点击弹出录入窗（2026-09-30 二次改版，录入区移入弹窗）
        _btnTare = NewButton("空车去皮", 20, 42, 175, 120, Color.FromArgb(198, 40, 40));
        _btnTare.Font = new Font("Microsoft YaHei UI", 14F, FontStyle.Bold);
        _btnTare.Click += (_, _) => OpenWeighEntry(isTare: true);

        _lblWeight = new Label
        {
            Text = "0 " + _cfg.Unit,
            Font = new Font("Consolas", 56F, FontStyle.Bold),
            ForeColor = Color.OrangeRed,
            TextAlign = ContentAlignment.MiddleCenter,
            Location = new Point(210, 32),
            Size = new Size(490, 140),
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.Black,
        };

        _btnGross = NewButton("重车过磅", 715, 42, 175, 120, Color.FromArgb(0, 130, 60));
        _btnGross.Font = new Font("Microsoft YaHei UI", 14F, FontStyle.Bold);
        _btnGross.Click += (_, _) => OpenWeighEntry(isTare: false);

        // 稳定/通讯指示（重车过磅右侧竖排）
        _lblStable = new Label
        {
            Text = "● 动态",
            Font = new Font("Microsoft YaHei UI", 14F, FontStyle.Bold),
            ForeColor = Color.Red,
            Location = new Point(905, 44),
            Size = new Size(180, 56),
            TextAlign = ContentAlignment.MiddleLeft,
        };
        // 通讯状态指示：启动时未通讯显示红色，收到仪表数据后变绿
        _lblComm = new Label
        {
            Text = "● 通讯",
            Font = new Font("Microsoft YaHei UI", 14F, FontStyle.Bold),
            ForeColor = Color.Red,
            Location = new Point(905, 106),
            Size = new Size(180, 56),
            TextAlign = ContentAlignment.MiddleLeft,
        };

        Controls.AddRange(new Control[] { _btnTare, _lblWeight, _btnGross, _lblStable, _lblComm });
    }

    /// <summary>打开称重录入弹窗（皮/毛由操作员手动选择）；保存成功后抓拍留证并刷新列表与统计。</summary>
    private void OpenWeighEntry(bool isTare, string? prefillVehicle = null)
    {
        using var f = new WeighEntryDialog(_manager, isTare, prefillVehicle);
        f.OnSaved = serial => CaptureFor(serial);
        if (f.ShowDialog(this) == DialogResult.OK)
        {
            RefreshGrid();
            RefreshStats();
        }
    }

    // ---------- 视频监控面板（首页中部，固定 3 个画面槽位；大屏弹窗见菜单） ----------
    private void BuildVideoPanel()
    {
        _videoPanel = new GroupBox
        {
            Text = "视频监控（最多 3 路，双击画面打开大屏）",
            Location = new Point(20, CameraY),
            Size = new Size(1060, CameraH),
            Anchor = AnchorStyles.Top | AnchorStyles.Left,   // 宽度由 LayoutCameraWidth 联动统计面板
        };
        FillVideoPanel();   // 构造时摄像头尚未启动，先显示占位；Load 后再绑定真实画面
        _videoStallTimer.Tick += (_, _) => CheckVideoStall();   // 定时检查各路是否长时间无画面
        Controls.Add(_videoPanel);
    }

    /// <summary>视频区宽度跟随右侧统计面板（保持 12px 间距）。</summary>
    private void LayoutCameraWidth()
    {
        _videoPanel.Width = Math.Max(280, _statsPanel.Left - _videoPanel.Left - 12);
    }

    /// <summary>
    /// 按当前摄像头源重建首页画面区（启动及摄像头设置保存后调用）。
    /// 固定 3 个槽位横向排开：已配置的槽位绑定实时画面，未配置的显示占位提示；
    /// 超过 3 路只显示前 3 路（大屏弹窗可查看全部），与保存抓拍、大屏弹窗共享同一批取流源。
    /// </summary>
    private void FillVideoPanel()
    {
        UnbindVideo();
        while (_videoPanel.Controls.Count > 0)
            _videoPanel.Controls[0].Dispose();

        if (!_cfg.CameraEnabled)
        {
            _videoPanel.Controls.Add(new Label
            {
                Text = "视频监控未启用。\r\n请用管理员账号在菜单「视频 → 摄像头设置」中开启。",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Color.Gray,
            });
            return;
        }

        var sources = _cameras.Sources;
        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = MaxVideoTiles,
            RowCount = 1,
        };
        for (int i = 0; i < MaxVideoTiles; i++)
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / MaxVideoTiles));
        table.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        for (int i = 0; i < MaxVideoTiles; i++)
            table.Controls.Add(BuildVideoTile(i < sources.Count ? sources[i] : null, i + 1));

        _videoPanel.Controls.Add(table);

        if (_videoTiles.Count > 0)
        {
            _videoBoundUtc = DateTime.UtcNow;
            _videoStallTimer.Start();
        }
    }

    /// <summary>构建单个画面槽位：有摄像头源则绑定实时帧，无源显示未配置占位。</summary>
    private Control BuildVideoTile(Video.ICameraSource? src, int index)
    {
        if (src == null)
        {
            return new Label
            {
                Text = $"摄像头 {index} 未配置\r\n请在菜单「视频 → 摄像头设置」中添加",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                BackColor = Color.FromArgb(70, 70, 70),
                ForeColor = Color.Gainsboro,
                Margin = new Padding(2),
            };
        }

        var pic = new PictureBox
        {
            Dock = DockStyle.Fill,
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Color.Black,
            Margin = new Padding(2),
        };

        // 摄像头名称角标（覆盖在画面左上角）
        pic.Controls.Add(new Label
        {
            Text = $" {src.Name} ",
            AutoSize = true,
            Location = new Point(4, 4),
            BackColor = Color.FromArgb(120, 0, 0, 0),
            ForeColor = Color.White,
        });

        // 取流异常提示（长时间无画面时由 CheckVideoStall 显示，避免只看到黑屏不知原因）
        var hint = new Label
        {
            Text = "无画面：取流失败或超时\r\n请检查地址是否有效/已过期；萤石摄像头建议改用 RTMP 地址",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = Color.Black,
            ForeColor = Color.FromArgb(255, 210, 120),
            Visible = false,
        };
        pic.Controls.Add(hint);

        // 双击画面打开大屏监控弹窗（多路小画面时查看细节）
        pic.DoubleClick += (_, _) =>
        {
            using var f = new VideoMonitorForm(_cameras);
            f.ShowDialog(this);
        };

        // 帧刷新：可能来自非 UI 线程，转发到 UI 线程（窗体关闭后不再处理）
        void OnFrame()
        {
            try
            {
                if (!IsHandleCreated || IsDisposed) return;
                _ = BeginInvoke(() =>
                {
                    if (IsDisposed) return;
                    pic.Image = src.CurrentFrame;
                    if (hint.Visible) hint.Visible = false;   // 画面恢复，撤掉异常提示
                    pic.Refresh();
                });
            }
            catch (ObjectDisposedException) { /* 窗体关闭瞬间的竞争，忽略 */ }
        }

        src.PreviewFrameUpdated += OnFrame;
        _videoBindings.Add((src, (Action)OnFrame));
        _videoTiles.Add((src, pic, hint));
        return pic;
    }

    /// <summary>各路画面是否长时间无帧（地址无效/过期/协议不支持时黑屏），显示提示并记录日志。</summary>
    private void CheckVideoStall()
    {
        var now = DateTime.UtcNow;
        foreach (var (src, _, hint) in _videoTiles)
        {
            bool stalled = IsVideoStalled(src.LastFrameUtc, _videoBoundUtc, now, VideoStallTimeout);
            if (stalled == hint.Visible) continue;
            hint.Visible = stalled;   // 无画面时显示提示，画面恢复后自动撤掉（原因与重试见 RtspCameraSource 日志）
        }
    }

    /// <summary>画面是否已超时无帧（从未收到画面时以绑定时间起算）。</summary>
    internal static bool IsVideoStalled(DateTime? lastFrameUtc, DateTime boundUtc, DateTime now, TimeSpan timeout)
        => now - (lastFrameUtc ?? boundUtc) > timeout;

    /// <summary>解绑全部帧事件（摄像头重建或窗体关闭时调用，避免向旧控件推帧）。</summary>
    private void UnbindVideo()
    {
        foreach (var (src, handler) in _videoBindings)
            src.PreviewFrameUpdated -= handler;
        _videoBindings.Clear();
        _videoTiles.Clear();
        _videoStallTimer.Stop();
    }

    /// <summary>按最新配置重建摄像头源（摄像头设置保存后调用，立即生效）。</summary>
    private void RestartCameras()
    {
        UnbindVideo();
        _cameras.Dispose();
        _cameras = new Video.CameraManager();
        if (_cfg.CameraEnabled) _cameras.Start();
        FillVideoPanel();   // 首页画面按新源重建
        SetState($"视频监控：{_cfg.Cameras.Count} 路", true);
    }

    // ---------- 右侧今日称重统计面板（点击可收起/展开） ----------
    private void BuildStatsPanel()
    {
        _statsPanel = new GroupBox
        {
            Text = "今日称重统计",
            ForeColor = Color.White,
            BackColor = Color.FromArgb(23, 54, 110),
            Location = new Point(ClientSize.Width - StatsW - 20, CameraY),   // 紧贴视频区右上角
            Size = new Size(StatsW, CameraH),
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            Cursor = Cursors.Hand,
        };
        _statsPanel.Click += (_, _) => ToggleStatsPanel();

        // 名目在左、数值在右对齐（数值底色随面板深蓝，金色高亮）
        Label Val(string name, int y)
        {
            var nameLbl = new Label
            { Text = name, AutoSize = true, Location = new Point(15, y), BackColor = Color.Transparent };
            nameLbl.Click += (_, _) => ToggleStatsPanel();
            var v = new Label
            {
                Text = "-",
                Location = new Point(100, y - 2),
                Size = new Size(138, 28),
                TextAlign = ContentAlignment.MiddleRight,
                ForeColor = Color.FromArgb(255, 214, 90),
                BackColor = Color.Transparent,
            };
            v.Click += (_, _) => ToggleStatsPanel();
            _statsPanel.Controls.Add(nameLbl);
            _statsPanel.Controls.Add(v);
            return v;
        }

        _lblStatTotal = Val("全部记录：", 56);
        _lblStatCount = Val("今日磅单：", 106);
        _lblStatGross = Val("今日毛重：", 156);
        _lblStatTare = Val("今日皮重：", 206);
        _lblStatNet = Val("今日净重：", 256);

        // 收起状态的竖排提示（展开时隐藏）
        _lblStatsCollapsed = new Label
        {
            Text = "今\r\n日\r\n统\r\n计",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Color.White,
            BackColor = Color.Transparent,
            Visible = false,
        };
        _lblStatsCollapsed.Click += (_, _) => ToggleStatsPanel();
        _statsPanel.Controls.Add(_lblStatsCollapsed);

        Controls.Add(_statsPanel);
    }

    /// <summary>展开/收起右侧今日统计面板（点击面板切换）。</summary>
    internal void ToggleStatsPanel()
    {
        _statsExpanded = !_statsExpanded;
        LayoutStatsPanel();
    }

    /// <summary>按展开/收起状态布局统计面板，并联动视频区宽度。</summary>
    private void LayoutStatsPanel()
    {
        _statsPanel.Text = _statsExpanded ? "今日称重统计" : "";
        _statsPanel.Width = _statsExpanded ? StatsW : StatsCollapsedW;
        // 右锚定只在父容器缩放时生效，程序化改宽后需手动贴回右缘（保持 20px 边距）
        _statsPanel.Left = ClientSize.Width - _statsPanel.Width - 20;
        foreach (Control c in _statsPanel.Controls)
            c.Visible = _statsExpanded != ReferenceEquals(c, _lblStatsCollapsed);
        LayoutCameraWidth();
    }

    /// <summary>刷新右侧统计面板与今日汇总行（全部记录数、今日磅单数与毛/皮/净汇总）。</summary>
    private void RefreshStats()
    {
        var (total, todayCount, gross, tare, net) = _recordRepo.GetHomeStats();
        _lblStatTotal.Text = $"{total} 条";
        _lblStatCount.Text = $"{todayCount} 张";
        _lblStatGross.Text = _cfg.FormatWeight(gross);
        _lblStatTare.Text = _cfg.FormatWeight(tare);
        _lblStatNet.Text = _cfg.FormatWeight(net);
        _lblTodaySummary.Text = $"今日称重：磅单 {todayCount} 张    净重 {_cfg.FormatWeight(net)}";
    }

    // ---------- 底部称重记录面板（显示条数可选；双击未完成行带车号打开录入窗） ----------
    private void BuildCapturePanel()
    {
        // 视频区下方今日汇总行
        _lblTodaySummary = new Label
        {
            Text = "今日称重：",
            AutoSize = true,
            Location = new Point(20, CameraY + CameraH + 6),
            ForeColor = Color.FromArgb(0, 102, 204),
            Font = new Font("Microsoft YaHei UI", 10.5F, FontStyle.Bold),
        };
        Controls.Add(_lblTodaySummary);

        int gridH = Math.Max(120, ClientSize.Height - 34 - _gridY);
        _capturePanel = new GroupBox
        {
            Text = "称重记录（双击未完成行带车号结算）",
            Location = new Point(20, _gridY),
            Size = new Size(ClientSize.Width - 40, gridH),
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
        };

        _captureGrid = new DataGridView
        {
            Location = new Point(8, 24),
            Size = new Size(_capturePanel.ClientSize.Width - 16, _capturePanel.ClientSize.Height - 32),
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            // 表头/行高随字体自适应：高 DPI 下默认固定表头高度会把中文列头截成一半
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize,
            AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCellsExceptHeaders,
            BackgroundColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
        };
        _captureGrid.Columns.Add("SerialNo", "流水号");
        _captureGrid.Columns.Add("Date", "日期");
        _captureGrid.Columns.Add("VehicleNo", "车牌号码");
        _captureGrid.Columns.Add("Goods", "货物");
        _captureGrid.Columns.Add("Gross", "毛重");
        _captureGrid.Columns.Add("Tare", "皮重");
        _captureGrid.Columns.Add("Net", "净重");
        _captureGrid.Columns.Add("GrossTime", "毛重时间");
        _captureGrid.Columns.Add("TareTime", "皮重时间");
        _captureGrid.Columns.Add("Status", "状态");

        // 双击未完成行：带车号打开录入窗（保存时按车号自动配对结算）
        _captureGrid.CellDoubleClick += (_, _) =>
        {
            if (_captureGrid.CurrentRow?.Tag is WeighRecord r && r.Status == "未完成")
            {
                OpenWeighEntry(isTare: false, prefillVehicle: r.VehicleNo);
            }
        };

        // 显示条数选择（20/50/70/100，默认 100）
        var lblPage = new Label
        {
            Text = "显示条数：",
            AutoSize = true,
            Location = new Point(_capturePanel.Width - 200, 21),
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
        };
        _cboPageSize = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(_capturePanel.Width - 110, 17),
            Size = new Size(80, 28),
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
        };
        _cboPageSize.Items.AddRange(["20", "50", "70", "100"]);
        _cboPageSize.SelectedIndex = 3;
        _cboPageSize.SelectedIndexChanged += (_, _) => RefreshGrid();

        _capturePanel.Controls.Add(_captureGrid);
        _capturePanel.Controls.Add(lblPage);
        _capturePanel.Controls.Add(_cboPageSize);
        Controls.Add(_capturePanel);
    }

    /// <summary>刷新称重记录列表（按选择的显示条数，新记录在前；未完成行标红提醒）。</summary>
    private void RefreshGrid()
    {
        int pageSize = int.TryParse(_cboPageSize.Text, out int n) && n > 0 ? n : 100;
        _captureGrid.Rows.Clear();
        foreach (var r in _recordRepo.Query(null, null, "", "", "", "").Take(pageSize))
        {
            int i = _captureGrid.Rows.Add(
                r.SerialNo,
                r.FirstTime?.ToString("yyyy-MM-dd HH:mm") ?? "",
                r.VehicleNo,
                r.Goods ?? "",
                _cfg.FormatWeight(r.GrossKg ?? 0),
                r.TareKg.HasValue ? _cfg.FormatWeight(r.TareKg.Value) : "--",
                r.NetKg.HasValue ? _cfg.FormatWeight(r.NetKg.Value) : "--",
                r.FirstTime?.ToString("HH:mm:ss") ?? "",
                r.SecondTime?.ToString("HH:mm:ss") ?? "",
                r.Status);
            _captureGrid.Rows[i].Tag = r;

            // 未完成（待二次磅）→ 整行标红提醒
            if (r.Status == "未完成")
            {
                _captureGrid.Rows[i].DefaultCellStyle.BackColor = Color.MistyRose;
                _captureGrid.Rows[i].DefaultCellStyle.ForeColor = Color.Firebrick;
            }
        }
    }

    /// <summary>保存称重后自动抓拍（对应 PRD F7-02），返回存档路径数。</summary>
    private int CaptureFor(string serialNo)
    {
        if (!_cfg.CameraEnabled) return 0;
        var files = _cameras.CaptureAll(serialNo);
        if (files.Count > 0)
            SetState($"已抓拍 {files.Count} 张存证图片", true);
        return files.Count;
    }

    // ---------- 菜单栏 ----------
    private void BuildMenu()
    {
        var menu = new MenuStrip();

        // 系统菜单：修改密码 / 注销 / 退出（对应 PRD F1-04）
        var mSys = new ToolStripMenuItem("系统(&Y)");
        mSys.DropDownItems.Add(new ToolStripMenuItem("修改密码", null, (_, _) =>
        {
            using var f = new ChangePasswordForm(AppSession.CurrentUser!.Id);
            f.ShowDialog(this);
        }));
        mSys.DropDownItems.Add(new ToolStripMenuItem("注销并重新登录", null, (_, _) =>
        {
            if (MessageBox.Show("确定注销当前用户并返回登录界面？", "确认",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                AppSession.ReLoginRequested = true;
                Close();
            }
        }));
        mSys.DropDownItems.Add(new ToolStripSeparator());
        // 数据备份/恢复（对应 PRD F11-02 手动备份、F11-03 恢复）
        mSys.DropDownItems.Add(new ToolStripMenuItem("备份数据", null, (_, _) =>
        {
            using var dlg = new SaveFileDialog
            {
                Title = "备份数据库",
                FileName = $"backup_{DateTime.Now:yyyyMMdd_HHmmss}.db",
                Filter = "数据库文件 (*.db)|*.db",
                InitialDirectory = Utils.BackupService.BackupDir,
            };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            var (ok, msg) = Utils.BackupService.BackupTo(dlg.FileName);
            MessageBox.Show(msg, ok ? "成功" : "错误",
                MessageBoxButtons.OK, ok ? MessageBoxIcon.Information : MessageBoxIcon.Error);
        }));
        mSys.DropDownItems.Add(new ToolStripMenuItem("恢复数据", null, (_, _) =>
        {
            if (MessageBox.Show(
                "恢复将覆盖当前全部数据，确定继续？\n（建议先执行一次备份）",
                "恢复数据", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;
            using var dlg = new OpenFileDialog
            {
                Title = "选择备份文件",
                Filter = "数据库文件 (*.db)|*.db",
                InitialDirectory = Utils.BackupService.BackupDir,
            };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            var (ok, msg) = Utils.BackupService.RestoreFrom(dlg.FileName);
            MessageBox.Show(msg, ok ? "成功" : "错误",
                MessageBoxButtons.OK, ok ? MessageBoxIcon.Information : MessageBoxIcon.Error);
        }));
        // 用户管理（对应 PRD F1-02：新增用户/角色分配；限管理员）
        if (AppSession.CurrentUser?.IsAdmin == true)
        {
            mSys.DropDownItems.Add(new ToolStripMenuItem("用户管理", null, (_, _) =>
            {
                using var f = new UserManagementForm();
                f.ShowDialog(this);
            }));
        }
        // 打印设置（对应 PRD F9-04：纸张、联数、字段显隐、自动打印；限管理员）
        if (AppSession.CurrentUser?.IsAdmin == true)
        {
            mSys.DropDownItems.Add(new ToolStripMenuItem("打印设置", null, (_, _) =>
            {
                using var f = new PrintSettingsDialog();
                f.ShowDialog(this);
            }));
            // 称重设置（首次称重有效天数：超期未配对的一次磅自动删除；限管理员）
            mSys.DropDownItems.Add(new ToolStripMenuItem("称重设置", null, (_, _) =>
            {
                using var f = new WeighSettingsDialog();
                f.ShowDialog(this);
            }));
            // 磅单模板管理（模板库：分类/搜索/预览/启用/复制/重命名/删除）
            mSys.DropDownItems.Add(new ToolStripMenuItem("磅单模板管理", null, (_, _) =>
            {
                using var f = new TemplateLibraryForm();
                f.ShowDialog(this);
            }));
        }
        mSys.DropDownItems.Add(new ToolStripSeparator());
        // 数据库设置（对应 PRD F9-05：单机 SQLite / 网络 SQL Server，限管理员）
        if (AppSession.CurrentUser?.IsAdmin == true)
        {
            mSys.DropDownItems.Add(new ToolStripMenuItem("数据库设置", null, (_, _) =>
            {
                using var f = new DatabaseSettingsDialog();
                f.ShowDialog(this);
            }));
        }
        mSys.DropDownItems.Add(new ToolStripSeparator());
        mSys.DropDownItems.Add(new ToolStripMenuItem("退出", null, (_, _) => Close()));
        menu.Items.Add(mSys);

        // 记录菜单：记录查询（对应 PRD 5.3 / F3-06、F3-07）
        var mRecord = new ToolStripMenuItem("记录(&R)");
        mRecord.DropDownItems.Add(new ToolStripMenuItem("记录查询", null, (_, _) =>
        {
            using var f = new RecordQueryForm();
            f.ShowDialog(this);
        }));
        menu.Items.Add(mRecord);

        // 报表菜单：报表统计（对应 PRD 5.4 / F4-01~F4-05）
        var mReport = new ToolStripMenuItem("报表(&B)");
        mReport.DropDownItems.Add(new ToolStripMenuItem("报表统计", null, (_, _) =>
        {
            using var f = new ReportForm();
            f.ShowDialog(this);
        }));
        menu.Items.Add(mReport);

        // 资料菜单：基础资料管理（对应 PRD 5.9 / F9-01~F9-05）
        var mBase = new ToolStripMenuItem("资料(&D)");
        mBase.DropDownItems.Add(new ToolStripMenuItem("基础资料管理", null, (_, _) =>
        {
            using var f = new BaseInfoForm();
            f.ShowDialog(this);
        }));
        menu.Items.Add(mBase);

        var mSource = new ToolStripMenuItem("数据源(&S)");
        mSource.DropDownItems.Add(new ToolStripMenuItem("串口参数设置", null, (_, _) =>
        {
            using var f = new SerialSettingsDialog();
            if (f.ShowDialog(this) != DialogResult.OK) return;
            _manager.RestartSerialSource();   // 保存后立即用新参数重连串口，无需重启软件
            OnWeightUpdated();                // 刷新状态栏数据源描述与通讯指示灯
        }));

        // 视频菜单（对应 PRD F7-01：首页已内嵌预览，此处为大屏弹窗，共享取流源）
        var mVideo = new ToolStripMenuItem("视频(&V)");
        mVideo.DropDownItems.Add(new ToolStripMenuItem("视频监控", null, (_, _) =>
        {
            using var f = new VideoMonitorForm(_cameras);
            f.ShowDialog(this);
        }));
        // 摄像头设置（多路取流地址配置；限管理员）
        if (AppSession.CurrentUser?.IsAdmin == true)
        {
            mVideo.DropDownItems.Add(new ToolStripSeparator());
            mVideo.DropDownItems.Add(new ToolStripMenuItem("摄像头设置", null, (_, _) =>
            {
                using var f = new CameraSettingsDialog();
                if (f.ShowDialog(this) == DialogResult.OK) RestartCameras();
            }));
        }
        menu.Items.Add(mVideo);

        mSource.DropDownItems.Add(new ToolStripSeparator());
        mSource.DropDownItems.Add(new ToolStripMenuItem("退出", null,
            (_, _) => Close()));

        menu.Items.Add(mSource);
        MainMenuStrip = menu;
        Controls.Add(menu);
    }

    // ---------- 状态栏 ----------
    private void BuildStatusBar()
    {
        _status = new StatusStrip();
        _tsslSource = new ToolStripStatusLabel { Text = "数据源：-" };
        _tsslState = new ToolStripStatusLabel
        { Text = "就绪", Spring = true, TextAlign = ContentAlignment.MiddleLeft };
        _tsslTime = new ToolStripStatusLabel { Text = "" };
        _status.Items.AddRange(new ToolStripItem[] { _tsslSource, _tsslState, _tsslTime });
        Controls.Add(_status);

        // 每秒刷新时间显示与通讯指示（通讯中断靠超时检测，需要定时刷新）
        var timer = new System.Windows.Forms.Timer { Interval = 1000 };
        timer.Tick += (_, _) =>
        {
            _tsslTime.Text = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            UpdateCommIndicator();
        };
        timer.Start();
    }

    private static Button NewButton(string text, int x, int y, int w, int h, Color back)
    {
        return new Button
        {
            Text = text,
            Location = new Point(x, y),
            Size = new Size(w, h),
            BackColor = back,
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold),
        };
    }

    // ================================================================
    // 实时刷新（在 UI 线程执行）
    // ================================================================

    private void OnWeightUpdated()
    {
        var cfg = AppConfig.Current;
        _lblWeight.Text = cfg.FormatWeight(_manager.CurrentKg);

        // 颜色状态：超载红 / 稳定绿 / 动态橙（对应 PRD F2-02、F2-03）
        if (_manager.IsOverload)
        {
            _lblWeight.ForeColor = Color.Red;
            _lblStable.Text = "● 超载";
            _lblStable.ForeColor = Color.Red;
        }
        else if (_manager.IsStable)
        {
            _lblWeight.ForeColor = Color.LimeGreen;
            _lblStable.Text = "● 稳定";
            _lblStable.ForeColor = Color.Green;
        }
        else
        {
            _lblWeight.ForeColor = Color.OrangeRed;
            _lblStable.Text = "● 动态";
            _lblStable.ForeColor = Color.Red;
        }

        _tsslSource.Text = $"数据源：{_manager.SourceDescription}";
        UpdateCommIndicator();
    }

    /// <summary>
    /// 刷新通讯状态指示（稳定指示下方）：
    /// 尚未通讯过或通讯中断（超时/报错）→ 红色；正常收到仪表数据 → 绿色。
    /// </summary>
    private void UpdateCommIndicator()
    {
        _lblComm.ForeColor = _manager.IsCommAlive ? Color.Green : Color.Red;
    }

    private void SetState(string msg, bool ok)
    {
        _tsslState.Text = msg;
        _tsslState.ForeColor = ok ? Color.Black : Color.Red;
    }

    /// <summary>
    /// 将动作安全转发到 UI 线程（串口数据事件来自线程池线程）。
    /// </summary>
    private void SafeInvoke(Action action)
    {
        if (!IsHandleCreated) return;
        if (InvokeRequired)
            BeginInvoke(action);
        else
            action();
    }
}
