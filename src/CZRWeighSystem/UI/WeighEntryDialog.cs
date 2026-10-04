using System.Text.RegularExpressions;
using CZRWeighSystem.Core;

namespace CZRWeighSystem.UI;

/// <summary>
/// 称重录入弹窗（仿老软件链式流程：确认 → 保存 → 打印 → 继续）。
/// 点首页「空车去皮 / 重车过磅」弹出，左侧米黄票据按网格两列布局：
/// 左列固定字段（车牌号码/地主姓名/地主电话/司机电话/货主姓名/品种），
/// 右列实时毛/皮/净预览；右栏为四步链式按钮，按顺序逐步解锁：
/// 确认（校验车号/稳定并锁定读数）→ 保存（第一次称重或二次磅结算）→
/// 打印（直接出单，不再弹"是否打印"）→ 继续（清空表单连续录入下一辆，窗口不关闭）。
/// 地主姓名等固定字段以固定键写入记录 ext_json（无需改表，磅单模板可直接引用），
/// 品种存 Goods 列；保存走统一皮/毛流程（车号有待配对记录时自动二次磅结算）。
/// </summary>
public class WeighEntryDialog : Form
{
    private readonly WeighManager _manager;
    private readonly Database.WeighRecordRepository _plateRepo = new();
    private bool _isTare;                    // 可经"切换"按钮在皮/毛模式间转换
    private readonly AppConfig _cfg = AppConfig.Current;

    // ===== 链式流程状态（0=待确认 1=待保存 2=待打印 3=待继续）=====
    private int _step;
    private double? _confirmedKg;            // 确认时锁定的稳定重量（千克）
    private WeighRecord? _confirmedFirst;    // 确认时识别出的待配对一次磅（null=第一次称重）
    private WeighRecord? _savedRecord;       // 保存成功的记录（打印用）

    /// <summary>当天已保存过（未作废）的记录：车牌自动补全候选 + 选中后回填上次信息。</summary>
    private List<WeighRecord> _todayRecords = [];

    // 两种模式的背景色：皮重=蓝、毛重=绿，一眼可辨当前在哪个流程
    private static readonly Color TareBack = Color.FromArgb(0, 102, 164);
    private static readonly Color GrossBack = Color.FromArgb(0, 145, 60);

    // ===== 对话框出口（internal 供测试注入静默实现；正常流程用 MessageBox）=====
    /// <summary>错误/警告提示框。测试注入后记录消息不弹窗。</summary>
    internal Action<string, string, bool> InfoBox = (msg, title, ok) =>
        MessageBox.Show(msg, title, MessageBoxButtons.OK,
            ok ? MessageBoxIcon.Information : MessageBoxIcon.Warning);

    /// <summary>保存成功后回调（参数=流水号；主界面用于抓拍留证）。</summary>
    internal Action<string>? OnSaved;

    /// <summary>打印动作（默认直接打印磅单；测试注入空实现避免真实出纸）。</summary>
    internal Func<WeighRecord, bool> PrintAction = r => Report.PoundTicketPrinter.Print(r);

    // ===== 控件 =====
    private ComboBox _cboPrefix = null!;     // 车牌省份简称（默认取综合设置的"默认省份"）
    private TextBox _txtVehicle = null!;     // 车牌编号（省份简称后的手动输入部分）
    private TextBox _txtVariety = null!;     // 品种（存记录 Goods 列）
    private TextBox _txtOwner = null!;       // 地主姓名（ext_json 固定键）
    private TextBox _txtOwnerPhone = null!;  // 地主电话（ext_json 固定键）
    private TextBox _txtDriverPhone = null!; // 司机电话（ext_json 固定键）
    private TextBox _txtCargoOwner = null!;  // 货主姓名（ext_json 固定键）
    private TextBox _txtUnitPrice = null!;   // 单价（ext_json 固定键；只允许数字与小数点）
    private TextBox _txtOperator = null!;    // 司磅员（当前登录用户，只读）
    private Label _lblSerialNo = null!;      // No：待配对时显示一次磅流水号
    private Label _lblPairHint = null!;      // 配对/新车提示
    private Label _lblTicketType = null!;    // 票据副标题（皮重/毛重过磅单，随切换变化）
    private Label _lblPanelTitle = null!;    // 右栏标题（本次皮重/本次毛重，随切换变化）
    private Label _lblGrossCell = null!;     // 票据右列：毛重预览
    private Label _lblTareCell = null!;      // 票据右列：皮重预览
    private Label _lblNetCell = null!;       // 票据右列：净重预览
    private Label _lblCurrent = null!;       // 实时重量（右栏大屏）
    private Label _lblLastWeight = null!;    // 上次重量（待配对一次磅）
    private Label _lblLastTime = null!;      // 上次时间
    private Label _lblNetPreview = null!;    // 预计净重（右栏）
    private Button _btnConfirm = null!;      // 链式步骤1：确认
    private Button _btnSave = null!;         // 链式步骤2：保存
    private Button _btnPrint = null!;        // 链式步骤3：打印
    private Button _btnContinue = null!;     // 链式步骤4：继续
    private readonly System.Windows.Forms.Timer _tick = new() { Interval = 200 };

    public WeighEntryDialog(WeighManager manager, bool isTare, string? prefillVehicle = null)
    {
        _manager = manager;
        _isTare = isTare;
        Text = (isTare ? "空车去皮" : "重车过磅") + $"（本次按{(isTare ? "皮" : "毛")}重保存）";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(970, 640);   // 右栏内容与右边缘留出边距
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        Font = new Font("Microsoft YaHei UI", 10F);
        // 高 DPI 下按 DPI 缩放布局，避免文字截断与控件错位
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = _isTare ? TareBack : GrossBack;

        BuildTicket();
        BuildWeightPanel();

        // 当天已保存车牌：自动补全候选（输入一半时下拉出现），选中后回填上次填写的信息
        _todayRecords = _plateRepo.Query(DateTime.Today, null, "", "", "", "")
            .Where(r => r.Status != "已作废").ToList();
        RefreshPlateSuggest();

        _txtVehicle.TextChanged += (_, _) => { TryAutoFill(); UpdateWeight(); };   // 车号变化：回填上次信息 + 刷新配对
        _cboPrefix.SelectedIndexChanged += (_, _) =>
        {
            RefreshPlateSuggest();             // 省份变化 → 补全候选按新省份重建
            TryAutoFill();
            UpdateWeight();
        };
        // 预填放在事件挂接之后：带车号进入时同样触发回填上次信息
        ApplyPrefill(prefillVehicle);

        // 综合设置开启"使用省、字母"且未带预填车号时，编号框预填默认字母
        // （如配置 黑D → 省份下拉"黑"、编号框"D"），光标置于字母后供继续输入
        if (_cfg.UseProvinceLetter && _cfg.DefaultLetter.Length > 0 && _txtVehicle.TextLength == 0)
        {
            _txtVehicle.Text = _cfg.DefaultLetter;
        }
        _txtVehicle.SelectionStart = _txtVehicle.TextLength;   // 光标到末尾，直接续输
        UpdateWeight();
        SetStep(0);   // 链式流程初始：仅"确认"可点

        _tick.Tick += (_, _) => UpdateWeight();   // 实时重量/净重预览随仪表数据刷新
        // 注意：定时器仅在窗体真正显示时启动（OnShown）——测试等场景下未显示的窗体
        // 不启动定时器，避免线程结束后残留未释放的 WinForms Timer
        FormClosed += (_, _) => _tick.Dispose();
    }

    /// <summary>窗体显示后启动实时刷新定时器（仪表数据约 10Hz，200ms 刷新足够）。</summary>
    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _tick.Start();
    }

    // ---------- 票据样式信息录入区（左侧米黄过磅单，网格两列布局仿老软件） ----------
    private void BuildTicket()
    {
        var ticket = new Panel
        {
            Location = new Point(15, 15),
            Size = new Size(650, 575),
            BackColor = Color.FromArgb(255, 255, 224),
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left,
        };

        // 抬头：公司名 + 过磅单类型 + 日期 + 流水号
        ticket.Controls.Add(new Label
        {
            Text = _cfg.CompanyName,
            Font = new Font("Microsoft YaHei UI", 16F, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleCenter,
            Location = new Point(0, 12),
            Size = new Size(650, 36),
        });
        _lblTicketType = new Label
        {
            Text = (_isTare ? "皮重" : "毛重") + _cfg.SlipHeader,   // 票据副标题取综合设置的磅单抬头
            Font = new Font("Microsoft YaHei UI", 12F),
            TextAlign = ContentAlignment.MiddleCenter,
            Location = new Point(0, 52),
            Size = new Size(650, 28),
        };
        ticket.Controls.Add(_lblTicketType);
        ticket.Controls.Add(new Label
        {
            Text = DateTime.Now.ToString("yyyy 年 M 月 d 日"),
            AutoSize = true,
            Location = new Point(25, 92),
            Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold),
        });
        _lblSerialNo = new Label
        {
            Text = "No：",
            AutoSize = true,
            Location = new Point(430, 92),
            Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold),
        };
        ticket.Controls.Add(_lblSerialNo);

        // ---- 固定字段表格：左列录入字段，右列毛/皮/净实时预览，网格线仿老过磅单 ----
        var grid = new TableLayoutPanel
        {
            Location = new Point(25, 124),
            Size = new Size(600, 356),
            ColumnCount = 4,
            RowCount = 7,
            CellBorderStyle = TableLayoutPanelCellBorderStyle.Single,
            BackColor = ticket.BackColor,
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 88));   // 字段名
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 264));  // 输入框（车牌行含省份下拉）
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 68));   // 重量名
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 176));  // 重量值
        for (int i = 0; i < 7; i++)
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));

        // 单元格控件统一留 1px 内边距：避免控件盖住 TableLayoutPanel 的网格线
        Label Head(string t) => new()
        {
            Text = t,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold),
            BackColor = ticket.BackColor,
            Margin = new Padding(1),
        };
        Label ValueCell() => new()
        {
            Text = "--",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleRight,
            Font = new Font("Consolas", 12F, FontStyle.Bold),
            BackColor = Color.White,
            Margin = new Padding(1),
            Padding = new Padding(0, 0, 6, 0),
        };
        // 12 号字 + 上下 7px 边距 → 输入框约 36px 高，视觉上更饱满
        TextBox Input() => new()
        {
            Dock = DockStyle.Fill,
            Font = new Font("Microsoft YaHei UI", 12F),
            Margin = new Padding(6, 7, 6, 7),
        };

        // 行1：车牌号码（省份简称下拉 + 编号手输，带当天历史自动补全）｜ 毛重
        grid.Controls.Add(Head("车牌号码"), 0, 0);
        var platePanel = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Margin = new Padding(2, 7, 2, 7) };
        _cboPrefix = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Font = new Font("Microsoft YaHei UI", 12F),
            Location = new Point(1, 1),
            Size = new Size(54, 34),
            FlatStyle = FlatStyle.Flat,
        };
        // 省份简称列表（首项为空 = 无省份），默认选中综合设置的"默认省份"
        _cboPrefix.Items.Add("");
        foreach (var p in VehiclePadDialog.Provinces) _cboPrefix.Items.Add(p.ToString());
        int idx = _cboPrefix.Items.IndexOf(_cfg.DefaultProvince);
        _cboPrefix.SelectedIndex = idx > 0 ? idx : 0;
        _txtVehicle = new TextBox
        {
            Location = new Point(56, 1),
            Size = new Size(203, 34),
            Font = new Font("Microsoft YaHei UI", 12F),
            // 当天保存过的车牌：输入一半时在下拉中出现（按编号前缀建议）
            AutoCompleteMode = AutoCompleteMode.SuggestAppend,
            AutoCompleteSource = AutoCompleteSource.CustomSource,
        };
        platePanel.Controls.Add(_cboPrefix);
        platePanel.Controls.Add(_txtVehicle);
        grid.Controls.Add(platePanel, 1, 0);
        grid.Controls.Add(Head("毛重"), 2, 0);
        _lblGrossCell = ValueCell();
        grid.Controls.Add(_lblGrossCell, 3, 0);

        // 行2：地主姓名 ｜ 皮重
        grid.Controls.Add(Head("地主姓名"), 0, 1);
        _txtOwner = Input();
        grid.Controls.Add(_txtOwner, 1, 1);
        grid.Controls.Add(Head("皮重"), 2, 1);
        _lblTareCell = ValueCell();
        grid.Controls.Add(_lblTareCell, 3, 1);

        // 行3：地主电话 ｜ 净重
        grid.Controls.Add(Head("地主电话"), 0, 2);
        _txtOwnerPhone = Input();
        grid.Controls.Add(_txtOwnerPhone, 1, 2);
        grid.Controls.Add(Head("净重"), 2, 2);
        _lblNetCell = ValueCell();
        grid.Controls.Add(_lblNetCell, 3, 2);

        // 行4：司机电话（右列留空）
        grid.Controls.Add(Head("司机电话"), 0, 3);
        _txtDriverPhone = Input();
        grid.Controls.Add(_txtDriverPhone, 1, 3);

        // 行5：货主姓名（右列留空）
        grid.Controls.Add(Head("货主姓名"), 0, 4);
        _txtCargoOwner = Input();
        grid.Controls.Add(_txtCargoOwner, 1, 4);

        // 行6：品种（存记录 Goods 列；右列留空）
        grid.Controls.Add(Head("品种"), 0, 5);
        _txtVariety = Input();
        grid.Controls.Add(_txtVariety, 1, 5);

        // 行7：单价（ext_json 固定键；右列留空）——只允许输入数字与小数点
        grid.Controls.Add(Head("单价"), 0, 6);
        _txtUnitPrice = Input();
        _txtUnitPrice.KeyPress += (_, e) =>
        {
            // 拦截数字/小数点/控制键（退格）之外的字符，避免录成文字后无法参与金额计算
            if (!char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar) && e.KeyChar != '.')
                e.Handled = true;
        };
        grid.Controls.Add(_txtUnitPrice, 1, 6);
        ticket.Controls.Add(grid);

        // 表格下方：司磅员（当前登录用户，自动记录）
        ticket.Controls.Add(new Label
        {
            Text = "司磅员：",
            AutoSize = true,
            Location = new Point(25, 494),
            Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold),
        });
        _txtOperator = new TextBox
        {
            Location = new Point(113, 488), Size = new Size(200, 36),
            Font = new Font("Microsoft YaHei UI", 12F),
            ReadOnly = true, Text = AppSession.DisplayName,
        };
        ticket.Controls.Add(_txtOperator);

        // 底部：配对/新车提示
        _lblPairHint = new Label
        {
            Text = "",
            AutoSize = true,
            Location = new Point(25, ticket.Height - 36),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left,
            ForeColor = Color.FromArgb(0, 102, 204),
        };
        ticket.Controls.Add(_lblPairHint);

        Controls.Add(ticket);
    }

    // ---------- 右侧实时重量与操作区 ----------
    private void BuildWeightPanel()
    {
        // 顶部"切换"按钮：点击在皮重/毛重模式间转换（对应老软件过磅单的"切换"）
        var btnToggle = new Button
        {
            Text = "切 换",
            Location = new Point(685, 15),
            Size = new Size(250, 40),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.White,
            Font = new Font("Microsoft YaHei UI", 12F, FontStyle.Bold),
        };
        new ToolTip().SetToolTip(btnToggle, "在空车去皮（皮重）与重车过磅（毛重）之间切换");
        btnToggle.Click += (_, _) => ToggleMode();
        Controls.Add(btnToggle);

        _lblPanelTitle = new Label
        {
            Text = _isTare ? "本次皮重" : "本次毛重",
            Font = new Font("Microsoft YaHei UI", 14F, FontStyle.Bold),
            ForeColor = Color.White,
            AutoSize = true,
            Location = new Point(690, 70),
        };
        Controls.Add(_lblPanelTitle);

        _lblCurrent = new Label
        {
            Text = "0 " + _cfg.UnitDisplay,
            Font = new Font("Consolas", 26F, FontStyle.Bold),
            ForeColor = Color.OrangeRed,
            BackColor = Color.Black,
            BorderStyle = BorderStyle.FixedSingle,
            TextAlign = ContentAlignment.MiddleCenter,
            Location = new Point(685, 106),
            Size = new Size(250, 72),
        };
        Controls.Add(_lblCurrent);

        // 待配对一次磅信息（上次重量/上次时间/预计净重）
        Label Info(string t, int y) => new()
        { Text = t, AutoSize = true, Location = new Point(675, y), ForeColor = Color.White };
        Label Val(int y) => new()
        {
            Text = "--", AutoSize = true,
            Location = new Point(770, y),
            ForeColor = Color.FromArgb(255, 239, 130),
            Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold),
        };
        Controls.Add(Info("上次重量：", 200));
        _lblLastWeight = Val(200);
        Controls.Add(_lblLastWeight);
        Controls.Add(Info("上次时间：", 230));
        _lblLastTime = Val(230);
        Controls.Add(_lblLastTime);
        Controls.Add(Info("预计净重：", 260));
        _lblNetPreview = Val(260);
        Controls.Add(_lblNetPreview);

        // ===== 链式流程按钮（确认 ↓ 保存 ↓ 打印 ↓ 继续，仿老软件逐步解锁）=====
        Button ChainButton(string text, int y, int h) => new()
        {
            Text = text,
            Location = new Point(685, y),
            Size = new Size(250, h),
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Microsoft YaHei UI", 13F, FontStyle.Bold),
            FlatAppearance.BorderSize = 1,
        };
        Label Arrow(int y) => new()
        {
            Text = "↓",
            TextAlign = ContentAlignment.MiddleCenter,
            Location = new Point(685, y),
            Size = new Size(250, 22),
            ForeColor = Color.Lime,
            Font = new Font("Microsoft YaHei UI", 12F, FontStyle.Bold),
        };

        _btnConfirm = ChainButton("确 认", 300, 48);
        _btnSave = ChainButton("保 存", 370, 48);
        _btnPrint = ChainButton("打 印", 440, 48);
        _btnContinue = ChainButton("继 续", 510, 42);
        _btnConfirm.Click += (_, _) => Confirm();
        _btnSave.Click += (_, _) => DoSave();
        _btnPrint.Click += (_, _) => DoPrint();
        _btnContinue.Click += (_, _) => DoContinue();

        // 退出：返回主界面（DialogResult.OK 触发主界面刷新记录/统计）
        var btnExit = new Button
        {
            Text = "退 出",
            Location = new Point(685, 556),
            Size = new Size(250, 34),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(235, 235, 235),
            Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold),
        };
        btnExit.Click += (_, _) => { DialogResult = DialogResult.OK; Close(); };

        Controls.Add(_btnConfirm);
        Controls.Add(Arrow(348));
        Controls.Add(_btnSave);
        Controls.Add(Arrow(418));
        Controls.Add(_btnPrint);
        Controls.Add(Arrow(488));
        Controls.Add(_btnContinue);
        Controls.Add(btnExit);
    }

    // ---------- 链式步骤状态与按钮外观 ----------
    // 未到达=灰 / 当前可点=白 / 已完成=浅绿
    private static readonly Color BtnLocked = Color.FromArgb(195, 195, 195);
    private static readonly Color BtnCurrent = Color.White;
    private static readonly Color BtnDone = Color.FromArgb(172, 226, 172);

    /// <summary>切换流程阶段并刷新四个按钮的颜色（灰=未到达，白=当前步，绿=已完成）。</summary>
    private void SetStep(int step)
    {
        _step = step;
        static void Style(Button b, int state)
        {
            b.BackColor = state < 0 ? BtnLocked : state == 0 ? BtnCurrent : BtnDone;
            b.ForeColor = state < 0 ? Color.DimGray : Color.Black;
        }
        Style(_btnConfirm, step);
        Style(_btnSave, step - 1);
        Style(_btnPrint, step - 2);
        Style(_btnContinue, step - 3);
    }

    /// <summary>"切换"按钮：皮重/毛重模式互换，同步刷新标题、背景色、票据副标题与右列预览。</summary>
    private void ToggleMode()
    {
        _isTare = !_isTare;
        BackColor = _isTare ? TareBack : GrossBack;   // 皮重=蓝、毛重=绿，一眼可辨当前流程
        Text = (_isTare ? "空车去皮" : "重车过磅") + $"（本次按{(_isTare ? "皮" : "毛")}重保存）";
        _lblTicketType.Text = (_isTare ? "皮重" : "毛重") + _cfg.SlipHeader;
        _lblPanelTitle.Text = _isTare ? "本次皮重" : "本次毛重";
        UpdateWeight();
    }

    /// <summary>
    /// 组合完整车号 = 省份简称 + 编号输入框内容；
    /// 编号本身已以省份开头（如手输整牌）时不重复拼接。
    /// </summary>
    internal string ComposeVehicleNo()
    {
        string prefix = _cboPrefix.SelectedItem?.ToString() ?? "";
        string rest = _txtVehicle.Text.Trim();
        return rest.StartsWith(prefix, StringComparison.Ordinal) ? rest : prefix + rest;
    }

    /// <summary>
    /// 填入完整车牌（预填车号）：首位是省份简称时拆到省份下拉，
    /// 其余进编号输入框；无省份简称时整体进输入框。
    /// </summary>
    private void ApplyPrefill(string? plate)
    {
        if (string.IsNullOrEmpty(plate)) return;
        if (VehiclePadDialog.Provinces.Contains(plate[0]))
        {
            int idx = _cboPrefix.Items.IndexOf(plate[0].ToString());
            if (idx > 0) _cboPrefix.SelectedIndex = idx;   // 与输入车牌一致的省份
            _txtVehicle.Text = plate[1..];
        }
        else
        {
            _txtVehicle.Text = plate;
        }
    }

    /// <summary>
    /// 重建车牌自动补全候选：当天已保存记录中与当前选中省份匹配的车牌，
    /// 去掉省份简称取编号部分去重后作为建议项（输入一半即在下拉中出现）。
    /// </summary>
    private void RefreshPlateSuggest()
    {
        string prefix = _cboPrefix.SelectedItem?.ToString() ?? "";
        var src = new AutoCompleteStringCollection();
        src.AddRange(_todayRecords
            .Where(r => prefix.Length == 0 || r.VehicleNo.StartsWith(prefix, StringComparison.Ordinal))
            .Select(r => r.VehicleNo.StartsWith(prefix, StringComparison.Ordinal)
                ? r.VehicleNo[prefix.Length..]
                : r.VehicleNo)
            .Distinct()
            .ToArray());
        _txtVehicle.AutoCompleteCustomSource = src;
    }

    /// <summary>
    /// 完整车号命中当天已保存记录时，自动回填上次填写的信息
    /// （品种 + 地主姓名/地主电话/司机电话/货主姓名/单价，取该车牌最新一条）。
    /// </summary>
    private void TryAutoFill()
    {
        string no = ComposeVehicleNo();
        if (no.Length == 0) return;
        var hit = _todayRecords.FirstOrDefault(r => r.VehicleNo == no);
        if (hit == null) return;

        var ext = hit.ParseExt();
        _txtVariety.Text = hit.Goods ?? "";
        _txtOwner.Text = ext.GetValueOrDefault("地主姓名", "");
        _txtOwnerPhone.Text = ext.GetValueOrDefault("地主电话", "");
        _txtDriverPhone.Text = ext.GetValueOrDefault("司机电话", "");
        _txtCargoOwner.Text = ext.GetValueOrDefault("货主姓名", "");
        _txtUnitPrice.Text = ext.GetValueOrDefault("单价", "");
    }

    /// <summary>
    /// 刷新实时重量与配对信息：稳定绿/动态橙；票据右列按模式回填——
    /// 本次称重填入对应行（去皮填皮重行、过毛填毛重行），配对的一次磅填对侧行；
    /// 车号匹配到未完成一次磅时显示其流水号/上次重量/上次时间与预计净重，
    /// 保存按钮切换为"保存并结算"。
    /// </summary>
    private void UpdateWeight()
    {
        _lblCurrent.Text = _cfg.FormatWeight(_manager.CurrentKg);
        // 绿灯 = 达到"可保存"稳定要求（综合设置开启"数据稳定 X 秒"时须持续满该秒数）
        _lblCurrent.ForeColor = _manager.IsStableForSave ? Color.LimeGreen : Color.OrangeRed;

        string no = ComposeVehicleNo();
        var first = no.Length == 0 ? null
            : _manager.GetUnfinishedList().FirstOrDefault(r => r.VehicleNo == no);

        // 票据右列毛/皮/净：实时值进本次行，已存一次磅进对侧行，配对且稳定时预览净重
        string live = _cfg.FormatWeight(_manager.CurrentKg);
        string saved = first != null ? _cfg.FormatWeight(first.GrossKg ?? 0) : "--";
        if (_isTare)
        {
            _lblTareCell.Text = live;
            _lblGrossCell.Text = saved;
        }
        else
        {
            _lblGrossCell.Text = live;
            _lblTareCell.Text = saved;
        }
        _lblNetCell.Text = first != null && _manager.IsStable
            ? _cfg.FormatWeight(Math.Abs((first.GrossKg ?? 0) - _manager.StableKg))
            : "--";

        if (first != null)
        {
            _lblSerialNo.Text = "No：" + first.SerialNo;
            _lblLastWeight.Text = _cfg.FormatWeight(first.GrossKg ?? 0);
            _lblLastTime.Text = first.FirstTime?.ToString("yyyy-MM-dd HH:mm") ?? "";
            _lblPairHint.Text = $"[{no}] 有未完成的二次磅，保存时将自动配对结算";
            _btnSave.Text = "保存并结算";
            _lblNetPreview.Text = _manager.IsStable
                ? _cfg.FormatWeight(Math.Abs((first.GrossKg ?? 0) - _manager.StableKg))
                : "--";
        }
        else
        {
            _lblSerialNo.Text = "No：";
            _lblLastWeight.Text = "--";
            _lblLastTime.Text = "--";
            _lblPairHint.Text = no.Length > 0 ? $"[{no}] 新车辆，本次为第一次称重" : "";
            _btnSave.Text = "保存";
            _lblNetPreview.Text = "--";
        }
    }

    // ================================================================
    // 保存（与首页按钮一致的皮/毛手动流程）
    // ================================================================

    /// <summary>统一保存：按车号查找待配对记录——有则二次磅结算，无则第一次称重。</summary>
    private void Save()
    {
        // 地主电话是平台归属匹配的必填依据（格式非法平台整条拒收），录入时即校验
        if (!ValidPhone(_txtOwnerPhone.Text.Trim()))
        {
            InfoBox("地主电话格式不正确：请输入 1 开头的 11 位手机号。", "提示", false);
            return;
        }

        string action = _isTare ? "空车去皮" : "重车过磅";
        string no = ComposeVehicleNo();

        var ext = CollectExt();

        var first = no.Length == 0 ? null
            : _manager.GetUnfinishedList().FirstOrDefault(r => r.VehicleNo == no);
        string weightText = _cfg.FormatWeight(_manager.StableKg);

        if (first != null)
        {
            // 二次磅结算
            if (!ConfirmBox($"{action}（第二次称重·结算）\n车号：{first.VehicleNo}  " +
                $"一次磅：{_cfg.FormatWeight(first.GrossKg ?? 0)}  本次：{weightText}", "确认操作"))
                return;

            var (ok, msg, record) = _manager.SaveSecondWeigh(first, null, _txtOperator.Text);
            InfoBox(msg, ok ? "结算成功" : "提示", ok);
            if (!ok) return;

            OnSaved?.Invoke(record!.SerialNo);
            TryPrint(record!);
            DialogResult = DialogResult.OK;
        }
        else
        {
            // 第一次称重（品种存 Goods 列；扣重/备注已从表单移除，存 null）
            if (!ConfirmBox($"{action}（第一次称重）\n车号：{no}  重量：{weightText}", "确认操作"))
                return;

            var (ok, msg, newRecord) = _manager.SaveFirstWeigh(
                no, _txtVariety.Text, null, null,
                null, null, null,
                null, _txtOperator.Text, ext);
            InfoBox(msg, ok ? "成功" : "提示", ok);
            if (!ok) return;

            OnSaved?.Invoke(newRecord!.SerialNo);
            DialogResult = DialogResult.OK;
        }
    }

    /// <summary>结算完成后打印磅单：开启自动打印直接出单，否则询问（对应 PRD F5-01 / F9-04）。</summary>
    private void TryPrint(Core.WeighRecord record)
    {
        if (AppConfig.Current.AutoPrintEnabled || ConfirmBox("是否打印磅单？", "打印磅单"))
        {
            Report.PoundTicketPrinter.Print(record);
        }
    }

    /// <summary>手机号格式校验（平台归属匹配要求 1 开头 11 位数字；留空不强填视为有效）。</summary>
    internal static bool ValidPhone(string phone) =>
        phone.Length == 0 || Regex.IsMatch(phone, "^1\\d{10}$");

    /// <summary>采集固定字段非空值（以固定键写入记录 ext_json，记录查询与磅单模板可直接引用）。</summary>
    private Dictionary<string, string> CollectExt()
    {
        var ext = new Dictionary<string, string>();
        void Add(string key, TextBox box)
        {
            string val = box.Text.Trim();
            if (val.Length > 0) ext[key] = val;
        }
        Add("地主姓名", _txtOwner);
        Add("地主电话", _txtOwnerPhone);
        Add("司机电话", _txtDriverPhone);
        Add("货主姓名", _txtCargoOwner);
        Add("单价", _txtUnitPrice);
        return ext;
    }
}