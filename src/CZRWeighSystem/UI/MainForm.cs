using CZRWeighSystem.Core;

namespace CZRWeighSystem.UI;

/// <summary>
/// 称重主界面（对应 PRD 5.2 称重主界面 / 7.1 主界面布局）。
/// 布局采用纯代码方式构建，包含：重量显示区、信息录入区、
/// 一次磅未完成列表、操作按钮区、底部采集信息列表 + 视频监控内嵌画面、状态栏。
/// </summary>
public class MainForm : Form
{
    private readonly WeighManager _manager = new();
    private readonly AppConfig _cfg = AppConfig.Current;

    // ===== 控件 =====
    private Label _lblWeight = null!;        // 大字实时重量
    private Label _lblStable = null!;        // 稳定/动态指示
    private Label _lblComm = null!;          // 通讯状态指示（绿=正常，红=未通讯/中断）
    private Label _lblGross = null!;         // 毛重
    private Label _lblTare = null!;          // 皮重
    private Label _lblNet = null!;           // 净重
    private readonly List<GroupBox> _weightBoxes = [];   // 毛/皮/净三栏（随窗口铺开）
    private TextBox _txtVehicle = null!;     // 车号（弹窗软键盘选择，只读）
    private TextBox _txtDeduct = null!;      // 扣重
    private TextBox _txtGoods = null!;       // 货物
    private TextBox _txtRemark = null!;      // 备注
    private TextBox _txtOperator = null!;    // 司磅员
    private Label _lblTareHint = null!;      // 车号档案默认皮重提示
    private Button _btnVehicle = null!;      // 选择车牌按钮
    private DataGridView _grid = null!;      // 未完成一次磅列表
    private Button _btnSave = null!;         // 保存（智能判断一次/二次磅）
    private StatusStrip _status = null!;
    private ToolStripStatusLabel _tsslSource = null!;
    private ToolStripStatusLabel _tsslState = null!;
    private ToolStripStatusLabel _tsslTime = null!;

    // ===== 视频监控（首页内嵌预览；大屏弹窗见菜单「视频监控」；抓拍由保存称重触发）=====
    // 摄像头设置保存后整体重建，故不可 readonly
    private Video.CameraManager _cameras = new();
    private GroupBox _videoPanel = null!;                                // 首页右下角内嵌预览区
    private readonly List<(Video.ICameraSource Src, Action Handler)> _videoBindings = [];
    private readonly List<(Video.ICameraSource Src, PictureBox Pic, Label Hint)> _videoTiles = [];
    private readonly System.Windows.Forms.Timer _videoStallTimer = new() { Interval = 2000 };
    private DateTime _videoBoundUtc = DateTime.UtcNow;   // 本批画面的绑定时间（判断"从未出过画面"）
    private static readonly TimeSpan VideoStallTimeout = TimeSpan.FromSeconds(30);

    // ===== 采集信息列表（首页左下角，展示最近称重记录）=====
    private GroupBox _capturePanel = null!;
    private DataGridView _captureGrid = null!;
    private readonly Database.WeighRecordRepository _recordRepo = new();

    // ===== 界面构建 =====
    public MainForm()
    {
        Text = $"CZR 智能称重管理系统 V0.1 - 磅号 {_cfg.ScaleNo} - {AppSession.DisplayName}";
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(1380, 860);
        MinimumSize = new Size(1200, 760);
        Font = new Font("Microsoft YaHei UI", 10F);
        // 高 DPI（125%/150%）下按 DPI 缩放布局，避免文字截断与控件错位
        AutoScaleMode = AutoScaleMode.Dpi;

        BuildMenu();
        BuildWeightPanel();
        BuildInputPanel();
        BuildGrid();
        BuildButtons();
        BuildCapturePanel();
        BuildVideoPanel();
        BuildStatusBar();

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
            RefreshCaptureList();
            SetState("就绪", true);

            // 启动摄像头取流（首页内嵌预览 + 保存称重时自动抓拍用）
            if (_cfg.CameraEnabled)
            {
                _cameras.Start();
            }
            FillVideoPanel();   // 摄像头源就绪后绑定首页预览画面（未启用时显示提示）

            // 启动时及每 6 小时检查一次自动备份（对应 PRD F11-02）
            Utils.BackupService.AutoBackupIfNeeded();
            var backupTimer = new System.Windows.Forms.Timer { Interval = 6 * 60 * 60 * 1000 };
            backupTimer.Tick += (_, _) => Utils.BackupService.AutoBackupIfNeeded();
            backupTimer.Start();

            // 每小时清理一次超期未配对的一次磅（有删除时刷新未完成列表）
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

    // ---------- 采集信息列表面板（底部左半栏，与信息录入区对齐）----------
    private void BuildCapturePanel()
    {
        _capturePanel = new GroupBox
        {
            Text = "采集信息（最近称重记录）",
            Location = new Point(20, _videoY),
            // 底部右半栏是视频监控区，故只固定 880 宽不向右拉伸
            Size = new Size(880, _videoH),
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left,
            MinimumSize = new Size(400, 140),
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
        _captureGrid.Columns.Add("Time", "时间");
        _captureGrid.Columns.Add("VehicleNo", "车号");
        _captureGrid.Columns.Add("Goods", "货物");
        _captureGrid.Columns.Add("Gross", "毛重");
        _captureGrid.Columns.Add("Tare", "皮重");
        _captureGrid.Columns.Add("Net", "净重");
        _captureGrid.Columns.Add("Status", "状态");

        _capturePanel.Controls.Add(_captureGrid);
        Controls.Add(_capturePanel);
    }

    /// <summary>刷新采集信息列表（最近 100 条称重记录，新记录在前）。</summary>
    private void RefreshCaptureList()
    {
        var list = _recordRepo.Query(null, null, "", "", "", "");
        _captureGrid.Rows.Clear();
        foreach (var r in list.Take(100))
        {
            _captureGrid.Rows.Add(
                r.SerialNo,
                r.FirstTime?.ToString("MM-dd HH:mm") ?? "",
                r.VehicleNo,
                r.Goods ?? "",
                _cfg.FormatWeight(r.GrossKg ?? 0),
                r.TareKg.HasValue ? _cfg.FormatWeight(r.TareKg.Value) : "--",
                r.NetKg.HasValue ? _cfg.FormatWeight(r.NetKg.Value) : "--",
                r.Status);
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

    // ---------- 视频监控面板（底部右半栏，内嵌实时预览；大屏弹窗见菜单「视频监控」）----------
    private void BuildVideoPanel()
    {
        _videoPanel = new GroupBox
        {
            Text = "视频监控",
            Location = new Point(915, _videoY),   // 与未完成列表同列对齐
            Size = new Size(435, _videoH),
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            MinimumSize = new Size(280, 140),
        };
        FillVideoPanel();   // 构造时摄像头尚未启动，先显示提示；Load 后再填充真实画面
        _videoStallTimer.Tick += (_, _) => CheckVideoStall();   // 定时检查各路是否长时间无画面
        Controls.Add(_videoPanel);
    }

    /// <summary>
    /// 按当前摄像头源重建首页内嵌预览（启动及摄像头设置保存后调用）。
    /// 网格分布见 VideoGridSize；与保存抓拍、大屏弹窗共享同一批取流源。
    /// </summary>
    private void FillVideoPanel()
    {
        UnbindVideo();
        while (_videoPanel.Controls.Count > 0)
            _videoPanel.Controls[0].Dispose();

        if (!_cfg.CameraEnabled || _cameras.Sources.Count == 0)
        {
            _videoPanel.Controls.Add(new Label
            {
                Text = "视频监控未启用或未配置摄像头。\r\n请用管理员账号在菜单「视频 → 摄像头设置」中添加。",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Color.Gray,
            });
            return;
        }

        var sources = _cameras.Sources;
        var (cols, rows) = VideoGridSize(sources.Count);
        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = cols,
            RowCount = rows,
        };
        for (int i = 0; i < cols; i++)
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / cols));
        for (int i = 0; i < rows; i++)
            table.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / rows));

        for (int i = 0; i < sources.Count; i++)
        {
            var src = sources[i];
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
            table.Controls.Add(pic);
        }
        _videoPanel.Controls.Add(table);

        _videoBoundUtc = DateTime.UtcNow;
        _videoStallTimer.Start();
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

    /// <summary>
    /// 首页视频区网格分布：1~2 路纵向铺满（窄长区域单路画面更宽），
    /// 3 路起改为两列网格，避免每格过扁看不清画面。
    /// </summary>
    internal static (int Cols, int Rows) VideoGridSize(int count) =>
        count <= 2 ? (1, Math.Max(1, count)) : (2, (count + 1) / 2);

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
        FillVideoPanel();   // 首页内嵌预览按新源重建
        SetState($"视频监控：{_cfg.Cameras.Count} 路", true);
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
            f.ShowDialog(this);
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

    // ---------- 重量显示区 ----------
    private void BuildWeightPanel()
    {
        _lblWeight = new Label
        {
            Text = "0 " + _cfg.Unit,
            Font = new Font("Consolas", 56F, FontStyle.Bold),
            ForeColor = Color.OrangeRed,
            TextAlign = ContentAlignment.MiddleCenter,
            Location = new Point(20, 45),
            Size = new Size(720, 130),      // 不加高，横向加长显示
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.Black,
        };

        _lblStable = new Label
        {
            Text = "● 动态",
            Font = new Font("Microsoft YaHei UI", 14F, FontStyle.Bold),
            ForeColor = Color.Red,
            Location = new Point(20, 180),
            Size = new Size(160, 34),
            TextAlign = ContentAlignment.MiddleLeft,
        };

        // 通讯状态指示（稳定指示右侧）：启动时未通讯显示红色，收到仪表数据后变绿
        _lblComm = new Label
        {
            Text = "● 通讯",
            Font = new Font("Microsoft YaHei UI", 14F, FontStyle.Bold),
            ForeColor = Color.Red,
            Location = new Point(190, 180),
            Size = new Size(160, 34),
            TextAlign = ContentAlignment.MiddleLeft,
        };

        // 车/皮/净三栏（对应 PRD 5.2 界面区域 1）
        // 注意：不能用 Left+Right 锚定——窗口变宽时三栏各自拉伸会互相遮盖，
        // 改为固定锚定 + Resize 时在重量框右侧均匀铺开（见 LayoutWeightBoxes）
        string[] names = { "毛重", "皮重", "净重" };
        Label[] targets = new Label[3];
        for (int i = 0; i < 3; i++)
        {
            var box = new GroupBox
            {
                Text = names[i],
                Location = new Point(600 + i * 250, 45),
                Size = new Size(240, 110),
                Anchor = AnchorStyles.Top | AnchorStyles.Left,
            };
            var val = new Label
            {
                Text = "--",
                Font = new Font("Consolas", 26F, FontStyle.Bold),
                ForeColor = Color.FromArgb(0, 102, 204),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                // 数字过长时自动缩小字体显示，避免被裁剪
                AutoEllipsis = true,
            };
            box.Controls.Add(val);
            Controls.Add(box);
            targets[i] = val;
            _weightBoxes.Add(box);
        }
        _lblGross = targets[0];
        _lblTare = targets[1];
        _lblNet = targets[2];

        Controls.Add(_lblWeight);
        Controls.Add(_lblStable);
        Controls.Add(_lblComm);

        // 窗口尺寸变化时重新铺开三栏；
        // DPI 缩放发生在句柄创建之后，缩放过程会覆盖重排结果，
        // 因此还要在句柄创建完成后（消息队列空闲时）再重排一次
        Resize += (_, _) => LayoutWeightBoxes();
        HandleCreated += (_, _) => BeginInvoke(new MethodInvoker(LayoutWeightBoxes));
    }

    /// <summary>把毛/皮/净三栏均匀铺开在重量显示框右侧（避免锚定拉伸互相遮盖）。</summary>
    private void LayoutWeightBoxes()
    {
        if (_weightBoxes.Count == 0) return;
        int start = _lblWeight.Right + 24;
        int end = ClientSize.Width - 20;
        int gap = 10;
        int w = (end - start - (_weightBoxes.Count - 1) * gap) / _weightBoxes.Count;
        if (w < 80) return;             // 窗口过窄时不再重排，避免出现负宽
        for (int i = 0; i < _weightBoxes.Count; i++)
            _weightBoxes[i].SetBounds(
                start + i * (w + gap), _weightBoxes[i].Top, w, _weightBoxes[i].Height);
    }

    // ---------- 信息录入区 ----------
    private void BuildInputPanel()
    {
        var panel = new GroupBox
        {
            Text = "信息录入（回车跳转下一项）",
            Location = new Point(20, 225),
            // 只固定宽度、不向右拉伸：右侧 x=915 起是未完成列表，
            // 若用 Right 锚定，窗口拉宽时录入区会向右生长盖住列表
            Size = new Size(880, 190),
            Anchor = AnchorStyles.Top | AnchorStyles.Left,
        };

        Label L(string text, int x, int y) => new Label
        { Text = text, AutoSize = true, Location = new Point(x, y) };

        // 两列对称布局：左列 标签x=25 输入x=110 宽290；右列 标签x=450 输入x=535 宽320
        const int lx = 25, lInX = 110, lInW = 290;
        const int rx = 450, rInX = 535, rInW = 320;
        static int Row(int row) => 32 + row * 38;

        // ---- 行1：车号（只读，点按钮弹软键盘选牌）｜ 扣重 ----
        panel.Controls.Add(L("车号：", lx, Row(0) + 3));
        _txtVehicle = NewTextBox(lInX, Row(0), 240);
        _txtVehicle.ReadOnly = true;
        _txtVehicle.TextChanged += OnVehicleTextChanged;   // 车号变化（选牌/清空）：失效旧选中并刷新保存按钮模式
        var btnVehicle = _btnVehicle = new Button
        {
            Text = "选择车牌", Location = new Point(lInX + 248, Row(0) - 1), Size = new Size(92, 30),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(0, 122, 204), ForeColor = Color.White,
        };
        btnVehicle.Click += (_, _) => SelectVehicle();

        panel.Controls.Add(L($"扣重({_cfg.Unit})：", rx, Row(0) + 3));
        _txtDeduct = NewTextBox(rInX, Row(0), rInW);

        // ---- 行2：货物 ｜ 司磅员 ----
        panel.Controls.Add(L("货物：", lx, Row(1) + 3));
        _txtGoods = NewTextBox(lInX, Row(1), lInW);
        panel.Controls.Add(L("司磅员：", rx, Row(1) + 3));
        _txtOperator = NewTextBox(rInX, Row(1), rInW);
        _txtOperator.Text = AppSession.DisplayName;   // 司磅员=当前登录用户，自动记录
        _txtOperator.ReadOnly = true;                  // 不允许手工修改

        // ---- 行3：备注 ｜ 车号相关提示（待配对）----
        panel.Controls.Add(L("备注：", lx, Row(2) + 3));
        _txtRemark = NewTextBox(lInX, Row(2), lInW);
        _lblTareHint = new Label
        {
            Text = "",
            AutoSize = true,
            Location = new Point(rInX, Row(2) + 4),
            ForeColor = Color.FromArgb(0, 102, 204),
        };

        panel.Controls.AddRange(new Control[] {
            _txtVehicle, btnVehicle, _txtDeduct,
            _txtGoods, _txtOperator, _txtRemark, _lblTareHint,
        });

        // 回车键流转录入（对应 PRD F2-05）：
        // 扣重→货物→备注→保存（智能判断一次/二次磅）
        Control[] flow = { _txtDeduct, _txtGoods, _txtRemark };
        for (int i = 0; i < flow.Length; i++)
        {
            int idx = i;
            flow[i].PreviewKeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) e.IsInputKey = true; };
            flow[i].KeyDown += (_, e) =>
            {
                if (e.KeyCode != Keys.Enter) return;
                e.SuppressKeyPress = true;
                if (idx < flow.Length - 1)
                    flow[idx + 1].Focus();
                else
                    Save();          // 最后一项回车直接保存（智能判断一次/二次磅）
            };
        }

        // 自定义字段（自定义表单）：从第 4 行起，延续两列对称布局
        var fields = _fieldRepo.GetAll(enabledOnly: true);
        int row = 3;
        for (int i = 0; i < fields.Count; i++)
        {
            var def = fields[i];
            bool left = i % 2 == 0;
            int x = left ? lx : rx;
            int inX = left ? lInX : rInX;
            int inW = left ? lInW : rInW;
            int y = Row(row);

            var lbl = new Label
            { Text = def.Name + "：", AutoSize = true, Location = new Point(x, y + 3) };
            Control input = def.FieldType switch
            {
                "下拉" => NewCombo(inX, y, inW, def.OptionList),
                "日期" => new DateTimePicker
                {
                    Location = new Point(inX, y),
                    Size = new Size(inW, 28),
                    Format = DateTimePickerFormat.Custom,
                    CustomFormat = "yyyy-MM-dd",
                },
                _ => new TextBox { Location = new Point(inX, y), Size = new Size(inW, 28) },
            };
            panel.Controls.AddRange([lbl, input]);
            _customInputs.Add((def, input));
            if (!left) row++;              // 右列放完才换行
        }
        if (fields.Count % 2 == 1) row++;  // 末行单字段独占一行

        // 布局参数随录入面板高度顺移：按钮行、列表高度、底部区域（采集信息 + 视频监控）
        // （底部区域底边保持 790 不变，行数越多越矮，最少保 140）
        int rowsUsed = fields.Count > 0 ? row : 3;
        _inputPanelH = Row(rowsUsed - 1) + 44;
        _buttonsY = 225 + _inputPanelH + 30;
        _gridH = _buttonsY + 60 - 225;
        _videoY = _buttonsY + 75;
        _videoH = Math.Max(140, 790 - _videoY);

        panel.Size = new Size(880, _inputPanelH);
        Controls.Add(panel);
    }

    /// <summary>弹出车牌软键盘，确认后回填车号并刷新档案提示。</summary>
    private void SelectVehicle()
    {
        using var f = new VehiclePadDialog();
        if (f.ShowDialog(this) == DialogResult.OK && f.ResultVehicle != null)
        {
            _txtVehicle.Text = f.ResultVehicle;
            OnVehicleLeft();     // 未完成配对提示
        }
    }

    private static TextBox NewTextBox(int x, int y, int w) => new()
    { Location = new Point(x, y), Size = new Size(w, 28) };

    private static ComboBox NewCombo(int x, int y, int w, params string[] items)
    {
        var cbo = new ComboBox
        {
            Location = new Point(x, y),
            Size = new Size(w, 28),
            DropDownStyle = ComboBoxStyle.DropDown,   // 可输入可下拉
            AutoCompleteMode = AutoCompleteMode.SuggestAppend,
            AutoCompleteSource = AutoCompleteSource.ListItems,
        };
        cbo.Items.AddRange(items);
        return cbo;
    }

    /// <summary>车号输入完成后：同车号已有未完成记录则提示配对（车辆档案已移除，不再带出默认皮重）。</summary>
    private void OnVehicleLeft()
    {
        string no = _txtVehicle.Text.Trim();
        if (no.Length == 0) { _lblTareHint.Text = ""; return; }

        // 未完成列表配对提示（自动模式下保存按钮已自动选中该记录）
        var hit = _manager.GetUnfinishedList()
            .FirstOrDefault(r => r.VehicleNo == no);
        _lblTareHint.Text = hit != null
            ? $"[{no}] 有未完成的二次磅，保存时将自动配对结算"
            : "";
    }

    // ---------- 基础档案数据源 ----------

    private readonly Database.CustomFieldRepository _fieldRepo = new();

    // ===== 自定义字段（自定义表单）=====
    private readonly List<(CustomField Def, Control Input)> _customInputs = [];

    // 录入区高度随自定义字段行数变化，按钮/列表/底部区域随之顺移
    private int _inputPanelH = 190;     // 录入面板高度
    private int _buttonsY = 445;        // 操作按钮行 y
    private int _gridH = 280;           // 未完成列表高度（底边与按钮行对齐）
    private int _videoY = 520;          // 底部区域（采集信息 + 视频监控）y
    private int _videoH = 270;          // 底部区域高度（底边保持 790）

    // ---------- 未完成列表 ----------
    private void BuildGrid()
    {
        var panel = new GroupBox
        {
            Text = "一次磅未完成列表（双击行配对）",
            Location = new Point(915, 225),   // 与左侧信息录入区顶边对齐
            // 高度与操作按钮行底边对齐（随自定义字段行数顺移）；下方是采集信息区，
            // 不能用 Bottom 锚定——窗口变高时列表会向下生长压住列表画面
            Size = new Size(435, _gridH),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
        };

        _grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            // 表头/行高随字体自适应：窗体用雅黑 10pt 且高 DPI 缩放，
            // 默认固定表头高度会导致中文字被截成一半
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize,
            AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCellsExceptHeaders,
            BackgroundColor = Color.White,
            BorderStyle = BorderStyle.None,
        };
        _grid.Columns.Add("VehicleNo", "车号");
        _grid.Columns.Add("Weight", "重量");
        _grid.Columns.Add("Time", "时间");
        _grid.Columns.Add("Goods", "货物");
        _grid.CellDoubleClick += (_, _) => SaveSecond();
        _grid.SelectionChanged += (_, _) => RefreshWeightBoxes();

        panel.Controls.Add(_grid);
        Controls.Add(panel);
    }

    // ---------- 操作按钮 ----------
    private void BuildButtons()
    {
        int y = _buttonsY;
        _btnSave = NewButton("保存", 20, y, 260, 60, Color.FromArgb(0, 122, 204));

        // 保存类型全自动判断：车号匹配到未完成记录 → 二次磅结算，否则一次磅
        // （配对兜底：双击右侧未完成列表行直接结算）
        var btnClear = NewButton("清空录入", 300, y, 120, 60, Color.Gray);

        _btnSave.Click += (_, _) => Save();
        btnClear.Click += (_, _) => ClearInput();

        Controls.Add(_btnSave);
        Controls.Add(btnClear);
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

    // ================================================================
    // 业务动作
    // ================================================================

    /// <summary>保存前二次确认（可通过 config.json ConfirmBeforeSave 关闭，对应 PRD 7.2-3）。</summary>
    private bool ConfirmSave(string action, string detail)
    {
        if (!AppConfig.Current.ConfirmBeforeSave) return true;
        return MessageBox.Show($"{action}\n{detail}", "确认操作",
            MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
    }

    // ===== 保存模式（单按钮自动判断：车号匹配到未完成记录 → 二次磅，否则一次磅）=====

    /// <summary>按录入车号在未完成列表中查找并选中匹配记录；返回是否命中。</summary>
    private bool AutoSelectByVehicle()
    {
        string no = _txtVehicle.Text.Trim();
        if (no.Length == 0) return false;
        foreach (DataGridViewRow row in _grid.Rows)
        {
            if (row.Tag is WeighRecord r && r.VehicleNo == no)
            {
                _grid.ClearSelection();
                row.Selected = true;
                _grid.CurrentCell = row.Cells[0];
                return true;
            }
        }
        return false;
    }

    /// <summary>本次是否为二次磅：已选中未完成记录、或录入车号能匹配到未完成记录。</summary>
    private bool IsSecondWeigh =>
        _grid.CurrentRow?.Tag is WeighRecord || AutoSelectByVehicle();

    /// <summary>车号输入变化：先失效与新车号不符的旧选中，再刷新保存按钮模式。
    /// 否则换输另一辆车时，列表里残留的选中行（CurrentRow）会把按钮一直钉在"二次磅"，
    /// 导致新车无法录一次磅。</summary>
    private void OnVehicleTextChanged(object? sender, EventArgs e)
    {
        if (_grid.CurrentRow?.Tag is WeighRecord r &&
            r.VehicleNo != _txtVehicle.Text.Trim())
        {
            _grid.CurrentCell = null;   // 同时清除当前行与选中（ClearSelection 不重置 CurrentRow）
        }
        UpdateSaveButton();   // 内部按新车号自动重新配对：匹配到未完成记录则重新进入二次磅模式
    }

    /// <summary>按自动判断刷新保存按钮文字与颜色，操作员一眼可见本次将执行的操作。</summary>
    private void UpdateSaveButton()
    {
        if (IsSecondWeigh)
        {
            _btnSave.Text = "保存二次磅（结算）";
            _btnSave.BackColor = Color.FromArgb(0, 153, 0);
        }
        else
        {
            _btnSave.Text = "保存";
            _btnSave.BackColor = Color.FromArgb(0, 122, 204);
        }
    }

    /// <summary>统一保存入口：自动判断走一次磅或二次磅。</summary>
    private void Save()
    {
        if (IsSecondWeigh) SaveSecond();
        else SaveFirst();
    }

    /// <summary>保存一次磅。</summary>
    private void SaveFirst()
    {
        double? deduct = ParseDeduct();
        if (_txtDeduct.TextLength > 0 && deduct == null) return;

        // 自定义字段采集与校验（必填/数字格式）
        var ext = CollectExt();
        if (ext == null) return;

        // 保存前二次确认（显示车号与重量）
        string weightText = _cfg.FormatWeight(_manager.StableKg);
        if (!ConfirmSave("确认保存一次磅？",
            $"车号：{_txtVehicle.Text}  重量：{weightText}")) return;

        var (ok, msg, firstRecord) = _manager.SaveFirstWeigh(
            _txtVehicle.Text, _txtGoods.Text, null, null,
            null, null, _txtRemark.Text,
            deduct, _txtOperator.Text, ext);

        MessageBox.Show(msg, ok ? "成功" : "提示",
            MessageBoxButtons.OK, ok ? MessageBoxIcon.Information : MessageBoxIcon.Warning);

        if (ok)
        {
            ClearInput();
            RefreshGrid();

            // 一次磅保存即抓拍留证（对应 PRD F7-02）
            if (firstRecord != null) CaptureFor(firstRecord.SerialNo);
        }
    }

    /// <summary>保存二次磅并结算。</summary>
    private void SaveSecond()
    {
        if (_grid.CurrentRow?.Tag is not WeighRecord first)
        {
            MessageBox.Show("请先在右侧列表中选择一条未完成的一次磅记录", "提示",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        // 车号与选中记录不符时确认
        string no = _txtVehicle.Text.Trim();
        if (no.Length > 0 && no != first.VehicleNo)
        {
            var r = MessageBox.Show(
                $"录入车号 [{no}] 与选中记录 [{first.VehicleNo}] 不一致，\r\n是否仍按选中记录 {first.VehicleNo} 结算？",
                "车号确认", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (r != DialogResult.Yes) return;
        }

        double? deduct = ParseDeduct();
        if (_txtDeduct.TextLength > 0 && deduct == null) return;

        // 保存前二次确认（显示配对车号与两次重量）
        string weightText = _cfg.FormatWeight(_manager.StableKg);
        if (!ConfirmSave("确认保存二次磅并结算？",
            $"车号：{first.VehicleNo}  一次磅：{_cfg.FormatWeight(first.GrossKg ?? 0)}  本次：{weightText}"))
            return;

        var (ok, msg, record) = _manager.SaveSecondWeigh(first, deduct, _txtOperator.Text);
        MessageBox.Show(msg, ok ? "结算成功" : "提示",
            MessageBoxButtons.OK, ok ? MessageBoxIcon.Information : MessageBoxIcon.Warning);

        if (ok)
        {
            ClearInput();
            RefreshGrid();
            RefreshCaptureList();

            // 结算完成自动抓拍留证（对应 PRD F7-02）
            if (record != null) CaptureFor(record.SerialNo);

            // 打印磅单：开启自动打印直接出单，否则询问（对应 PRD F5-01 / F9-04）
            if (record != null)
            {
                if (AppConfig.Current.AutoPrintEnabled ||
                    MessageBox.Show("是否打印磅单？", "打印磅单",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                {
                    Report.PoundTicketPrinter.Print(record);
                }
            }
        }
    }

    /// <summary>解析扣重输入；留空 = 无扣重；非法输入时提示并返回 null。</summary>
    private double? ParseDeduct()
    {
        if (_txtDeduct.TextLength == 0) return null;
        if (!double.TryParse(_txtDeduct.Text, out double v) || v < 0)
        {
            MessageBox.Show("扣重格式不正确（应为不小于 0 的数字）", "提示",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return null;
        }
        // 输入按显示单位换算为千克
        return _cfg.Unit == "t" ? v * 1000 : v;
    }

    /// <summary>
    /// 采集自定义字段值；校验失败（必填缺失/数字格式错误）提示并返回 null。
    /// </summary>
    private Dictionary<string, string>? CollectExt()
    {
        if (_customInputs.Count == 0) return [];
        var ext = new Dictionary<string, string>();
        foreach (var (def, input) in _customInputs)
        {
            string val = input.Text.Trim();
            if (val.Length == 0)
            {
                if (def.Required)
                {
                    MessageBox.Show($"[{def.Name}] 为必填项", "提示",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    input.Focus();
                    return null;
                }
                continue;
            }
            if (def.FieldType == "数字" && !double.TryParse(val, out _))
            {
                MessageBox.Show($"[{def.Name}] 需填写数字", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                input.Focus();
                return null;
            }
            ext[def.Name] = val;
        }
        return ext;
    }

    /// <summary>刷新未完成列表（超时记录标红提醒，对应 PRD 4.1 规则 3）。</summary>
    private void RefreshGrid()
    {
        _grid.Rows.Clear();
        double warnHours = _cfg.UnfinishedWarnHours;
        foreach (var r in _manager.GetUnfinishedList())
        {
            int i = _grid.Rows.Add(
                r.VehicleNo,
                _cfg.FormatWeight(r.GrossKg ?? 0),
                r.FirstTime?.ToString("MM-dd HH:mm") ?? "",
                r.Goods ?? "");
            _grid.Rows[i].Tag = r;

            // 超过阈值未完成二次磅 → 整行标红
            if (r.FirstTime.HasValue &&
                (DateTime.Now - r.FirstTime.Value).TotalHours >= warnHours)
            {
                _grid.Rows[i].DefaultCellStyle.BackColor = Color.MistyRose;
                _grid.Rows[i].DefaultCellStyle.ForeColor = Color.Firebrick;
            }
        }

        // 列表重建后重估保存按钮模式：保存完成后清空车号触发 TextChanged 时，
        // 旧选中行还在列表里会被误判为二次磅模式，导致按钮一直停留
        // "保存二次磅（结算）"，必须按刷新后的新列表状态复位
        UpdateSaveButton();
    }

    /// <summary>清空录入区并把焦点放回车号（对应 PRD F2-05）。</summary>
    private void ClearInput()
    {
        _txtVehicle.Clear();
        _txtDeduct.Clear();
        _txtGoods.Clear();
        _txtRemark.Clear();
        foreach (var (_, input) in _customInputs)
        {
            if (input is DateTimePicker dtp) dtp.Value = DateTime.Today;
            else input.Text = "";
        }
        _lblTareHint.Text = "";
        _btnVehicle.Focus();
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
        RefreshWeightBoxes();
    }

    /// <summary>
    /// 刷新通讯状态指示（稳定指示右侧）：
    /// 尚未通讯过或通讯中断（超时/报错）→ 红色；正常收到仪表数据 → 绿色。
    /// </summary>
    private void UpdateCommIndicator()
    {
        _lblComm.ForeColor = _manager.IsCommAlive ? Color.Green : Color.Red;
    }

    /// <summary>
    /// 毛/皮/净三栏显示（毛重只显示已保存的记录值，不镜像实时重量——
    /// 实时重量仅在大屏显示，保存后毛重栏才会出现记录值）：
    /// 选中未完成记录时 → 毛重=已存一次磅重量，皮重=当前稳定重量，净重=两者差值实时预览
    /// （实际毛/皮按结算时大小值自动分配，此处仅直观展示）；
    /// 未选中时 → 毛/皮/净均显示 "--"。
    /// </summary>
    private void RefreshWeightBoxes()
    {
        if (_grid.CurrentRow?.Tag is WeighRecord first)
        {
            _lblGross.Text = _cfg.FormatWeight(first.GrossKg ?? 0);
            if (_manager.IsStable && _manager.StableKg > 0)
            {
                _lblTare.Text = _cfg.FormatWeight(_manager.StableKg);
                _lblNet.Text = _cfg.FormatWeight(Math.Abs((first.GrossKg ?? 0) - _manager.StableKg));
            }
            else
            {
                _lblTare.Text = "--";
                _lblNet.Text = "--";
            }
        }
        else
        {
            // 未选中记录：毛重栏不跟随实时重量变化（毛重是保存后的结果值）
            _lblGross.Text = "--";
            _lblTare.Text = "--";
            _lblNet.Text = "--";
        }
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
