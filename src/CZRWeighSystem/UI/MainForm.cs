using CZRWeighSystem.Core;

namespace CZRWeighSystem.UI;

/// <summary>
/// 称重主界面（对应 PRD 5.2 称重主界面 / 7.1 主界面布局）。
/// 布局采用纯代码方式构建，包含：重量显示区、信息录入区、
/// 一次磅未完成列表、操作按钮区、状态栏。
/// </summary>
public class MainForm : Form
{
    private readonly WeighManager _manager = new();
    private readonly AppConfig _cfg = AppConfig.Current;

    // ===== 控件 =====
    private Label _lblWeight = null!;        // 大字实时重量
    private Label _lblStable = null!;        // 稳定/动态指示
    private Label _lblGross = null!;         // 毛重
    private Label _lblTare = null!;          // 皮重
    private Label _lblNet = null!;           // 净重
    private readonly List<GroupBox> _weightBoxes = [];   // 毛/皮/净三栏（随窗口铺开）
    private TextBox _txtVehicle = null!;     // 车号
    private ComboBox _cboGoods = null!;      // 货物
    private ComboBox _cboCompany = null!;    // 收/发货单位
    private ComboBox _cboSpec = null!;       // 规格
    private TextBox _txtBatch = null!;       // 批次
    private TextBox _txtRemark = null!;      // 备注
    private TextBox _txtManual = null!;      // 手工补录重量
    private TextBox _txtOperator = null!;    // 司磅员
    private Label _lblTareHint = null!;      // 车号档案默认皮重提示
    private DataGridView _grid = null!;      // 未完成一次磅列表
    private Button _btnFirst = null!;        // 保存一次磅
    private Button _btnSecond = null!;       // 保存二次磅
    private StatusStrip _status = null!;
    private ToolStripStatusLabel _tsslSource = null!;
    private ToolStripStatusLabel _tsslState = null!;
    private ToolStripStatusLabel _tsslTime = null!;

    // ===== 视频监控 =====
    private readonly Video.CameraManager _cameras = new();
    private GroupBox _videoPanel = null!;
    private readonly List<PictureBox> _picBoxes = [];

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
        BuildVideoPanel();
        BuildStatusBar();

        // 订阅业务事件（串口数据来自非 UI 线程，统一转发到 UI 线程）
        _manager.WeightUpdated += () => SafeInvoke(OnWeightUpdated);
        _manager.SourceError += msg => SafeInvoke(() => SetState("数据源异常：" + msg, false));

        Load += (_, _) =>
        {
            _manager.Start();
            RefreshGrid();
            SetState("就绪", true);

            // 启动摄像头预览（对应 PRD F7-01）
            if (_cfg.CameraEnabled)
            {
                _cameras.Start();
                BindCameraFrames();
            }

            // 启动时及每 6 小时检查一次自动备份（对应 PRD F11-02）
            Utils.BackupService.AutoBackupIfNeeded();
            var backupTimer = new System.Windows.Forms.Timer { Interval = 6 * 60 * 60 * 1000 };
            backupTimer.Tick += (_, _) => Utils.BackupService.AutoBackupIfNeeded();
            backupTimer.Start();
        };
        FormClosing += (_, _) =>
        {
            _manager.Dispose();
            _cameras.Dispose();
        };
    }

    // ---------- 视频监控面板（对应 PRD F7-01）----------
    private void BuildVideoPanel()
    {
        _videoPanel = new GroupBox
        {
            Text = "视频监控（保存称重时自动抓拍留证）",
            Location = new Point(20, _videoY),
            Size = new Size(1330, _videoH),
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            // 最小高度：窗口缩到最小时仍保留一屏完整画面（避免标题被截/画面过小）
            MinimumSize = new Size(400, 140),
        };

        // 按配置路数生成预览画面
        int count = Math.Clamp(_cfg.CameraCount, 1, 4);
        int pw = _videoPanel.ClientSize.Width / count - 8;
        for (int i = 0; i < count; i++)
        {
            var pic = new PictureBox
            {
                Location = new Point(8 + i * (pw + 8), 24),
                Size = new Size(pw, _videoPanel.ClientSize.Height - 34),
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Black,
                // 只有最后一路随面板拉伸（吸收宽度变化）；其余各路固定宽度，
                // 否则多路画面全部拉伸会互相重叠
                Anchor = i == count - 1
                    ? AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
                    : AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left,
            };
            _videoPanel.Controls.Add(pic);
            _picBoxes.Add(pic);
        }
        Controls.Add(_videoPanel);
    }

    /// <summary>启动各路画面的刷新订阅（摄像头启动后调用）。</summary>
    private void BindCameraFrames()
    {
        for (int i = 0; i < _cameras.Sources.Count && i < _picBoxes.Count; i++)
        {
            int idx = i;
            var source = _cameras.Sources[idx];
            source.PreviewFrameUpdated += () => SafeInvoke(() =>
            {
                _picBoxes[idx].Image = source.CurrentFrame;
                _picBoxes[idx].Refresh();
            });
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
        // 打印设置（对应 PRD F9-04：纸张、联数、字段显隐、自动打印；限管理员）
        if (AppSession.CurrentUser?.Role.Contains("管理员") == true)
        {
            mSys.DropDownItems.Add(new ToolStripMenuItem("打印设置", null, (_, _) =>
            {
                using var f = new PrintSettingsDialog();
                f.ShowDialog(this);
            }));
        }
        mSys.DropDownItems.Add(new ToolStripSeparator());
        // 数据库设置（对应 PRD F9-05：单机 SQLite / 网络 SQL Server，仅超管可见）
        if (AppSession.CurrentUser?.Role.Contains("超级管理员") == true)
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
            ReloadBaseInfo();       // 关闭后刷新录入区下拉数据源
        }));
        menu.Items.Add(mBase);

        var mSource = new ToolStripMenuItem("数据源(&S)");
        mSource.DropDownItems.Add(new ToolStripMenuItem("串口参数设置", null, (_, _) =>
        {
            using var f = new SerialSettingsDialog();
            f.ShowDialog(this);
        }));

        // 视频菜单（对应 PRD F7-01：预览可隐藏）
        var mVideo = new ToolStripMenuItem("视频(&V)");
        mVideo.DropDownItems.Add(new ToolStripMenuItem("显示/隐藏监控画面", null, (_, _) =>
            _videoPanel.Visible = !_videoPanel.Visible));
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
            Size = new Size(560, 130),
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

        panel.Controls.Add(L("车号：", 20, 35));
        _txtVehicle = NewTextBox(85, 32, 130);
        _txtVehicle.Leave += (_, _) => OnVehicleLeft();
        LoadVehicleAutoComplete();     // 车号自动完成（来自车辆档案）

        panel.Controls.Add(L("货物：", 235, 35));
        _cboGoods = NewCombo(290, 32, 130, LoadGoodsNames());

        panel.Controls.Add(L("单位：", 445, 35));
        _cboCompany = NewCombo(500, 32, 150, LoadCompanyNames());

        panel.Controls.Add(L("规格：", 670, 35));
        _cboSpec = NewCombo(725, 32, 130, LoadSpecNames());

        panel.Controls.Add(L("批次：", 20, 85));
        _txtBatch = NewTextBox(85, 82, 130);

        panel.Controls.Add(L("备注：", 235, 85));
        _txtRemark = NewTextBox(290, 82, 360);

        panel.Controls.Add(L("司磅员：", 670, 85));
        _txtOperator = NewTextBox(745, 82, 110);
        _txtOperator.Text = AppSession.DisplayName;   // 司磅员=当前登录用户，自动记录
        _txtOperator.ReadOnly = true;                  // 不允许手工修改

        panel.Controls.Add(L("补录重量(" + _cfg.Unit + ")：", 20, 135));
        _txtManual = NewTextBox(150, 132, 120);
        _txtManual.TextChanged += (_, _) =>
        {
            // 输入补录重量后按钮文字提示
            bool manual = double.TryParse(_txtManual.Text, out _);
            _btnFirst.Text = manual ? "保存一次磅(补录)" : "保存一次磅";
            _btnSecond.Text = manual ? "保存二次磅(补录)" : "保存二次磅";
        };

        panel.Controls.AddRange(new Control[] {
            _txtVehicle, _cboGoods, _cboCompany, _cboSpec,
            _txtBatch, _txtRemark, _txtOperator, _txtManual
        });

        // 回车键流转录入（对应 PRD F2-05）：
        // 车号→货物→单位→规格→批次→备注→补录重量→保存一次磅
        Control[] flow = { _txtVehicle, _cboGoods, _cboCompany, _cboSpec,
                           _txtBatch, _txtRemark, _txtManual };
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
                    SaveFirst();     // 最后一项回车直接保存一次磅
            };
        }

        // 车号匹配档案时的默认皮重提示
        _lblTareHint = new Label
        {
            Text = "",
            AutoSize = true,
            Location = new Point(290, 139),
            ForeColor = Color.FromArgb(0, 102, 204),
        };
        panel.Controls.Add(_lblTareHint);

        // 自定义字段（自定义表单）：从定义表读取启用字段动态渲染，
        // 每行两列（x=20 / x=445），行高 36；录入面板高度随之扩展
        var fields = _fieldRepo.GetAll(enabledOnly: true);
        int rowY = 178;
        for (int i = 0; i < fields.Count; i++)
        {
            var def = fields[i];
            int x = i % 2 == 0 ? 20 : 445;
            if (i % 2 == 0 && i > 0) rowY += 36;

            var lbl = new Label
            { Text = def.Name + "：", AutoSize = true, Location = new Point(x, rowY + 3) };
            // 用窗体字体测量（Label 未加入控件树时会回落到系统默认字体导致测宽偏小），
            // AutoSize 渲染宽度略大于测量值，再留 6px 余量
            int inputX = x + TextRenderer.MeasureText(lbl.Text, Font).Width + 6;
            int inputW = (i % 2 == 0 ? 430 : 855) - inputX;
            if (inputW < 90) inputW = 90;    // 超长字段名时输入框保底宽度（可能轻微越界，设计上避免过长字段名）

            Control input = def.FieldType switch
            {
                "下拉" => NewCombo(inputX, rowY, inputW, def.OptionList),
                "日期" => new DateTimePicker
                {
                    Location = new Point(inputX, rowY),
                    Size = new Size(inputW, 28),
                    Format = DateTimePickerFormat.Custom,
                    CustomFormat = "yyyy-MM-dd",
                },
                _ => new TextBox { Location = new Point(inputX, rowY), Size = new Size(inputW, 28) },
            };
            panel.Controls.AddRange([lbl, input]);
            _customInputs.Add((def, input));
        }
        if (fields.Count > 0)
            _inputPanelH = rowY + 36 + 8;

        // 布局参数随录入面板高度顺移：按钮行、列表高度、视频区
        // （视频区底边保持 790 不变，行数越多视频区越矮，最少保 140）
        _buttonsY = 225 + _inputPanelH + 30;
        _gridH = _buttonsY + 60 - 225;
        _videoY = _buttonsY + 75;
        _videoH = Math.Max(140, 790 - _videoY);

        panel.Size = new Size(880, _inputPanelH);
        Controls.Add(panel);
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

    /// <summary>车号输入完成后：匹配车辆档案显示默认皮重；同车号已有未完成记录则提示配对。</summary>
    private void OnVehicleLeft()
    {
        string no = _txtVehicle.Text.Trim();
        if (no.Length == 0) { _lblTareHint.Text = ""; return; }

        // 未完成列表配对提示（已有逻辑）
        var hit = _manager.GetUnfinishedList()
            .FirstOrDefault(r => r.VehicleNo == no);
        if (hit != null)
        {
            _lblTareHint.Text = $"[{no}] 有未完成的二次磅，请右侧选中配对";
            return;
        }

        // 车辆档案带出默认皮重（对应 PRD F9-01，为快捷结算做数据准备）
        var v = _baseRepo.GetVehicles().FirstOrDefault(x => x.VehicleNo == no);
        _lblTareHint.Text = v?.DefaultTareKg.HasValue == true
            ? $"档案默认皮重：{_cfg.FormatWeight(v.DefaultTareKg.Value)}"
            : "";
    }

    // ---------- 基础档案数据源（对应 PRD F9：录入区下拉来自档案）----------

    private readonly Database.BaseInfoRepository _baseRepo = new();
    private readonly Database.CustomFieldRepository _fieldRepo = new();

    // ===== 自定义字段（自定义表单）=====
    private readonly List<(CustomField Def, Control Input)> _customInputs = [];

    // 录入区高度随自定义字段行数变化，按钮/列表/视频区随之顺移
    private int _inputPanelH = 190;     // 录入面板高度
    private int _buttonsY = 445;        // 操作按钮行 y
    private int _gridH = 280;           // 未完成列表高度（底边与按钮行对齐）
    private int _videoY = 520;          // 视频区 y
    private int _videoH = 270;          // 视频区高度（底边保持 790）

    /// <summary>车辆档案 → 车号输入框自动完成列表。</summary>
    private void LoadVehicleAutoComplete()
    {
        var list = _baseRepo.GetVehicles(onlyEnabled: true)
            .Select(v => v.VehicleNo).ToArray();
        if (list.Length == 0) return;
        _txtVehicle.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
        _txtVehicle.AutoCompleteSource = AutoCompleteSource.CustomSource;
        _txtVehicle.AutoCompleteCustomSource.AddRange(list);
    }

    /// <summary>货物档案 → 货物下拉；档案为空时保留默认示例项。</summary>
    private string[] LoadGoodsNames()
    {
        var list = _baseRepo.GetGoods(onlyEnabled: true)
            .Select(g => g.Name).ToArray();
        return list.Length > 0 ? list : new[] { "煤炭", "矿粉", "钢材", "砂石" };
    }

    /// <summary>单位档案 → 单位下拉；档案为空时保留默认示例项。</summary>
    private string[] LoadCompanyNames()
    {
        var list = _baseRepo.GetCompanies(onlyEnabled: true)
            .Select(c => c.Name).ToArray();
        return list.Length > 0 ? list : new[] { "供货商A", "收货商B", "临时客户" };
    }

    /// <summary>货物档案规格去重 → 规格下拉。</summary>
    private string[] LoadSpecNames()
    {
        var list = _baseRepo.GetGoods(onlyEnabled: true)
            .Select(g => g.Spec).Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s!).Distinct().ToArray();
        return list.Length > 0 ? list : new[] { "一级", "二级" };
    }

    /// <summary>基础资料变更后刷新录入区下拉与车号自动完成（保留当前已填值）。</summary>
    private void ReloadBaseInfo()
    {
        string goods = _cboGoods.Text, company = _cboCompany.Text,
               spec = _cboSpec.Text, vehicle = _txtVehicle.Text;

        ReloadCombo(_cboGoods, LoadGoodsNames(), goods);
        ReloadCombo(_cboCompany, LoadCompanyNames(), company);
        ReloadCombo(_cboSpec, LoadSpecNames(), spec);
        LoadVehicleAutoComplete();
        _txtVehicle.Text = vehicle;
    }

    /// <summary>重填下拉列表并尽量保留原值。</summary>
    private static void ReloadCombo(ComboBox cbo, string[] items, string keep)
    {
        cbo.Items.Clear();
        cbo.Items.AddRange(items);
        cbo.Text = keep;
    }

    // ---------- 未完成列表 ----------
    private void BuildGrid()
    {
        var panel = new GroupBox
        {
            Text = "一次磅未完成列表（双击行配对）",
            Location = new Point(915, 225),
            // 高度与操作按钮行底边对齐（随自定义字段行数顺移）；下方是视频区，
            // 不能用 Bottom 锚定——窗口变高时列表会向下生长压住视频画面
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
        _btnFirst = NewButton("保存一次磅", 20, y, 200, 60, Color.FromArgb(0, 122, 204));
        _btnSecond = NewButton("保存二次磅", 240, y, 200, 60, Color.FromArgb(0, 153, 0));
        var btnClear = NewButton("清空录入", 460, y, 120, 60, Color.Gray);

        _btnFirst.Click += (_, _) => SaveFirst();
        _btnSecond.Click += (_, _) => SaveSecond();
        btnClear.Click += (_, _) => ClearInput();

        Controls.Add(_btnFirst);
        Controls.Add(_btnSecond);
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

        // 每秒刷新时间显示
        var timer = new System.Windows.Forms.Timer { Interval = 1000 };
        timer.Tick += (_, _) => _tsslTime.Text = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
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

    /// <summary>保存一次磅。</summary>
    private void SaveFirst()
    {
        double? manual = ParseManual();
        if (_txtManual.TextLength > 0 && manual == null) return;

        // 自定义字段采集与校验（必填/数字格式）
        var ext = CollectExt();
        if (ext == null) return;

        // 保存前二次确认（显示车号与重量）
        string weightText = manual.HasValue
            ? _cfg.FormatWeight(manual.Value)
            : _cfg.FormatWeight(_manager.StableKg);
        if (!ConfirmSave("确认保存一次磅？",
            $"车号：{_txtVehicle.Text}  重量：{weightText}")) return;

        var (ok, msg, firstRecord) = _manager.SaveFirstWeigh(
            _txtVehicle.Text, _cboGoods.Text, _cboCompany.Text,
            _cboSpec.Text, _txtBatch.Text, _txtRemark.Text,
            manual, _txtOperator.Text, ext);

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

        double? manual = ParseManual();
        if (_txtManual.TextLength > 0 && manual == null) return;

        // 保存前二次确认（显示配对车号与两次重量）
        string weightText = manual.HasValue
            ? _cfg.FormatWeight(manual.Value)
            : _cfg.FormatWeight(_manager.StableKg);
        if (!ConfirmSave("确认保存二次磅并结算？",
            $"车号：{first.VehicleNo}  一次磅：{_cfg.FormatWeight(first.GrossKg ?? 0)}  本次：{weightText}"))
            return;

        var (ok, msg, record) = _manager.SaveSecondWeigh(first, manual, _txtOperator.Text);
        MessageBox.Show(msg, ok ? "结算成功" : "提示",
            MessageBoxButtons.OK, ok ? MessageBoxIcon.Information : MessageBoxIcon.Warning);

        if (ok)
        {
            ClearInput();
            RefreshGrid();

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

    /// <summary>解析补录重量输入；非法输入时提示并返回 null。</summary>
    private double? ParseManual()
    {
        if (_txtManual.TextLength == 0) return null;
        if (!double.TryParse(_txtManual.Text, out double v) || v <= 0)
        {
            MessageBox.Show("补录重量格式不正确（应为大于 0 的数字）", "提示",
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
    }

    /// <summary>清空录入区并把焦点放回车号（对应 PRD F2-05）。</summary>
    private void ClearInput()
    {
        _txtVehicle.Clear();
        _txtBatch.Clear();
        _txtRemark.Clear();
        _txtManual.Clear();
        foreach (var (_, input) in _customInputs)
        {
            if (input is DateTimePicker dtp) dtp.Value = DateTime.Today;
            else input.Text = "";
        }
        _txtVehicle.Focus();
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
        RefreshWeightBoxes();
    }

    /// <summary>
    /// 毛/皮/净三栏实时预览：
    /// 选中未完成记录时 → 毛重=已存一次磅重量，皮重=当前稳定重量，净重=两者差值实时预览
    /// （实际毛/皮按结算时大小值自动分配，此处仅直观展示）；
    /// 未选中时 → 毛重栏显示当前稳定重量（即将保存的一次磅值），皮/净显示 "--"。
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
            _lblGross.Text = _manager.IsStable && _manager.StableKg > 0
                ? _cfg.FormatWeight(_manager.StableKg)
                : "--";
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
