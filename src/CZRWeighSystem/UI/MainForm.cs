using CZRWeighSystem.Core;
using System.Drawing.Drawing2D;

namespace CZRWeighSystem.UI;

/// <summary>
/// 称重主界面（2026-09-30 按目标版式二次改版）：
/// 顶部为白色工具栏：左侧快捷方式（记录查询/报表统计/视频监控/基础资料），
/// 中间为「空车去皮 / 重车过磅」手动选择按钮 + 大屏实时重量显示（稳定/通讯指示横排于大屏下方），
/// 右侧为计算器/退出快捷方式；点击称重按钮弹出录入窗（首页不再放置信息录入区）；
/// 中部为 3 个摄像头画面槽位（最多显示 3 路，单击某路画面放大查看该路），摄像头 3 画面上悬浮自绘的今日称重统计卡片（与画面顶边齐平，点击可收起/展开）；
/// 底部为称重记录列表（可选显示条数 20/50/70/100/今日全部，双击未完成行带车号打开录入窗结算）。
/// 布局采用纯代码方式构建。
/// </summary>
public class MainForm : Form
{
    private readonly WeighManager _manager = new();
    private readonly AppConfig _cfg = AppConfig.Current;

    // ===== 顶部工具栏（白色通栏：左侧快捷方式｜空车去皮/大屏重量/重车过磅｜右侧快捷方式） =====
    private MenuStrip _menu = null!;
    private Panel _topPanel = null!;
    private readonly List<Button> _shortcutButtons = [];
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

    // ===== 今日称重统计（悬浮于摄像头画面上，自绘卡片，点击可竖直收起/展开）=====
    private const int StatsW = 252;           // 展开宽度
    private const int StatsH = 282;           // 展开高度（悬浮卡片，7 行统计）
    private const int StatsCollapsedH = 32;   // 收起后仅剩标题条（竖直伸缩）
    private const int StatsHeaderH = 34;      // 标题条高度（标题居中）
    private const int StatsFirstRowY = 42;    // 首行数值 y
    private const int StatsRowGap = 34;       // 行间距
    private const int StatsRowH = 26;         // 行高
    private const int StatsGap = 10;          // 名目与数值间距
    private static readonly Color StatsHeaderColor = Color.FromArgb(202, 224, 246);     // 标题条底色
    private static readonly Color StatsBodyTopColor = Color.FromArgb(240, 247, 254);    // 内容区渐变（上）
    private static readonly Color StatsBodyBottomColor = Color.FromArgb(212, 232, 250); // 内容区渐变（下）
    private static readonly Color StatsBorderColor = Color.FromArgb(150, 186, 222);     // 卡片描边
    private static readonly Color StatsTitleColor = Color.FromArgb(16, 58, 112);        // 标题文字
    private static readonly Color StatsNameColor = Color.FromArgb(62, 84, 110);         // 名目文字
    private static readonly Color StatsValueColor = Color.FromArgb(12, 74, 156);        // 数值文字
    private bool _statsExpanded = true;
    private Panel _statsPanel = null!;
    private Label _lblStatsArrow = null!;      // 收起/展开状态箭头（标题条右侧）
    private readonly Font _statsTitleFont = new("Microsoft YaHei UI", 11F, FontStyle.Bold);
    private readonly Font _statsNameFont = new("Microsoft YaHei UI", 10F);
    private readonly Font _statsValueFont = new("Microsoft YaHei UI", 10F, FontStyle.Bold);
    private string _statTotal = "-";          // 全部记录
    private string _statToday = "-";          // 今日磅单
    private string _statGross = "-";          // 今日毛重（车数）
    private string _statTare = "-";           // 今日皮重（车数）
    private string _statNet = "-";            // 今日净重（车数）
    private string _statFake = "-";           // 今日付称（只称了重车未回空车的车数）
    private string _statAmount = "-";         // 今日金额（元，合计）
    private Label _lblTodaySummary = null!;   // 视频区下方今日汇总行

    // ===== 底部称重记录列表 =====
    private GroupBox _capturePanel = null!;
    private DataGridView _captureGrid = null!;
    private ComboBox _cboPageSize = null!;   // 显示条数：20/50/70/100/今日全部（默认 20）
    private ContextMenuStrip _captureMenu = null!;   // 记录行右键菜单：当日记录/打印预览/过磅视图
    /// <summary>条数下拉框"今日全部"选项（选中时显示今日全部记录，不限条数）。</summary>
    internal const string TodayAllPageSizeText = "今日全部";
    private RadioButton _rdoKg = null!;      // 显示单位 kg（快速切换，写回 config.Unit）
    private RadioButton _rdoTon = null!;     // 显示单位 吨
    private readonly Database.WeighRecordRepository _recordRepo = new();
    private Core.BlackBoxMonitor? _blackBox;   // 黑匣子监控（重量稳定持续设定秒数后自动记录 + 抓拍）
    private Sync.PushScheduler? _sync;         // 平台推送调度器（记录/照片/黑匣子/作废同步）

    // ===== 状态栏 =====
    private StatusStrip _status = null!;
    private ToolStripStatusLabel _tsslSource = null!;
    private ToolStripStatusLabel _tsslState = null!;
    private ToolStripStatusLabel _tsslTime = null!;

    // 布局参数：中部视频区、底部记录列表（顶部称重条下方直接是视频区）
    private const int CameraY = 172;     // 视频监控区 y（顶部称重条含稳定/通讯行底边 166 + 6）
    private const int CameraH = 400;     // 视频监控区高度（固定；加大画面区、相应压低底部记录列表）
    private int _gridY = CameraY + CameraH + 34;   // 底部记录列表 y（中间留今日汇总行）

    // ===== 界面构建 =====
    public MainForm()
    {
        Text = $"精锐计量称重软件 V{Application.ProductVersion} - 磅号 {_cfg.ScaleNo} - {AppSession.DisplayName}";
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(1380, 880);
        MinimumSize = new Size(1240, 780);
        WindowState = FormWindowState.Maximized;   // 软件打开默认全屏（最大化）
        Font = new Font("Microsoft YaHei UI", 10F);
        Icon = AppIcon.Load();                     // 窗口图标（与 exe 图标同一份内嵌 app.ico）
        // 高 DPI（125%/150%）下按 DPI 缩放布局，避免文字截断与控件错位
        AutoScaleMode = AutoScaleMode.Dpi;
        // F3 打开综合设置（对应目标软件快捷键）
        KeyPreview = true;
        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.F3 && AppSession.CurrentUser?.IsAdmin == true)
            {
                e.Handled = true;
                OpenComprehensiveSettings();
            }
        };

        BuildMenu();
        BuildTopBar();
        BuildVideoPanel();
        BuildStatsPanel();
        BuildCapturePanel();
        BuildStatusBar();
        LayoutTopBar();          // 初始化顶部称重条居中
        LayoutStatsPanel();      // 初始化统计面板展开态并贴齐右缘
        AlignStatsToVideo();     // 统计面板与摄像头画面顶边齐平
        Resize += (_, _) => LayoutTopBar();   // 窗口缩放时顶部称重条保持居中

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
            AlignStatsToVideo();   // 画面布局完成后与画面顶边齐平

            // 启动黑匣子监控（重量稳定持续设定秒数后自动记录 + 抓拍，对应黑匣子功能）
            _blackBox = new Core.BlackBoxMonitor(_manager, () => _cameras);
            _blackBox.RecordSaved += r => SafeInvoke(() =>
            {
                SetState($"黑匣子已记录 {_cfg.FormatWeight(r.WeightKg)}（抓拍 {r.ParseImages().Count} 张）", true);
                _sync?.EnqueueBlackbox(r);   // 黑匣子留痕同步平台（设置中开启时）
            });

            // 启动平台推送调度器（结算记录/抓拍照片/黑匣子/作废 同步；未启用时空转）
            _sync = new Sync.PushScheduler();
            _sync.Start();

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
            _sync?.Dispose();
            _blackBox?.Dispose();
            _manager.Dispose();
            _cameras.Dispose();
        };
    }

    // ---------- 顶部白色工具栏：左侧快捷方式｜空车去皮/大屏重量/重车过磅｜右侧快捷方式 ----------
    private const int TopPanelH = 142;    // 工具栏高度（含大屏下方稳定/通讯行）
    private const int ShortcutW = 76;     // 快捷按钮宽（4 字标签 9pt 约 61px，留足余量保证单行不换行）
    private const int ShortcutH = 92;     // 快捷按钮高（与空/重车按钮同高对齐）
    private const int ShortcutGap = 6;    // 快捷按钮间距
    private const int ShortcutLeftCount = 4;

    private void BuildTopBar()
    {
        // 白色工具栏：紧贴菜单栏下方通栏，底部分隔线与组间竖线在 Paint 中绘制
        _topPanel = new Panel
        {
            Location = new Point(0, _menu.Height),
            Size = new Size(ClientSize.Width, TopPanelH),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            BackColor = Color.White,
        };
        _topPanel.Paint += TopPanel_Paint;

        // 左侧快捷方式（与对应菜单项等价的高频入口；参考行业软件版式）
        AddShortcut("记录查询", "query", () => { using var f = new RecordQueryForm(); f.ShowDialog(this); });
        AddShortcut("报表统计", "report", () => { using var f = new ReportForm(); f.ShowDialog(this); });
        AddShortcut("视频监控", "video", () => { using var f = new VideoMonitorForm(_cameras); f.ShowDialog(this); });
        AddShortcut("基础资料", "base", () => { using var f = new BaseInfoForm(); f.ShowDialog(this); });
        // 右侧快捷方式：系统计算器 + 退出
        AddShortcut("计算器", "calc", OpenCalculator);
        AddShortcut("退出", "exit", Close);

        // 手动选择本次称重类型：点击弹出录入窗（2026-09-30 二次改版，录入区移入弹窗）
        // 浅色扁平按钮 + 自卸车图标（空厢举升=空车去皮，满载=重车过磅），图上文下
        _btnTare = NewButton("空车去皮", 20, 12, 170, 92, Color.White);
        _btnTare.Font = new Font("Microsoft YaHei UI", 12F, FontStyle.Bold);
        _btnTare.ForeColor = Color.FromArgb(55, 58, 62);
        _btnTare.FlatAppearance.BorderColor = Color.FromArgb(206, 212, 220);
        _btnTare.FlatAppearance.BorderSize = 1;
        _btnTare.Image = TruckIcon.Draw(loaded: false, 56);
        _btnTare.ImageAlign = ContentAlignment.MiddleCenter;
        _btnTare.TextImageRelation = TextImageRelation.ImageAboveText;
        _btnTare.Click += (_, _) => OpenWeighEntry(isTare: true);

        _lblWeight = new Label
        {
            Text = "0 " + _cfg.UnitDisplay,
            Font = new Font("Consolas", 54F, FontStyle.Bold),
            ForeColor = Color.OrangeRed,
            TextAlign = ContentAlignment.MiddleCenter,
            Location = new Point(210, 6),
            Size = new Size(450, 104),
            BorderStyle = BorderStyle.None,  // 无边框（去掉大屏四周的黑框线）
            BackColor = Color.Transparent,   // 无色背景（随工具栏白底），不再用黑底
        };

        _btnGross = NewButton("重车过磅", 715, 12, 170, 92, Color.White);
        _btnGross.Font = new Font("Microsoft YaHei UI", 12F, FontStyle.Bold);
        _btnGross.ForeColor = Color.FromArgb(55, 58, 62);
        _btnGross.FlatAppearance.BorderColor = Color.FromArgb(206, 212, 220);
        _btnGross.FlatAppearance.BorderSize = 1;
        _btnGross.Image = TruckIcon.Draw(loaded: true, 56);
        _btnGross.ImageAlign = ContentAlignment.MiddleCenter;
        _btnGross.TextImageRelation = TextImageRelation.ImageAboveText;
        _btnGross.Click += (_, _) => OpenWeighEntry(isTare: false);

        // 稳定/通讯指示（大屏重量下方横排，由 LayoutTopBar 成组居中；AutoSize：文本固定三字宽度稳定）
        _lblStable = new Label
        {
            Text = "● 动态",
            Font = new Font("Microsoft YaHei UI", 14F, FontStyle.Bold),
            ForeColor = Color.Red,
            BackColor = Color.White,
            AutoSize = true,
            Location = new Point(905, 114),
        };
        // 通讯状态指示：启动时未通讯显示红色，收到仪表数据后变绿
        _lblComm = new Label
        {
            Text = "● 通讯",
            Font = new Font("Microsoft YaHei UI", 14F, FontStyle.Bold),
            ForeColor = Color.Red,
            BackColor = Color.White,
            AutoSize = true,
            Location = new Point(1010, 114),
        };

        _topPanel.Controls.AddRange(new Control[] { _btnTare, _lblWeight, _btnGross, _lblStable, _lblComm });
        Controls.Add(_topPanel);
        // 居中布局在构造函数末尾统一执行（见 ctor 末尾的 LayoutTopBar 调用）
    }

    /// <summary>新增一个工具栏快捷按钮（图标在上、文字在下，白底扁平，悬停浅蓝反馈）。</summary>
    private void AddShortcut(string text, string icon, Action onClick)
    {
        var btn = new Button
        {
            Text = text,
            Size = new Size(ShortcutW, ShortcutH),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.White,
            ForeColor = Color.FromArgb(60, 66, 76),
            // 9pt：4 字标签在 76px 按钮内单行排下
            Font = new Font("Microsoft YaHei UI", 9F),
            Image = ShortcutIcon.Draw(icon, 38),
            ImageAlign = ContentAlignment.MiddleCenter,
            TextImageRelation = TextImageRelation.ImageAboveText,
            Cursor = Cursors.Hand,
            Tag = "shortcut",
        };
        btn.FlatAppearance.BorderSize = 0;
        btn.FlatAppearance.MouseOverBackColor = Color.FromArgb(234, 243, 251);
        btn.FlatAppearance.MouseDownBackColor = Color.FromArgb(213, 232, 248);
        btn.Click += (_, _) => onClick();
        _topPanel.Controls.Add(btn);
        _shortcutButtons.Add(btn);
    }

    /// <summary>启动 Windows 自带计算器（操作员常用辅助工具）；系统精简无 calc 时提示。</summary>
    private void OpenCalculator()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("calc.exe")
            {
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show("无法启动系统计算器：" + ex.Message, "提示",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    /// <summary>工具栏装饰：底部 1px 分隔线 + 快捷区与称重区之间竖向分隔线。</summary>
    private void TopPanel_Paint(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        using var bottom = new Pen(Color.FromArgb(217, 222, 229));
        g.DrawLine(bottom, 0, TopPanelH - 1, _topPanel.Width, TopPanelH - 1);
        if (_shortcutButtons.Count >= ShortcutLeftCount + 2)
        {
            using var sep = new Pen(Color.FromArgb(229, 232, 238));
            int leftX = (_shortcutButtons[ShortcutLeftCount - 1].Right + _btnTare.Left) / 2;
            int rightX = (_btnGross.Right + _shortcutButtons[ShortcutLeftCount].Left) / 2;
            g.DrawLine(sep, leftX, 14, leftX, TopPanelH - 15);
            g.DrawLine(sep, rightX, 14, rightX, TopPanelH - 15);
        }
    }

    /// <summary>
    /// 顶部工具栏横向布局：左快捷组、称重区（空车去皮｜大屏重量｜重车过磅）、右快捷组；
    /// 称重区在两组之间居中，稳定/通讯指示横排于大屏重量下方并居中。
    /// 大屏宽度弹性伸缩（300~450）：快捷按钮加宽后，最小窗口宽度（1240）下自动收窄大屏
    /// 保证三组不重叠；窗口缩放时重新布局。
    /// </summary>
    private void LayoutTopBar()
    {
        const int gap = 10;
        int leftW = ShortcutLeftCount * ShortcutW + (ShortcutLeftCount - 1) * ShortcutGap;
        int rightCount = _shortcutButtons.Count - ShortcutLeftCount;
        int rightW = rightCount * ShortcutW + Math.Max(0, rightCount - 1) * ShortcutGap;
        int w = ClientSize.Width;   // 面板通栏锚定右缘，宽度与客户区一致

        // 大屏弹性宽度：以"称重区相对窗口居中"为先——左右留出对称空隙（需容纳较宽的左快捷组），
        // 余下空间给大屏（默认窗口约 340，宽窗口封顶 450，最小窗口保底 300 优先保证不重叠）
        int edge = w >= leftW + rightW + 810 + 20 ? 10 : 0;   // 810 = 称重区满配宽（170+10+450+10+170）
        int symmetric = w - 2 * edge - 2 * leftW;             // 居中时称重区可占宽度
        _lblWeight.Width = Math.Clamp(symmetric - 2 * gap - _btnTare.Width - _btnGross.Width, 300, 450);

        int centerW = _btnTare.Width + gap + _lblWeight.Width + gap + _btnGross.Width;
        // 称重区优先相对窗口居中；左右两组不对称（4 vs 2），按可用区居中会右偏，
        // 故只在最小窗口宽度排不下时才向两侧组让位（Clamp），保证三组始终不重叠
        int ideal = (w - centerW) / 2;
        int minLeft = edge + leftW;
        int maxLeft = w - edge - rightW - centerW;
        int cx = Math.Clamp(ideal, minLeft, Math.Max(minLeft, maxLeft));
        _btnTare.Left = cx;
        _lblWeight.Left = _btnTare.Right + gap;
        _btnGross.Left = _lblWeight.Right + gap;

        for (int i = 0; i < ShortcutLeftCount; i++)
            _shortcutButtons[i].Left = edge + i * (ShortcutW + ShortcutGap);
        int rightX0 = w - edge - rightW;
        for (int i = 0; i < rightCount; i++)
            _shortcutButtons[ShortcutLeftCount + i].Left = rightX0 + i * (ShortcutW + ShortcutGap);

        // 稳定/通讯指示：大屏重量下方横排（不占用右侧空间）
        const int indicatorGap = 24;
        int rowW = _lblStable.Width + indicatorGap + _lblComm.Width;
        _lblStable.Left = _lblWeight.Left + (_lblWeight.Width - rowW) / 2;
        _lblComm.Left = _lblStable.Right + indicatorGap;

        _topPanel.Invalidate();   // 组间竖分隔线位置随布局重绘
    }

    /// <summary>打开称重录入弹窗（皮/毛由操作员手动选择）；保存成功后抓拍留证并刷新列表与统计。</summary>
    private void OpenWeighEntry(bool isTare, string? prefillVehicle = null)
    {
        using var f = new WeighEntryDialog(_manager, isTare, prefillVehicle);
        f.OnSaved = serial =>
        {
            CaptureFor(serial);
            _sync?.EnqueueRecord(serial);   // 二次磅结算完成后推送平台（一次磅内部跳过）
        };
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
            Text = "视频监控（最多 3 路，单击画面全屏查看该路）",
            Location = new Point(20, CameraY),
            Size = new Size(ClientSize.Width - 40, CameraH),   // 通栏铺满，统计面板悬浮其右上角
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
        };
        FillVideoPanel();   // 构造时摄像头尚未启动，先显示占位；Load 后再绑定真实画面
        _videoStallTimer.Tick += (_, _) => CheckVideoStall();   // 定时检查各路是否长时间无画面
        Controls.Add(_videoPanel);
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
                BackColor = SystemColors.Control,   // 浅底占位：不再用深灰/黑块
                ForeColor = Color.Gray,
                Margin = new Padding(0),
            };
        }

        var pic = new PictureBox
        {
            Dock = DockStyle.Fill,
            SizeMode = PictureBoxSizeMode.Normal,   // 帧由 Paint 等比填满（不留黑边、不拉伸变形）
            BackColor = SystemColors.Control,
            Margin = new Padding(0),
            Cursor = Cursors.Hand,
        };

        // 帧画面等比缩放填满整个槽位（居中裁剪）：去掉画面四周的黑色留边
        pic.Paint += (_, e) =>
        {
            if (src.CurrentFrame is not { } frame) return;
            var rect = FitCover(frame.Width, frame.Height, pic.ClientSize.Width, pic.ClientSize.Height);
            e.Graphics.InterpolationMode = InterpolationMode.HighQualityBilinear;
            e.Graphics.DrawImage(frame, rect);
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

        // 取流异常提示（长时间无画面时由 CheckVideoStall 显示，避免只看到空画面不知原因）
        var hint = new Label
        {
            Text = "无画面：取流失败或超时\r\n请检查地址是否有效/已过期；萤石摄像头建议改用 RTMP 地址",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = SystemColors.Control,
            ForeColor = Color.Firebrick,
            Visible = false,
        };
        pic.Controls.Add(hint);

        // 单击画面进入沉浸全屏，只显示点击的这一路（看其他路需关闭后回首页点击对应画面）
        pic.Click += (_, _) =>
        {
            using var f = new VideoMonitorForm(_cameras, index - 1, fullscreen: true);
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
                    if (hint.Visible) hint.Visible = false;   // 画面恢复，撤掉异常提示
                    pic.Invalidate();   // Paint 重新绘制当前帧
                });
            }
            catch (ObjectDisposedException) { /* 窗体关闭瞬间的竞争，忽略 */ }
        }

        src.PreviewFrameUpdated += OnFrame;
        _videoBindings.Add((src, (Action)OnFrame));
        _videoTiles.Add((src, pic, hint));
        return pic;
    }

    /// <summary>
    /// 等比缩放填满槽位的目标矩形（居中裁剪，对应 CSS background-size: cover）：
    /// 不拉伸变形、不留黑边；图片越小超出槽位的部分被裁掉。
    /// </summary>
    internal static Rectangle FitCover(int imgW, int imgH, int boxW, int boxH)
    {
        if (imgW <= 0 || imgH <= 0 || boxW <= 0 || boxH <= 0) return Rectangle.Empty;
        double scale = Math.Max((double)boxW / imgW, (double)boxH / imgH);
        int w = (int)Math.Ceiling(imgW * scale);
        int h = (int)Math.Ceiling(imgH * scale);
        return new Rectangle((boxW - w) / 2, (boxH - h) / 2, w, h);
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

    // ---------- 今日称重统计（悬浮于摄像头画面上，自绘卡片，点击可竖直收起/展开） ----------
    private void BuildStatsPanel()
    {
        _statsPanel = new Panel
        {
            // 顶部位置由 AlignStatsToVideo 与摄像头画面顶边对齐
            Location = new Point(ClientSize.Width - StatsW - 20, CameraY),
            Size = new Size(StatsW, StatsH),
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            Cursor = Cursors.Hand,
            BackColor = StatsHeaderColor,   // 标题条底色（自绘卡片；透明子控件亦取此底色）
        };
        _statsPanel.Paint += (_, e) => PaintStatsCard(e.Graphics);
        _statsPanel.Click += (_, _) => ToggleStatsPanel();

        // 收起/展开状态箭头（标题条右侧，展开▲ 收起▼）
        _lblStatsArrow = new Label
        {
            Text = "▲",
            AutoSize = true,
            Location = new Point(StatsW - 28, 7),
            Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold),
            ForeColor = StatsTitleColor,
            BackColor = Color.Transparent,
        };
        _lblStatsArrow.Click += (_, _) => ToggleStatsPanel();
        _statsPanel.Controls.Add(_lblStatsArrow);

        Controls.Add(_statsPanel);
        _statsPanel.BringToFront();   // 悬浮在视频画面之上
    }

    /// <summary>统计面板与摄像头画面顶边齐平（悬浮于画面之上，不压在画面区标题栏上）。</summary>
    private void AlignStatsToVideo()
    {
        // 画面槽位位于画面区标题栏之下：取画面宿主控件的实际位置，构造阶段布局未完成时按标题栏估算
        var host = _videoPanel.Controls.Count > 0 ? _videoPanel.Controls[0] : null;
        int pictureTop = host is { Top: > 0 } ? host.Top : _videoPanel.DisplayRectangle.Top;
        _statsPanel.Top = _videoPanel.Top + pictureTop + 2;   // 2 = 画面槽位外边距
    }

    /// <summary>
    /// 自绘统计卡片：浅蓝标题条（标题居中）+ 浅色渐变内容区 + 细描边；
    /// 数值按「名目 + 数值」成组水平居中且各行对齐，收起时只画标题条。
    /// </summary>
    private void PaintStatsCard(Graphics g)
    {
        int w = _statsPanel.Width, h = _statsPanel.Height;
        int headerH = Math.Min(StatsHeaderH, h);
        using (var header = new SolidBrush(StatsHeaderColor))
            g.FillRectangle(header, 0, 0, w, headerH);
        if (h > headerH)
        {
            var body = new Rectangle(0, headerH, w, h - headerH);
            using var brush = new LinearGradientBrush(body, StatsBodyTopColor, StatsBodyBottomColor, 90F);
            g.FillRectangle(brush, body);
        }
        using (var pen = new Pen(StatsBorderColor))
            g.DrawRectangle(pen, 0, 0, w - 1, h - 1);

        TextRenderer.DrawText(g, "今日称重统计", _statsTitleFont,
            new Rectangle(0, 0, w, headerH), StatsTitleColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

        if (!_statsExpanded) return;

        // 先量出名目/数值的最大宽度，再整体水平居中（各行左右对齐、成组居中）
        string[] names = ["全部记录：", "今日磅单：", "今日毛重：", "今日皮重：", "今日净重：", "今日付称：", "今日金额："];
        string[] values = [_statTotal, _statToday, _statGross, _statTare, _statNet, _statFake, _statAmount];
        int nameW = 0, valueW = 0;
        foreach (var n in names) nameW = Math.Max(nameW, MeasureText(n, _statsNameFont));
        foreach (var v in values) valueW = Math.Max(valueW, MeasureText(v, _statsValueFont));
        int left = Math.Max(6, (w - (nameW + StatsGap + valueW)) / 2);
        for (int i = 0; i < names.Length; i++)
        {
            int y = StatsFirstRowY + i * StatsRowGap;
            TextRenderer.DrawText(g, names[i], _statsNameFont,
                new Rectangle(left, y, nameW, StatsRowH), StatsNameColor,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, values[i], _statsValueFont,
                new Rectangle(left + nameW + StatsGap, y, valueW, StatsRowH), StatsValueColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }

    /// <summary>文本渲染宽度（与 NoPadding 绘制口径一致，用于成组居中）。</summary>
    private static int MeasureText(string text, Font font) =>
        TextRenderer.MeasureText(text, font, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding).Width;

    /// <summary>展开/收起今日称重统计面板（点击面板切换，竖直伸缩）。</summary>
    internal void ToggleStatsPanel()
    {
        _statsExpanded = !_statsExpanded;
        LayoutStatsPanel();
    }

    /// <summary>按展开/收起状态竖直伸缩统计面板：收起后仅剩标题条，箭头切换 ▲/▼。</summary>
    private void LayoutStatsPanel()
    {
        _statsPanel.Height = _statsExpanded ? StatsH : StatsCollapsedH;
        _lblStatsArrow.Text = _statsExpanded ? "▲" : "▼";
        _statsPanel.Invalidate();   // 卡片自绘：内容区随展开状态重绘
    }

    /// <summary>刷新右侧统计面板与今日汇总行（全部记录数、今日磅单数、毛/皮/净车数、付称车数与金额合计）。</summary>
    private void RefreshStats()
    {
        var (total, todayCount, grossCars, tareCars, netCars, onlyGrossCars, amountYuan) = _recordRepo.GetHomeStats();
        _statTotal = $"{total} 条";
        _statToday = $"{todayCount} 张";
        _statGross = $"{grossCars} 车";
        _statTare = $"{tareCars} 车";
        _statNet = $"{netCars} 车";
        _statFake = $"{onlyGrossCars} 车";
        _statAmount = $"{amountYuan:0.##} 元";
        _statsPanel.Invalidate();   // 卡片自绘：数值变化后重绘
        _lblTodaySummary.Text = $"今日称重：磅单 {todayCount} 张    金额合计 {amountYuan:0.##} 元";
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
            Text = "称重记录（双击未完成行结算，右键更多操作）",
            Location = new Point(20, _gridY),
            Size = new Size(ClientSize.Width - 40, gridH),
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
        };

        _captureGrid = new DataGridView
        {
            // 顶部留出条带：条数选择框必须完整露在表格上方，不能被表格遮挡
            Location = new Point(8, 50),
            Size = new Size(_capturePanel.ClientSize.Width - 16, _capturePanel.ClientSize.Height - 58),
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
        // 记录列表字段与「空车去皮/重车过磅」录入弹窗一致：车牌、品种、地主姓名/电话、
        // 司机电话、货主姓名、货主电话、单价 + 结算结果（毛/皮/净、金额、时间、司磅员、状态）；
        // 列宽由 FitCaptureColumns 按"内容宽/表头宽取大"自适应，保证表头与内容都单行完整展示，
        // 列总宽超出面板时表格自动出现横向滚动条；状态列 Fill 模式：宽窗口拉伸补满右侧
        void AddCol(string name, string header, int minWidth)
        {
            _captureGrid.Columns.Add(name, header);
            var col = _captureGrid.Columns[name]!;
            // 网格级默认 Fill 会忽略单列 Width，显式 None 才能按内容固定列宽
            col.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
            col.MinimumWidth = minWidth;   // 保底宽，实际宽度由 FitCaptureColumns 按内容/表头取大
            col.HeaderCell.Style.WrapMode = DataGridViewTriState.False;   // 表头单行不换行
        }
        AddCol("SerialNo", "流水号", 138);
        AddCol("Date", "日期", 96);
        AddCol("VehicleNo", "车牌号码", 92);
        AddCol("Goods", "品种", 70);
        AddCol("Owner", "地主姓名", 90);
        AddCol("OwnerPhone", "地主电话", 110);
        AddCol("DriverPhone", "司机电话", 110);
        AddCol("CargoOwner", "货主姓名", 90);
        AddCol("CargoOwnerPhone", "货主电话", 110);
        AddCol("UnitPrice", "单价", 60);
        AddCol("Gross", "毛重", 80);
        AddCol("Tare", "皮重", 80);
        AddCol("Net", "净重", 80);
        UpdateWeightColumnHeaders();   // 毛/皮/净表头带当前单位（如"毛重(吨)"）
        AddCol("AmountYuan", "金额(元)", 66);
        AddCol("GrossTime", "毛重时间", 76);
        AddCol("TareTime", "皮重时间", 76);
        AddCol("Operator", "司磅员", 70);
        _captureGrid.Columns.Add("Status", "状态");
        var statusCol = _captureGrid.Columns["Status"]!;   // 末列填充剩余宽度
        statusCol.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        statusCol.FillWeight = 1;
        statusCol.MinimumWidth = 60;
        statusCol.HeaderCell.Style.WrapMode = DataGridViewTriState.False;

        // 双击未完成行：带车号打开录入窗（保存时按车号自动配对结算）
        _captureGrid.CellDoubleClick += (_, _) =>
        {
            if (_captureGrid.CurrentRow?.Tag is WeighRecord r && r.Status == "未完成")
            {
                OpenWeighEntry(isTare: false, prefillVehicle: r.VehicleNo);
            }
        };

        BuildRecordContextMenu();   // 记录行右键菜单（当日记录/打印预览/过磅视图）

        // 显示条数选择（20/50/70/100/今日全部，默认 20）：置于表格上方预留条带内，紧贴面板右缘
        var lblPage = new Label
        {
            Text = "显示条数：",
            Size = new Size(84, 22),
            TextAlign = ContentAlignment.MiddleRight,
            Location = new Point(_capturePanel.Width - 240, 19),
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
        };
        _cboPageSize = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            DropDownWidth = 130,
            Location = new Point(_capturePanel.Width - 150, 16),
            Size = new Size(120, 28),
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
        };
        _cboPageSize.Items.AddRange(["20", "50", "70", "100", TodayAllPageSizeText]);
        _cboPageSize.SelectedIndexChanged += (_, _) => RefreshGrid();
        _cboPageSize.SelectedIndex = 0;   // 默认 20 条（赋值触发刷新，列表按 20 条加载）

        // 显示单位快速切换（kg/吨，位于显示条数左侧）：选中即写回 config 并刷新全部重量显示
        _rdoKg = new RadioButton
        {
            Text = "kg",
            AutoSize = true,
            Location = new Point(_capturePanel.Width - 386, 19),
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            Checked = _cfg.Unit != "t",
        };
        _rdoTon = new RadioButton
        {
            Text = "吨",
            AutoSize = true,
            Location = new Point(_capturePanel.Width - 336, 19),
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            Checked = _cfg.Unit == "t",
        };
        _rdoKg.CheckedChanged += (_, _) => SwitchUnit("kg");
        _rdoTon.CheckedChanged += (_, _) => SwitchUnit("t");

        _capturePanel.Controls.Add(_captureGrid);
        _capturePanel.Controls.Add(lblPage);
        _capturePanel.Controls.Add(_cboPageSize);
        _capturePanel.Controls.AddRange([_rdoKg, _rdoTon]);
        // 表格先加入 → 处于 z 序前层，会把后加入的控件压住；显式置前保证条数选择框始终可见
        lblPage.BringToFront();
        _cboPageSize.BringToFront();
        _rdoKg.BringToFront();
        _rdoTon.BringToFront();
        Controls.Add(_capturePanel);
    }

    /// <summary>毛/皮/净列表头按当前显示单位刷新（单位只在表头显示，单元格为纯数字）。</summary>
    private void UpdateWeightColumnHeaders()
    {
        _captureGrid.Columns["Gross"]!.HeaderText = $"毛重({_cfg.UnitDisplay})";
        _captureGrid.Columns["Tare"]!.HeaderText = $"皮重({_cfg.UnitDisplay})";
        _captureGrid.Columns["Net"]!.HeaderText = $"净重({_cfg.UnitDisplay})";
        FitCaptureColumns();   // 表头带单位变长后重新适配列宽
    }

    /// <summary>
    /// 称重记录列宽自适应：每列取「单元格内容宽」与「表头文字宽」的较大值（不低于保底宽），
    /// 保证每个字段表头与内容都单行完整展示（不换行、不截断）；
    /// 列总宽超出表格可视宽度时 DataGridView 自动出现横向滚动条。
    /// </summary>
    private void FitCaptureColumns()
    {
        foreach (DataGridViewColumn col in _captureGrid.Columns)
        {
            int headerW = TextRenderer.MeasureText(col.HeaderText, _captureGrid.Font).Width + 12;
            if (col.AutoSizeMode == DataGridViewAutoSizeColumnMode.Fill)
            {
                col.MinimumWidth = Math.Max(col.MinimumWidth, headerW);   // 末列拉伸补剩余宽，表头保完整
                continue;
            }
            int contentW = col.GetPreferredWidth(DataGridViewAutoSizeColumnMode.AllCells, true);
            col.Width = Math.Max(col.MinimumWidth, Math.Max(headerW, contentW));
        }
    }

    /// <summary>切换显示单位（kg/吨）：写回 config.json，列表、统计卡片、今日汇总与顶部大屏全部按新单位刷新。</summary>
    private void SwitchUnit(string unit)
    {
        if (_cfg.Unit == unit) return;   // 构造时按当前单位设置单选框会触发事件，同值直接返回
        _cfg.Unit = unit;
        _cfg.Save();
        UpdateWeightColumnHeaders();   // 表头单位
        RefreshGrid();      // 列表重量列
        RefreshStats();     // 统计卡片与今日汇总
        OnWeightUpdated();  // 顶部大屏实时重量与颜色
        Log.Info($"显示单位已切换为：{(unit == "t" ? "吨" : "kg")}");
    }

    /// <summary>刷新称重记录列表（按选择的显示条数，新记录在前；未完成行标红提醒）。</summary>
    private void RefreshGrid()
    {
        // "今日全部"=显示今天全部记录（不限条数）；其余档位只取前 N 条
        IEnumerable<WeighRecord> records = _cboPageSize.Text == TodayAllPageSizeText
            ? _recordRepo.Query(DateTime.Today, DateTime.Today, "", "", "", "")
            : _recordRepo.Query(null, null, "", "", "", "").Take(SelectedPageSize());
        _captureGrid.Rows.Clear();
        foreach (var r in records)
        {
            // 弹窗录入的扩展字段（地主/司机/货主）存 ext_json 固定键
            var ext = r.ParseExt();
            int i = _captureGrid.Rows.Add(
                r.SerialNo,
                r.FirstTime?.ToString("yyyy-MM-dd") ?? "",   // 日期只显日期（时间在毛/皮重时间列）
                r.VehicleNo,
                r.Goods ?? "",
                ext.GetValueOrDefault("地主姓名", ""),
                ext.GetValueOrDefault("地主电话", ""),
                ext.GetValueOrDefault("司机电话", ""),
                ext.GetValueOrDefault("货主姓名", ""),
                ext.GetValueOrDefault("货主电话", ""),
                ext.GetValueOrDefault("单价", ""),
                // 毛/皮/净：单位在表头，单元格只显示换算后的数字
                _cfg.FormatWeightNumber(r.GrossKg ?? 0),
                r.TareKg.HasValue ? _cfg.FormatWeightNumber(r.TareKg.Value) : "--",
                r.NetKg.HasValue ? _cfg.FormatWeightNumber(r.NetKg.Value) : "--",
                r.AmountYuan?.ToString("0.##") ?? "--",
                r.FirstTime?.ToString("HH:mm:ss") ?? "",
                r.SecondTime?.ToString("HH:mm:ss") ?? "",
                r.Operator ?? "",
                r.Status);
            _captureGrid.Rows[i].Tag = r;

            // 未完成（待二次磅）→ 整行标红提醒
            if (r.Status == "未完成")
            {
                _captureGrid.Rows[i].DefaultCellStyle.BackColor = Color.MistyRose;
                _captureGrid.Rows[i].DefaultCellStyle.ForeColor = Color.Firebrick;
            }
        }

        FitCaptureColumns();   // 填充后按实际内容/表头适配列宽（不足出横向滚动条）
    }

    /// <summary>当前条数档位对应的条数（非数字档位如「今日全部」回退默认 20）。</summary>
    private int SelectedPageSize() =>
        int.TryParse(_cboPageSize.Text, out int n) && n > 0 ? n : 20;

    // ---------- 记录行右键菜单（对应参考版式：当日记录 / 打印预览 / 过磅视图） ----------
    /// <summary>为称重记录列表挂右键菜单：右键先选中该行，再按菜单项操作。</summary>
    private void BuildRecordContextMenu()
    {
        _captureMenu = new ContextMenuStrip();
        _captureMenu.Items.Add("当日记录", ShortcutIcon.Draw("daily", 16), (_, _) => OpenDailyRecords());
        _captureMenu.Items.Add("打印预览", ShortcutIcon.Draw("printer", 16), (_, _) => PrintPreviewSelected());
        _captureMenu.Items.Add("过磅视图", ShortcutIcon.Draw("camera", 16), (_, _) => ShowSelectedCaptures());
        _captureGrid.ContextMenuStrip = _captureMenu;

        // 右键即选中光标所在行（菜单动作作用于该行）；右键空白处则取消选中
        _captureGrid.CellMouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Right) return;
            _captureGrid.ClearSelection();
            if (e.RowIndex < 0) return;
            _captureGrid.CurrentCell = _captureGrid.Rows[e.RowIndex].Cells[0];
            _captureGrid.Rows[e.RowIndex].Selected = true;
        };
    }

    /// <summary>当前选中的记录（未选中返回 null）。</summary>
    private WeighRecord? SelectedRecord => _captureGrid.CurrentRow?.Tag as WeighRecord;

    /// <summary>右键-当日记录：打开记录查询页并只看该记录当天的全部记录。</summary>
    private void OpenDailyRecords()
    {
        if (SelectedRecord is not { } r) { WarnNoRecordSelected(); return; }
        using var f = new RecordQueryForm(r.FirstTime?.Date ?? DateTime.Today);
        f.ShowDialog(this);
    }

    /// <summary>右键-打印预览：预览该条记录的磅单（不耗纸）。</summary>
    private void PrintPreviewSelected()
    {
        if (SelectedRecord is not { } r) { WarnNoRecordSelected(); return; }
        Report.PoundTicketPrinter.PrintPreview(r);
    }

    /// <summary>右键-过磅视图：查看该次过磅的抓拍存证图片（对应 PRD F7-03）。</summary>
    private void ShowSelectedCaptures()
    {
        if (SelectedRecord is not { } r) { WarnNoRecordSelected(); return; }
        var files = Video.CameraManager.FindCaptures(r.SerialNo);
        if (files.Count == 0)
        {
            MessageBox.Show($"记录 {r.SerialNo} 没有抓拍图片\n（抓拍图片保存在 exe 目录 captures/ 下）",
                "抓拍查询", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        using var f = new CaptureViewForm(r.SerialNo, files);
        f.ShowDialog(this);
    }

    private static void WarnNoRecordSelected() =>
        MessageBox.Show("请先在称重记录上点击右键选择一条记录", "提示",
            MessageBoxButtons.OK, MessageBoxIcon.Warning);

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
        _menu = new MenuStrip();

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
            // 综合设置（对应目标软件"综合设置 [F3]"：基本/串口/联网/视频设置；限管理员）
            mSys.DropDownItems.Add(new ToolStripMenuItem("综合设置", null, (_, _) => OpenComprehensiveSettings()));
            // 收费设置（按重量分段固定金额，参考行业"代磅收费"版式；限管理员）
            mSys.DropDownItems.Add(new ToolStripMenuItem("收费设置", null, (_, _) =>
            {
                using var f = new FeeSettingsDialog();
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
        _menu.Items.Add(mSys);

        // 记录菜单：记录查询（对应 PRD 5.3 / F3-06、F3-07）+ 黑匣子记录
        var mRecord = new ToolStripMenuItem("记录(&R)");
        mRecord.DropDownItems.Add(new ToolStripMenuItem("记录查询", null, (_, _) =>
        {
            using var f = new RecordQueryForm();
            f.ShowDialog(this);
        }));
        mRecord.DropDownItems.Add(new ToolStripMenuItem("黑匣子记录", null, (_, _) =>
        {
            using var f = new BlackBoxForm();
            f.ShowDialog(this);
        }));
        _menu.Items.Add(mRecord);

        // 报表菜单：报表统计（对应 PRD 5.4 / F4-01~F4-05）
        var mReport = new ToolStripMenuItem("报表(&B)");
        mReport.DropDownItems.Add(new ToolStripMenuItem("报表统计", null, (_, _) =>
        {
            using var f = new ReportForm();
            f.ShowDialog(this);
        }));
        _menu.Items.Add(mReport);

        // 资料菜单：基础资料管理（对应 PRD 5.9 / F9-01~F9-05）
        var mBase = new ToolStripMenuItem("资料(&D)");
        mBase.DropDownItems.Add(new ToolStripMenuItem("基础资料管理", null, (_, _) =>
        {
            using var f = new BaseInfoForm();
            f.ShowDialog(this);
        }));
        _menu.Items.Add(mBase);

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
        _menu.Items.Add(mVideo);

        mSource.DropDownItems.Add(new ToolStripSeparator());
        mSource.DropDownItems.Add(new ToolStripMenuItem("退出", null,
            (_, _) => Close()));

        _menu.Items.Add(mSource);
        MainMenuStrip = _menu;
        Controls.Add(_menu);
    }

    // ---------- 综合设置（对应目标软件"综合设置 [F3]"，限管理员） ----------

    /// <summary>打开综合设置：串口/摄像头在页签内保存后立即按新配置重连。</summary>
    private void OpenComprehensiveSettings()
    {
        if (AppSession.CurrentUser?.IsAdmin != true)
        {
            MessageBox.Show("综合设置需要管理员权限。", "提示",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        using var f = new ComprehensiveSettingsDialog();
        f.SerialSaved += () =>
        {
            _manager.RestartSerialSource();   // 保存后立即用新参数重连串口，无需重启软件
            OnWeightUpdated();                // 刷新状态栏数据源描述与通讯指示灯
        };
        f.CamerasSaved += RestartCameras;
        if (f.ShowDialog(this) == DialogResult.OK)
            _blackBox?.Reload();              // 黑匣子新参数（基本设置页）立即生效
    }

    // ---------- 闲置锁屏（综合设置-基本设置：电脑闲置 X 分钟屏幕锁定） ----------

    /// <summary>Win32 最后输入时间（用于闲置判定）。</summary>
    [System.Runtime.InteropServices.StructLayout(
        System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(
        System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

    /// <summary>当前闲置秒数（键盘/鼠标无输入的持续时长）。</summary>
    private static double IdleSeconds()
    {
        var info = new LASTINPUTINFO { cbSize = (uint)System.Runtime.InteropServices
            .Marshal.SizeOf<LASTINPUTINFO>() };
        if (!GetLastInputInfo(ref info)) return 0;
        return Math.Max(0, Environment.TickCount - unchecked((int)info.dwTime)) / 1000.0;
    }

    /// <summary>每 30 秒检查闲置时长，达到设定分钟数弹出锁屏（解锁需当前账号密码）。</summary>
    private bool _lockShown;   // 锁屏弹窗期间避免重复触发

    private void CheckIdleLock()
    {
        int minutes = AppConfig.Current.IdleLockMinutes;
        if (minutes <= 0 || _lockShown) return;
        if (IdleSeconds() < minutes * 60) return;

        _lockShown = true;
        try
        {
            using var lockForm = new ScreenLockForm();
            lockForm.ShowDialog(this);
        }
        finally
        {
            _lockShown = false;
        }
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
            CheckIdleLock();   // 闲置锁屏检查（综合设置-基本设置）
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
        // 综合设置开启"数据稳定 X 秒"时，稳定持续满该秒数（绿灯亮、方可保存）才显示稳定
        if (_manager.IsOverload)
        {
            _lblWeight.ForeColor = Color.Red;
            _lblStable.Text = "● 超载";
            _lblStable.ForeColor = Color.Red;
        }
        else if (_manager.IsStableForSave)
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
    /// 刷新通讯状态指示（稳定指示右侧）：
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
