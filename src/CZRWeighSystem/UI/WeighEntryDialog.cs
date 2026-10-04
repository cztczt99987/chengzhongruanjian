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
    private PlateKeypadForm? _keypad;        // 车牌编号字母触摸面板（双击编号框弹出）

    /// <summary>当天已保存过（未作废）的记录：车牌自动补全候选 + 选中后回填上次信息。</summary>
    private List<WeighRecord> _todayRecords = [];

    /// <summary>
    /// 当前值由"命中历史车牌自动回填"而来的字段集合。
    /// 换车号且无命中时只清空这些字段——用户手动输入/改过的字段保留，
    /// 避免自动填入的上一辆车信息残留到新车；用户一旦手改某字段即从集合摘除。
    /// </summary>
    private readonly HashSet<TextBox> _autoFilledFields = [];
    /// <summary>程序回填赋值进行中：此期间字段 TextChanged 不算用户手改，不摘除脏标记。</summary>
    private bool _autoFilling;

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

    /// <summary>当前打开的字母面板（测试断言用；未打开为 null）。</summary>
    internal PlateKeypadForm? CurrentKeypad => _keypad;

    // ===== 控件 =====
    private ProvincePicker _cboPrefix = null!; // 车牌省份简称（多列网格选择，默认取综合设置的"默认省份"）
    private TextBox _txtVehicle = null!;     // 车牌编号（省份简称后的手动输入部分）
    private TextBox _txtVariety = null!;     // 品种（存记录 Goods 列）
    private TextBox _txtOwner = null!;       // 地主姓名（ext_json 固定键）
    private TextBox _txtOwnerPhone = null!;  // 地主电话（ext_json 固定键）
    private TextBox _txtDriverPhone = null!; // 司机电话（ext_json 固定键）
    private TextBox _txtCargoOwner = null!;  // 货主姓名（ext_json 固定键）
    private TextBox _txtUnitPrice = null!;   // 单价（ext_json 固定键；只允许数字与小数点）
    private TextBox _txtOperator = null!;    // 司磅员（当前登录用户，只读）
    private TableLayoutPanel _grid = null!;  // 票据固定字段表格（测试用：验证不压住下方司磅员框）
    private Label _lblSerialNo = null!;      // No：待配对时显示一次磅流水号
    private Label _lblPairHint = null!;      // 配对/新车提示
    private Label _lblTicketType = null!;    // 票据副标题（皮重/毛重过磅单，随切换变化）
    private Label _lblPanelTitle = null!;    // 右栏标题（本次皮重/本次毛重，随切换变化）
    private Label _lblGrossCell = null!;     // 票据右列：毛重预览
    private Label _lblTareCell = null!;      // 票据右列：皮重预览
    private Label _lblNetCell = null!;       // 票据右列：净重预览
    private Label _lblCurrent = null!;       // 实时重量（右栏大屏）
    private Label _lblLastWeightTitle = null!; // 上次重量标题（仅空车去皮模式显示）
    private Label _lblLastWeight = null!;    // 上次重量（本车最近一次已完成皮重）
    private Label _lblLastTimeTitle = null!; // 上次时间标题（仅空车去皮模式显示）
    private Label _lblLastTime = null!;      // 上次时间
    private string? _lastTareKey;            // 上次皮重缓存对应的车号（避免定时器反复查库）
    private WeighRecord? _lastTare;          // 本车最近一次已完成皮重记录（null=无历史皮重）
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
        Size = new Size(970, 655);   // 右栏内容与右边缘留出边距；票据加高后窗口同步加高
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
        FormClosed += (_, _) =>
        {
            _tick.Dispose();
            _keypad?.Close();   // 字母面板一并关闭（测试场景避免残留窗口）
        };
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
            Size = new Size(650, 590),
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
        // 高度由行内容自适应（AutoSize）：固定高度在高 DPI 缩放下与行高总和产生取整误差，
        // 空间不足时 TLP 会压缩靠后的行（用户实机"单价行下方显示不全"即第 7 行被压）
        var grid = new TableLayoutPanel
        {
            Location = new Point(25, 124),
            ColumnCount = 4,
            RowCount = 7,
            CellBorderStyle = TableLayoutPanelCellBorderStyle.Single,
            BackColor = ticket.BackColor,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };
        _grid = grid;   // 提为字段供布局测试断言（不压住下方司磅员框）
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
        // 省份选择器：点击弹出 8 列×4 行网格，避免 31 个省份拉成超长单列下拉
        _cboPrefix = new ProvincePicker
        {
            Font = new Font("Microsoft YaHei UI", 12F),
            Location = new Point(1, 1),
            Size = new Size(54, 34),
        };
        // 首项为"无省份"，默认选中综合设置的"默认省份"
        int idx = ProvincePicker.Items.IndexOf(_cfg.DefaultProvince);
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
        // 老软件交互：双击编号框（字母位置）弹出 A-Z 触摸面板，选字母连续输入
        _txtVehicle.DoubleClick += (_, _) => ShowKeypad();
        new ToolTip().SetToolTip(_txtVehicle, "双击打开字母选择面板（可连续点选）");
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

        // 用户手动改任一可回填字段 → 摘除其"自动填入"标记，之后换车号不再把它当旧车信息清掉
        foreach (var box in new[] { _txtVariety, _txtOwner, _txtOwnerPhone, _txtDriverPhone, _txtCargoOwner, _txtUnitPrice })
            box.TextChanged += (_, _) => { if (!_autoFilling) _autoFilledFields.Remove(box); };

        // 表格下方：司磅员（当前登录用户，自动记录）。
        // 框顶与表格底留 24px（表格底 480 → 框顶 504）：高 DPI 缩放取整会吃掉小间隙，
        // 原 8px 间隙在用户 125%/150% 缩放下表格底边框线压住框顶
        ticket.Controls.Add(new Label
        {
            Text = "司磅员：",
            AutoSize = true,
            Location = new Point(25, 510),
            Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold),
        });
        _txtOperator = new TextBox
        {
            Location = new Point(113, 504), Size = new Size(200, 36),
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

        // 上次空车去皮信息（仅空车去皮模式可见：本车最近一次已完成皮重与时间）
        Label Info(string t, int y) => new()
        { Text = t, AutoSize = true, Location = new Point(675, y), ForeColor = Color.White };
        Label Val(int y) => new()
        {
            Text = "--", AutoSize = true,
            Location = new Point(770, y),
            ForeColor = Color.FromArgb(255, 239, 130),
            Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold),
        };
        _lblLastWeightTitle = Info("上次重量：", 200);
        _lblLastWeight = Val(200);
        _lblLastTimeTitle = Info("上次时间：", 230);
        _lblLastTime = Val(230);
        Controls.AddRange([_lblLastWeightTitle, _lblLastWeight, _lblLastTimeTitle, _lblLastTime]);
        UpdateLastTareVisibility();   // 重车过磅模式不显示该区块

        // ===== 链式流程按钮（确认 ↓ 保存 ↓ 打印 ↓ 继续，仿老软件逐步解锁）=====
        Button ChainButton(string text, int y, int h) => new()
        {
            Text = text,
            Location = new Point(685, y),
            Size = new Size(250, h),
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Microsoft YaHei UI", 13F, FontStyle.Bold),
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

    /// <summary>"切换"按钮：皮重/毛重模式互换，同步刷新标题、背景色、票据副标题与右列预览，
    /// 并放弃当前链式进度回到"确认"（确认后再切换模式没有意义）。</summary>
    private void ToggleMode()
    {
        _isTare = !_isTare;
        BackColor = _isTare ? TareBack : GrossBack;   // 皮重=蓝、毛重=绿，一眼可辨当前流程
        Text = (_isTare ? "空车去皮" : "重车过磅") + $"（本次按{(_isTare ? "皮" : "毛")}重保存）";
        _lblTicketType.Text = (_isTare ? "皮重" : "毛重") + _cfg.SlipHeader;
        _lblPanelTitle.Text = _isTare ? "本次皮重" : "本次毛重";
        _lastTareKey = null;   // 模式切换后车号相同也要重取（显示/隐藏语义已变）
        UpdateLastTareVisibility();
        ResetFlow();
        UpdateWeight();
    }

    /// <summary>上次空车去皮信息区块仅空车去皮模式可见（重车过磅模式整组隐藏）。</summary>
    private void UpdateLastTareVisibility()
    {
        bool show = _isTare;
        _lblLastWeightTitle.Visible = show;
        _lblLastWeight.Visible = show;
        _lblLastTimeTitle.Visible = show;
        _lblLastTime.Visible = show;
    }

    /// <summary>上次空车去皮信息区块是否应显示（仅空车去皮模式）。
    /// 测试在不 Show 窗体时无法读取控件真实 Visible（父窗体未显示时恒为 false），故暴露此状态。</summary>
    internal bool LastTareInfoVisible => _isTare;

    /// <summary>
    /// 取本车最近一次已完成皮重（按车号缓存，车号变化才重新查库）。
    /// 空车号返回 null；仅空车去皮模式调用。
    /// </summary>
    private WeighRecord? GetLastTare(string no)
    {
        if (no.Length == 0) return null;
        if (_lastTareKey != no)
        {
            _lastTareKey = no;
            _lastTare = _plateRepo.GetLastCompletedTare(no);
        }
        return _lastTare;
    }

    /// <summary>放弃链式进度：清空确认/保存状态，回到"确认"步。</summary>
    private void ResetFlow()
    {
        _confirmedKg = null;
        _confirmedFirst = null;
        _savedRecord = null;
        SetStep(0);
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
            int idx = ProvincePicker.Items.IndexOf(plate[0].ToString());
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
    /// 车号变化时回填/清理历史信息：
    /// 完整车号命中当天已保存记录 → 回填最新一条的品种/地主姓名/地主电话/司机电话/货主姓名/单价，
    /// 并把这些字段标记为"自动填入"；
    /// 车号为空或无命中 → 只清空仍带"自动填入"标记的字段（用户手动输入/改过的保留），
    /// 避免上一辆车的自动回填信息残留到新车。
    /// </summary>
    private void TryAutoFill()
    {
        string no = ComposeVehicleNo();
        var hit = no.Length == 0 ? null
            : _todayRecords.FirstOrDefault(r => r.VehicleNo == no);

        _autoFilling = true;   // 下面赋值触发的 TextChanged 是程序回填，不算用户手改
        try
        {
            if (hit != null)
            {
                var ext = hit.ParseExt();
                FillAuto(_txtVariety, hit.Goods ?? "");
                FillAuto(_txtOwner, ext.GetValueOrDefault("地主姓名", ""));
                FillAuto(_txtOwnerPhone, ext.GetValueOrDefault("地主电话", ""));
                FillAuto(_txtDriverPhone, ext.GetValueOrDefault("司机电话", ""));
                FillAuto(_txtCargoOwner, ext.GetValueOrDefault("货主姓名", ""));
                FillAuto(_txtUnitPrice, ext.GetValueOrDefault("单价", ""));
            }
            else
            {
                // 无命中：清空自动回填字段；用户手填/手改过的字段（不在集合中）原样保留
                foreach (var box in _autoFilledFields.ToList())
                    box.Text = "";
                _autoFilledFields.Clear();
            }
        }
        finally
        {
            _autoFilling = false;
        }
    }

    /// <summary>程序自动回填一个字段并登记脏标记（仅值变化时才登记，空值不登记）。</summary>
    private void FillAuto(TextBox box, string value)
    {
        box.Text = value;
        if (value.Length > 0) _autoFilledFields.Add(box);
        else _autoFilledFields.Remove(box);
    }

    /// <summary>
    /// 刷新实时重量与配对信息：稳定绿/动态橙；票据右列按模式回填——
    /// 本次称重填入对应行（去皮填皮重行、过毛填毛重行），配对的一次磅填对侧行；
    /// 车号匹配到未完成一次磅时显示其流水号。
    /// 保存成功后票面固定展示已存结果，不再跟随实时重量（车下磅归零后仍应看到本次值，
    /// 显示"毛重有值、皮重为空"会让操作员误读）。
    /// 空车去皮模式另在右栏显示本车最近一次已完成皮重（供比对，超差在确认时预警）。
    /// </summary>
    private void UpdateWeight()
    {
        _lblCurrent.Text = _cfg.FormatWeight(_manager.CurrentKg);
        // 绿灯 = 达到"可保存"稳定要求（综合设置开启"数据稳定 X 秒"时须持续满该秒数）
        _lblCurrent.ForeColor = _manager.IsStableForSave ? Color.LimeGreen : Color.OrangeRed;

        string no = ComposeVehicleNo();

        if (_savedRecord != null)
        {
            // 保存后票面固定展示已存结果，不跟随实时重量变化（车下磅归零后仍应看到本次值）
            if (_savedRecord.NetKg.HasValue)
            {
                // 二次磅结算完成：毛/皮/净均为结算后的最终值（按大小自动分配）
                _lblGrossCell.Text = _cfg.FormatWeight(_savedRecord.GrossKg ?? 0);
                _lblTareCell.Text = _cfg.FormatWeight(_savedRecord.TareKg ?? 0);
                _lblNetCell.Text = _cfg.FormatWeight(_savedRecord.NetKg.Value);
                _lblPairHint.Text = $"[{_savedRecord.VehicleNo}] 已结算完成";
            }
            else
            {
                // 一次磅：重量暂存于 gross 列（配对时按大小归入毛或皮），展示在称重模式对应的行
                string saved = _cfg.FormatWeight(_savedRecord.GrossKg ?? 0);
                _lblGrossCell.Text = _isTare ? "--" : saved;
                _lblTareCell.Text = _isTare ? saved : "--";
                _lblNetCell.Text = "--";
                _lblPairHint.Text = $"[{_savedRecord.VehicleNo}] 一次磅已保存，等待二次磅配对结算";
            }
            _lblSerialNo.Text = "No：" + _savedRecord.SerialNo;
        }
        else
        {
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
                _lblPairHint.Text = $"[{no}] 有未完成的二次磅，保存时将自动配对结算";
            }
            else
            {
                _lblSerialNo.Text = "No：";
                _lblPairHint.Text = no.Length > 0 ? $"[{no}] 新车辆，本次为第一次称重" : "";
            }
        }

        // 右栏"上次重量/上次时间"：仅空车去皮模式，取本车最近一次已完成皮重
        if (_isTare)
        {
            var lastTare = GetLastTare(no);
            _lblLastWeight.Text = lastTare?.TareKg.HasValue == true
                ? _cfg.FormatWeight(lastTare.TareKg.Value) : "--";
            _lblLastTime.Text = lastTare?.FirstTime?.ToString("yyyy-MM-dd HH:mm") ?? "--";
        }
        else
        {
            _lblLastWeight.Text = "--";
            _lblLastTime.Text = "--";
        }
    }

    // ================================================================
    // 链式流程：确认 → 保存 → 打印 → 继续
    // ================================================================

    /// <summary>步骤1【确认】：校验车号/稳定/超载，锁定当前稳定读数并识别是否二次磅。</summary>
    private void Confirm()
    {
        if (_step != 0) return;

        // 地主电话是平台归属匹配的必填依据（格式非法平台整条拒收），录入时即校验
        if (!ValidPhone(_txtOwnerPhone.Text.Trim()))
        {
            InfoBox("地主电话格式不正确：请输入 1 开头的 11 位手机号。", "提示", false);
            return;
        }

        string no = ComposeVehicleNo();
        if (no.Length == 0)
        {
            InfoBox("请选择车号", "提示", false);
            return;
        }
        if (!_manager.IsStableForSave)
        {
            InfoBox("重量未稳定，请等待稳定后再确认", "提示", false);
            return;
        }
        if (_manager.StableKg <= 0)
        {
            InfoBox("当前重量为 0，请确认车辆已上磅", "提示", false);
            return;
        }
        if (_manager.IsOverload)
        {
            InfoBox("重量超出最大量程，禁止保存", "提示", false);
            return;
        }

        _confirmedKg = _manager.StableKg;
        _confirmedFirst = _manager.GetUnfinishedList().FirstOrDefault(r => r.VehicleNo == no);

        // 空车去皮差值预警：本次皮重与本车最近一次已完成皮重相差超过综合设置阈值（吨）时提示。
        // 仅预警不阻止保存（用户已确认）；阈值 0 = 关闭。
        if (_isTare && _cfg.TareDiffWarnTon > 0)
        {
            var lastTare = GetLastTare(no);
            if (lastTare?.TareKg.HasValue == true)
            {
                double diffTon = Math.Abs(_confirmedKg.Value - lastTare.TareKg.Value) / 1000.0;
                if (diffTon > _cfg.TareDiffWarnTon)
                {
                    InfoBox(
                        $"两次空车去皮称重的差值过大！\n\n"
                        + $"上次皮重：{_cfg.FormatWeight(lastTare.TareKg.Value)}"
                        + $"（{lastTare.FirstTime:yyyy-MM-dd HH:mm}）\n"
                        + $"本次皮重：{_cfg.FormatWeight(_confirmedKg.Value)}\n"
                        + $"相差：{diffTon:0.##} 吨（预警值 {_cfg.TareDiffWarnTon:0.##} 吨）\n\n"
                        + "请核实车辆状态（货厢残留/改装/油品水量等）后再保存。",
                        "空车去皮差值预警", false);
                }
            }
        }

        SetStep(1);   // 确认完成 → 解锁"保存"
    }

    /// <summary>步骤2【保存】：有确认的待配对记录则二次磅结算，否则第一次称重（无弹窗）。</summary>
    private void DoSave()
    {
        if (_step != 1 || _confirmedKg == null) return;

        string no = ComposeVehicleNo();
        var ext = CollectExt();

        if (_confirmedFirst != null)
        {
            // 二次磅结算（manager 内部校验当前仍稳定，并按大小分配毛/皮）
            var (ok, msg, record) = _manager.SaveSecondWeigh(_confirmedFirst, null, _txtOperator.Text);
            if (!ok) { InfoBox(msg, "提示", false); return; }

            _savedRecord = record;
            OnSaved?.Invoke(record!.SerialNo);
        }
        else
        {
            // 第一次称重（品种存 Goods 列；扣重/备注已从表单移除，存 null）
            var (ok, msg, newRecord) = _manager.SaveFirstWeigh(
                no, _txtVariety.Text, null, null,
                null, null, null,
                null, _txtOperator.Text, ext);
            if (!ok) { InfoBox(msg, "提示", false); return; }

            _savedRecord = newRecord;
            OnSaved?.Invoke(newRecord!.SerialNo);
        }
        _lastTareKey = null;   // 保存后历史皮重可能变化，下次查询重新取库
        SetStep(2);   // 保存完成 → 解锁"打印"
    }

    /// <summary>步骤3【打印】：直接打印磅单，不再弹"是否打印"；失败可原地重试。</summary>
    private void DoPrint()
    {
        if (_step != 2 || _savedRecord == null) return;

        bool printed;
        using (var pi = new PrintingIndicator())
        {
            pi.Show(this);
            try
            {
                printed = PrintAction(_savedRecord);
            }
            catch (Exception ex)
            {
                Log.Error("磅单打印失败", ex);
                printed = false;
            }
        }

        if (!printed)
        {
            InfoBox("打印未完成：请检查打印机是否开机/有纸，然后再次点击\"打印\"重试。",
                "提示", false);
            return;   // 停在打印步，允许重试
        }
        SetStep(3);   // 打印完成 → 解锁"继续"
    }

    /// <summary>步骤4【继续】：清空表单与流程状态，窗口不关闭，连续录入下一辆。</summary>
    private void DoContinue()
    {
        if (_step != 3) return;

        _txtVehicle.Clear();
        _txtVariety.Clear();
        _txtOwner.Clear();
        _txtOwnerPhone.Clear();
        _txtDriverPhone.Clear();
        _txtCargoOwner.Clear();
        _txtUnitPrice.Clear();
        _autoFilledFields.Clear();   // 表单整体清空：脏标记一并复位，不依赖各 Clear 的事件顺序

        _confirmedKg = null;
        _confirmedFirst = null;
        _savedRecord = null;

        // 当天记录加入刚保存的车号（下一辆车输入时自动补全/回填候选）
        _todayRecords = _plateRepo.Query(DateTime.Today, null, "", "", "", "")
            .Where(r => r.Status != "已作废").ToList();
        RefreshPlateSuggest();

        SetStep(0);
        _txtVehicle.Focus();
    }

    // ---------- 车牌字母触摸面板 ----------

    /// <summary>打开（或激活）车牌字母触摸面板：双击编号框触发。</summary>
    internal void ShowKeypad()
    {
        if (_keypad != null && !_keypad.IsDisposed)
        {
            _keypad.BringToFront();   // 已打开 → 置顶，不重复开窗
            return;
        }

        // 双击默认会选中一段文本：首次点选不应意外替换，取消选中并改为末尾追加
        if (_txtVehicle.SelectionLength > 0)
        {
            _txtVehicle.SelectionLength = 0;
            _txtVehicle.SelectionStart = _txtVehicle.TextLength;
        }

        _keypad = new PlateKeypadForm();
        _keypad.CharPicked += InsertPlateChar;
        _keypad.FormClosed += (_, _) => _keypad = null;
        _keypad.PlaceNear(_txtVehicle);
        _keypad.Show(this);   // 非模态：编号框保持焦点，可连续点选
    }

    /// <summary>
    /// 在编号框当前光标处插入字符（有选中则替换选中部分），光标右移。
    /// 面板按钮设置了 ControlStyles.Selectable=false 不抢焦点，
    /// SelectionStart 始终是编号框的真实光标；物理键盘输入同样有效。
    /// </summary>
    internal void InsertPlateChar(string s)
    {
        int start = _txtVehicle.SelectionStart;
        int len = _txtVehicle.SelectionLength;
        _txtVehicle.Text = _txtVehicle.Text.Remove(start, len).Insert(start, s);
        _txtVehicle.SelectionStart = start + s.Length;
        _txtVehicle.SelectionLength = 0;
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