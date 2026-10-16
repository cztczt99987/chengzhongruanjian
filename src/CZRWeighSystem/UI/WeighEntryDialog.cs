using System.Text.RegularExpressions;
using CZRWeighSystem.Core;

namespace CZRWeighSystem.UI;

/// <summary>
/// 称重录入弹窗（仿老软件链式流程：确认 → 保存 → 打印 → 继续）。
/// 点首页「空车去皮 / 重车过磅」弹出，左侧米黄票据按网格两列布局：
/// 左列固定字段（车牌号码/地主姓名/地主电话/司机电话/货主姓名/货主电话），
/// 右列实时毛/皮/净预览，净重下方空一格后再放品种与单价（表格由 8 行压到 6 行）；
/// 右栏为四步链式按钮，按顺序逐步解锁：
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
    /// <summary>保存成功的记录（打印用）</summary>
    private WeighRecord? _savedRecord;
    /// <summary>本次弹窗期间是否保存过记录（关闭时决定要不要让首页刷新列表）</summary>
    private bool _savedAny;
    /// <summary>关闭收尾是否已执行（防 FormClosing 与 btnExit 重复触发刷新）</summary>
    private bool _closed;
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

    /// <summary>
    /// 弹窗关闭时回调（参数=本次是否保存过记录）。
    /// 由 FormClosing 统一触发，因此点标题栏的叉与点「退出」按钮行为一致。
    /// （命名避开 Form.OnClosed 事件）
    /// </summary>
    internal Action<bool>? OnDialogClosed;

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
    private TextBox _txtCargoOwnerPhone = null!; // 货主电话（ext_json 固定键）
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
        Size = new Size(734, 452);   // 紧凑版：票据 470×390 + 右栏 205 宽，再压行高后的窗体
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

        _txtVehicle.TextChanged += (_, _) =>
        {
            ResetFlow();                  // 车号一变即作废本次确认：票面清空、须重新点"确认"
            TryAutoFill();                // 回填上次信息
        };
        _cboPrefix.SelectedIndexChanged += (_, _) =>
        {
            ResetFlow();                  // 省份简称也属于车号的一部分，同样需重新确认
            RefreshPlateSuggest();        // 省份变化 → 补全候选按新省份重建
            TryAutoFill();
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
        // 关闭收尾统一走 FormClosing：标题栏的叉不会设 DialogResult（默认 Cancel），
        // 只有「退出」按钮设 OK，导致点叉离开时首页记录列表不刷新。
        // 这里改成"保存过就通知首页刷新"，不论用叉还是用「退出」按钮都一致。
        FormClosing += (_, _) =>
        {
            if (_closed) return;
            _closed = true;
            if (_savedAny) DialogResult = DialogResult.OK;
            OnDialogClosed?.Invoke(_savedAny);
        };
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
            Location = new Point(12, 12),
            Size = new Size(470, 390),   // 紧凑版：表格压到 6 行后票据同步压低
            BackColor = Color.FromArgb(255, 255, 224),
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left,
        };

        // 抬头：公司名 + 过磅单类型 + 日期 + 流水号
        ticket.Controls.Add(new Label
        {
            Text = _cfg.CompanyName,
            Font = new Font("Microsoft YaHei UI", 13F, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleCenter,
            Location = new Point(0, 6),
            Size = new Size(470, 24),
        });
        _lblTicketType = new Label
        {
            Text = (_isTare ? "皮重" : "毛重") + _cfg.SlipHeader,   // 票据副标题取综合设置的磅单抬头
            Font = new Font("Microsoft YaHei UI", 10F),
            TextAlign = ContentAlignment.MiddleCenter,
            Location = new Point(0, 30),
            Size = new Size(470, 20),
        };
        ticket.Controls.Add(_lblTicketType);
        ticket.Controls.Add(new Label
        {
            Text = DateTime.Now.ToString("yyyy 年 M 月 d 日"),
            AutoSize = true,
            Location = new Point(18, 54),
            Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold),
        });
        _lblSerialNo = new Label
        {
            Text = "No：",
            AutoSize = true,
            Location = new Point(250, 54),
            Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold),
        };
        ticket.Controls.Add(_lblSerialNo);

        // ---- 固定字段表格：左列录入字段，右列毛/皮/净实时预览，网格线仿老过磅单 ----
        // 高度由行内容自适应（AutoSize）：固定高度在高 DPI 缩放下与行高总和产生取整误差，
        // 空间不足时 TLP 会压缩靠后的行（用户实机"单价行下方显示不全"即第 7 行被压）
        var grid = new TableLayoutPanel
        {
            Location = new Point(18, 80),
            ColumnCount = 4,
            RowCount = 6,
            CellBorderStyle = TableLayoutPanelCellBorderStyle.Single,
            BackColor = ticket.BackColor,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };
        _grid = grid;   // 提为字段供布局测试断言（不压住下方司磅员框）
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 64));   // 字段名
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 200));  // 输入框（车牌行含省份下拉）
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 50));   // 重量名/品种/单价名
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));  // 重量值 + 品种/单价输入
        for (int i = 0; i < 6; i++)
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));

        // 单元格控件统一留 1px 内边距：避免控件盖住 TableLayoutPanel 的网格线
        Label Head(string t) => new()
        {
            Text = t,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold),
            BackColor = ticket.BackColor,
            Margin = new Padding(1),
        };
        Label ValueCell() => new()
        {
            Text = "--",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleRight,
            Font = new Font("Consolas", 11F, FontStyle.Bold),
            BackColor = Color.White,
            Margin = new Padding(1),
            Padding = new Padding(0, 0, 4, 0),
        };
        // 11 号字 + 上下 4px 边距 → 输入框约 30px 高，行高 38 下视觉饱满且不留空
        TextBox Input() => new()
        {
            Dock = DockStyle.Fill,
            Font = new Font("Microsoft YaHei UI", 11F),
            Margin = new Padding(5, 4, 5, 4),
        };

        // 行1：车牌号码（省份简称下拉 + 编号手输，带当天历史自动补全）｜ 毛重
        grid.Controls.Add(Head("车牌号码"), 0, 0);
        var platePanel = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Margin = new Padding(2, 3, 2, 3) };
        // 省份选择器：点击弹出 8 列×4 行网格，避免 31 个省份拉成超长单列下拉
        _cboPrefix = new ProvincePicker
        {
            Font = new Font("Microsoft YaHei UI", 11F),
            Location = new Point(1, 1),
            Size = new Size(64, 26),   // 宽度维持 64：文字可用宽 ≥ 2 倍单字宽，"黑"字不被右缘裁切
        };
        // 首项为"无省份"，默认选中综合设置的"默认省份"
        int idx = ProvincePicker.Items.IndexOf(_cfg.DefaultProvince);
        _cboPrefix.SelectedIndex = idx > 0 ? idx : 0;
        _txtVehicle = new TextBox
        {
            Location = new Point(66, 1),    // 紧贴省份框右侧，避免遮盖
            Size = new Size(128, 26),       // 66+128=194 ≤ 面板可用宽 196
            Font = new Font("Microsoft YaHei UI", 11F),
            // 当天保存过的车牌：输入一半时在下拉中出现（按编号前缀建议）
            AutoCompleteMode = AutoCompleteMode.SuggestAppend,
            AutoCompleteSource = AutoCompleteSource.CustomSource,
        };
        platePanel.Controls.Add(_cboPrefix);
        platePanel.Controls.Add(_txtVehicle);
        // 老软件交互：双击编号框（字母位置）弹出 A-Z 触摸面板；点选一个字符后面板自动关闭
        _txtVehicle.DoubleClick += (_, _) => ShowKeypad();
        new ToolTip().SetToolTip(_txtVehicle, "双击打开字母选择面板（点选一个字符后自动关闭）");
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

        // 行4：司机电话（右列留空——净重与品种之间隔一个空行）
        grid.Controls.Add(Head("司机电话"), 0, 3);
        _txtDriverPhone = Input();
        grid.Controls.Add(_txtDriverPhone, 1, 3);

        // 行5：货主姓名 ｜ 品种（存记录 Goods 列）
        grid.Controls.Add(Head("货主姓名"), 0, 4);
        _txtCargoOwner = Input();
        grid.Controls.Add(_txtCargoOwner, 1, 4);
        grid.Controls.Add(Head("品种"), 2, 4);
        _txtVariety = Input();
        grid.Controls.Add(_txtVariety, 3, 4);

        // 行6：货主电话（ext_json 固定键）｜ 单价（ext_json 固定键；只允许输入数字与小数点）
        grid.Controls.Add(Head("货主电话"), 0, 5);
        _txtCargoOwnerPhone = Input();
        grid.Controls.Add(_txtCargoOwnerPhone, 1, 5);
        grid.Controls.Add(Head("单价"), 2, 5);
        _txtUnitPrice = Input();
        _txtUnitPrice.KeyPress += (_, e) =>
        {
            // 拦截数字/小数点/控制键（退格）之外的字符，避免录成文字后无法参与金额计算
            if (!char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar) && e.KeyChar != '.')
                e.Handled = true;
        };
        grid.Controls.Add(_txtUnitPrice, 3, 5);
        ticket.Controls.Add(grid);

        // 用户手动改任一可回填字段 → 摘除其"自动填入"标记，之后换车号不再把它当旧车信息清掉
        foreach (var box in new[] { _txtVariety, _txtOwner, _txtOwnerPhone, _txtDriverPhone, _txtCargoOwner, _txtCargoOwnerPhone, _txtUnitPrice })
            box.TextChanged += (_, _) => { if (!_autoFilling) _autoFilledFields.Remove(box); };

        // 表格下方：司磅员（当前登录用户，自动记录）。
        // 框顶与表格底留 18px（6 行表格底约 291 → 框顶 309）：高 DPI 缩放取整会吃掉小间隙，
        // 间隙过小时表格底边框线会压住框顶
        ticket.Controls.Add(new Label
        {
            Text = "司磅员：",
            AutoSize = true,
            Location = new Point(18, 314),
            Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold),
        });
        _txtOperator = new TextBox
        {
            Location = new Point(92, 309), Size = new Size(140, 28),
            Font = new Font("Microsoft YaHei UI", 11F),
            ReadOnly = true, Text = AppSession.DisplayName,
        };
        ticket.Controls.Add(_txtOperator);

        // 底部：配对/新车提示
        _lblPairHint = new Label
        {
            Text = "",
            AutoSize = true,
            Location = new Point(18, ticket.Height - 30),
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
            Location = new Point(494, 12),
            Size = new Size(205, 30),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.White,
            Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold),
        };
        new ToolTip().SetToolTip(btnToggle, "在空车去皮（皮重）与重车过磅（毛重）之间切换");
        btnToggle.Click += (_, _) => ToggleMode();
        Controls.Add(btnToggle);

        _lblPanelTitle = new Label
        {
            Text = _isTare ? "本次皮重" : "本次毛重",
            Font = new Font("Microsoft YaHei UI", 12F, FontStyle.Bold),
            ForeColor = Color.White,
            AutoSize = true,
            Location = new Point(498, 46),
        };
        Controls.Add(_lblPanelTitle);

        _lblCurrent = new Label
        {
            Text = "0 " + _cfg.UnitDisplay,
            Font = new Font("Consolas", 22F, FontStyle.Bold),
            ForeColor = Color.OrangeRed,
            BackColor = Color.Black,
            BorderStyle = BorderStyle.FixedSingle,
            TextAlign = ContentAlignment.MiddleCenter,
            Location = new Point(494, 72),
            Size = new Size(205, 54),
        };
        Controls.Add(_lblCurrent);

        // 上次空车去皮信息（仅空车去皮模式可见：本车最近一次已完成皮重与时间）
        Label Info(string t, int y) => new()
        {
            Text = t, AutoSize = true, Location = new Point(486, y),
            ForeColor = Color.White, Font = new Font("Microsoft YaHei UI", 9.5F),
        };
        Label Val(int y) => new()
        {
            Text = "--", AutoSize = true,
            Location = new Point(556, y),
            ForeColor = Color.FromArgb(255, 239, 130),
            Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold),
        };
        _lblLastWeightTitle = Info("上次重量：", 132);
        _lblLastWeight = Val(132);
        _lblLastTimeTitle = Info("上次时间：", 154);
        _lblLastTime = Val(154);
        Controls.AddRange([_lblLastWeightTitle, _lblLastWeight, _lblLastTimeTitle, _lblLastTime]);
        UpdateLastTareVisibility();   // 重车过磅模式不显示该区块

        // ===== 链式流程按钮（确认 ↓ 保存 ↓ 打印 ↓ 继续，仿老软件逐步解锁）=====
        Button ChainButton(string text, int y, int h) => new()
        {
            Text = text,
            Location = new Point(494, y),
            Size = new Size(205, h),
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Microsoft YaHei UI", 12F, FontStyle.Bold),
        };
        Label Arrow(int y) => new()
        {
            Text = "↓",
            TextAlign = ContentAlignment.MiddleCenter,
            Location = new Point(494, y),
            Size = new Size(205, 12),
            ForeColor = Color.Lime,
            Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold),
        };

        _btnConfirm = ChainButton("确 认", 182, 36);
        _btnSave = ChainButton("保 存", 234, 36);
        _btnPrint = ChainButton("打 印", 286, 36);
        _btnContinue = ChainButton("继 续", 338, 32);
        _btnConfirm.Click += (_, _) => Confirm();
        _btnSave.Click += (_, _) => DoSave();
        _btnPrint.Click += (_, _) => DoPrint();
        _btnContinue.Click += (_, _) => DoContinue();

        // 退出：返回主界面（FormClosing 按"是否保存过"决定首页是否刷新记录/统计）
        var btnExit = new Button
        {
            Text = "退 出",
            Location = new Point(494, 376),
            Size = new Size(205, 26),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(235, 235, 235),
            Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold),
        };
        btnExit.Click += (_, _) => { DialogResult = DialogResult.OK; Close(); };

        Controls.Add(_btnConfirm);
        Controls.Add(Arrow(220));
        Controls.Add(_btnSave);
        Controls.Add(Arrow(272));
        Controls.Add(_btnPrint);
        Controls.Add(Arrow(324));
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
        ResetFlow();   // 内部已刷新票面（票面清空、回到"确认"步）
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

    /// <summary>
    /// 放弃链式进度：清空确认/保存状态，回到"确认"步并刷新票面。
    /// 刷新不可省——票面右列的毛/皮/净在"确认"后才填写，光置空状态而不重绘会残留上一辆车的数字
    /// （用户要求：换车号后毛/皮/净一律不显示，等重新点"确认"再显示）。
    /// </summary>
    private void ResetFlow()
    {
        _confirmedKg = null;
        _confirmedFirst = null;
        _savedRecord = null;
        SetStep(0);
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
    /// 完整车号命中当天已保存记录 → 回填最新一条的品种/地主姓名/地主电话/司机电话/货主姓名/货主电话/单价，
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
                FillAuto(_txtCargoOwnerPhone, ext.GetValueOrDefault("货主电话", ""));
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
    /// 票面在"确认"锁定后固定展示锁定读数、在"保存"成功后固定展示已存结果，
    /// 均不再跟随实时重量（车下磅归零后仍应看到本次值，被刷成 0 会让操作员误读）。
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
                // 一次磅：重量按操作模式入库（空车去皮→皮重列、重车过磅→毛重列），
                // 展示在该重量实际所在的列上；配对时再按大小归位毛/皮
                double stored = _savedRecord.FirstWeightKg ?? 0;
                _lblGrossCell.Text = _savedRecord.FirstWeighIsTare ? "--" : _cfg.FormatWeight(stored);
                _lblTareCell.Text = _savedRecord.FirstWeighIsTare ? _cfg.FormatWeight(stored) : "--";
                _lblNetCell.Text = "--";
                _lblPairHint.Text = $"[{_savedRecord.VehicleNo}] 一次磅已保存，等待二次磅配对结算";
            }
            _lblSerialNo.Text = "No：" + _savedRecord.SerialNo;
        }
        else if (_confirmedKg != null)
        {
            // 确认已锁定读数：票面固定展示锁定值，不再跟随实时重量（车下磅归零也不变）；
            // 保存时按该锁定值入库（见 DoSave）
            double lockedKg = _confirmedKg.Value;
            double? firstKg = _confirmedFirst?.FirstWeightKg;

            // 本次锁定值进当前操作模式对应行；已配对一次磅的重量进对侧行——
            // 两项合起来才是本次的毛/皮对，所以净重必须由这两个数算，不能只看单边
            double? grossKg = null, tareKg = null;
            if (_isTare)
            {
                tareKg = lockedKg;
                if (firstKg is { } f) grossKg = f > lockedKg ? f : null;
            }
            else
            {
                grossKg = lockedKg;
                if (firstKg is { } f) tareKg = lockedKg > f ? f : null;
            }

            _lblGrossCell.Text = grossKg.HasValue ? _cfg.FormatWeight(grossKg.Value) : "--";
            _lblTareCell.Text = tareKg.HasValue ? _cfg.FormatWeight(tareKg.Value) : "--";
            // 净重 = 毛重 − 皮重；毛皮未配对齐（尤其只称过一次）时为 null → 显示 "--"
            _lblNetCell.Text = NetCellText(grossKg, tareKg);

            if (_confirmedFirst != null)
            {
                _lblSerialNo.Text = "No：" + _confirmedFirst.SerialNo;
                _lblPairHint.Text = $"[{no}] 有未完成的二次磅，保存时将自动配对结算";
            }
            else
            {
                _lblSerialNo.Text = "No：";
                _lblPairHint.Text = no.Length > 0 ? $"[{no}] 新车辆，本次为第一次称重" : "";
            }
        }
        else
        {
            var first = no.Length == 0 ? null
                : _manager.GetUnfinishedList().FirstOrDefault(r => r.VehicleNo == no);

            // 本次尚未"确认"：票面右列不显示实时重量，毛/皮/净一律为 "--"
            // （2026-10 用户要求：未点确认不显示重量；确认后才把锁定的读数填进对应行）
            _lblGrossCell.Text = "--";
            _lblTareCell.Text = "--";
            _lblNetCell.Text = "--";

            // 先填本车历史皮重（毛重匹配时操作员输入完车号就要看到它）；
            // 若该车另有未完成记录，则用那条记录的重量覆盖——此时票面必须显示"即将配对结算"的那个值，
            // 否则会与下方"A 有未完成的二次磅，保存时将自动配对结算"的提示自相矛盾
            // （现场反馈：列表里未完成记录皮重 0.18，弹窗却显示历史皮重 0.12）。
            ShowArchivedTare(no);
            if (first is { FirstWeightKg: { } pendingKg })
                _lblTareCell.Text = _cfg.FormatWeight(pendingKg);

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

    /// <summary>
    /// 净重 = 毛重 − 皮重：两个数值都填写/存在时才计算，缺任一项返回 null（票据显示 "--"）。
    /// 空车去皮模式下"这次皮重 + 该车已过磅毛重"也按此规则取净重。
    /// </summary>
    internal static double? NetKg(double? grossKg, double? tareKg)
    {
        if (grossKg == null || tareKg == null) return null;
        return grossKg.Value - tareKg.Value;
    }

    /// <summary>
    /// 车号命中本车已完成皮重时，把该皮重填进票据右侧的"皮重"栏（未确认状态下的参考值）。
    /// 毛重匹配（重车过磅）时，操作员输入完车号即能直接看到这辆车的皮重；
    /// 无历史皮重（或车号为空）则保持初始化时清好的 "--"。
    /// 注意：本方法只认"已完成"记录的历史皮重；若该车还有未完成记录，
    /// 调用方需用那条记录的重量覆盖（见 UpdateWeight 未确认分支），否则显示值与配对提示矛盾。
    /// </summary>
    private void ShowArchivedTare(string no)
    {
        if (GetLastTare(no)?.TareKg is { } tareKg)
            _lblTareCell.Text = _cfg.FormatWeight(tareKg);
    }

    /// <summary>毛/皮两个数值 → 净重单元格文本（任一为 null 时净重显示 "--"）。</summary>
    private string NetCellText(double? grossKg, double? tareKg)
    {
        double? netKg = NetKg(grossKg, tareKg);
        return netKg.HasValue ? _cfg.FormatWeight(netKg.Value) : "--";
    }

    // ================================================================
    // 链式流程：确认 → 保存 → 打印 → 继续
    // ================================================================

    /// <summary>步骤1【确认】：校验车号/稳定/超载，锁定当前稳定读数并识别是否二次磅；
    /// 之后票面展示与保存均用锁定值，车辆下磅（归零）不影响。</summary>
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
        // 读数前置校验：0 是无效重量，先于"稳定"判定——车没上磅时读数未稳定，
        // 若先判稳定会提示"重量未稳定"，掩盖了真正的原因（车不在磅上）。
        // 用 CurrentKg 而非 StableKg：后者只在稳定时才写入（StabilityFilter），
        // 读数跳动期间恒为 0，会把"有车但未稳定"误报成"重量为 0"。
        if (_manager.CurrentKg <= 0)
        {
            InfoBox("当前重量为 0，请确认车辆已上磅", "提示", false);
            return;
        }
        if (!_manager.IsStableForSave)
        {
            InfoBox("重量未稳定，请等待稳定后再确认", "提示", false);
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
        UpdateWeight();   // 立即把锁定的读数画到票面（否则要等 200ms 定时器刷新）
    }

    /// <summary>步骤2【保存】：有确认的待配对记录则二次磅结算，否则第一次称重（无弹窗）。</summary>
    private void DoSave()
    {
        if (_step != 1 || _confirmedKg == null) return;

        string no = ComposeVehicleNo();
        var ext = CollectExt();

        if (_confirmedFirst != null)
        {
            // 二次磅结算（按确认锁定的读数结算，并按大小分配毛/皮）
            var (ok, msg, record) = _manager.SaveSecondWeigh(
                _confirmedFirst, null, _txtOperator.Text, _confirmedKg);
            if (!ok) { InfoBox(msg, "提示", false); return; }

            _savedRecord = record;
            OnSaved?.Invoke(record!.SerialNo);
        }
        else
        {
            // 第一次称重（品种存 Goods 列；扣重/备注已从表单移除，存 null）
            // isTare：空车去皮→重量入皮重列、重车过磅→重量入毛重列
            var (ok, msg, newRecord) = _manager.SaveFirstWeigh(
                no, _txtVariety.Text, null, null,
                null, null, null,
                null, _txtOperator.Text, ext, _confirmedKg, _isTare);
            if (!ok) { InfoBox(msg, "提示", false); return; }

            _savedRecord = newRecord;
            OnSaved?.Invoke(newRecord!.SerialNo);
        }
        _savedAny = true;      // 关窗时据此让首页刷新记录列表/统计
        _lastTareKey = null;   // 保存后历史皮重可能变化，下次查询重新取库
        SetStep(2);   // 保存完成 → 解锁"打印"
        UpdateWeight();   // 立即把已存结果显示到票面（否则要等 200ms 定时器刷新）
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

        // 先清状态再清输入框：清空车号会触发 TextChanged → UpdateWeight，
        // 若 _savedRecord 尚未清空，已保存记录的结果会被重新画回票面。
        _confirmedKg = null;
        _confirmedFirst = null;
        _savedRecord = null;

        _txtVehicle.Clear();
        _txtVariety.Clear();
        _txtOwner.Clear();
        _txtOwnerPhone.Clear();
        _txtDriverPhone.Clear();
        _txtCargoOwner.Clear();
        _txtCargoOwnerPhone.Clear();
        _txtUnitPrice.Clear();
        _autoFilledFields.Clear();   // 表单整体清空：脏标记一并复位，不依赖各 Clear 的事件顺序

        // 当天记录加入刚保存的车号（下一辆车输入时自动补全/回填候选）
        _todayRecords = _plateRepo.Query(DateTime.Today, null, "", "", "", "")
            .Where(r => r.Status != "已作废").ToList();
        RefreshPlateSuggest();

        ResetFlow();          // 回到"确认"步并刷新票面（毛/皮/净清空）
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
        _keypad.Show(this);   // 非模态；点选一个字符后面板自动关闭，焦点交还编号框
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
        Add("货主电话", _txtCargoOwnerPhone);
        Add("单价", _txtUnitPrice);
        return ext;
    }
}