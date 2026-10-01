using CZRWeighSystem.Core;

namespace CZRWeighSystem.UI;

/// <summary>
/// 称重录入弹窗（2026-09-30 三次改版：票据区仿老软件过磅单表格样式）。
/// 点首页「空车去皮 / 重车过磅」弹出，左侧米黄票据按网格两列布局：
/// 左列固定字段（车牌号码/地主姓名/地主电话/司机电话/货主姓名/品种），
/// 右列实时毛/皮/净预览；右栏保留实时重量、上次重量/时间、预计净重与保存按钮。
/// 地主姓名等固定字段以固定键写入记录 ext_json（无需改表，磅单模板可直接引用），
/// 品种存 Goods 列；保存走统一皮/毛流程（车号有待配对记录时自动二次磅结算）。
/// </summary>
public class WeighEntryDialog : Form
{
    private readonly WeighManager _manager;
    private readonly bool _isTare;
    private readonly AppConfig _cfg = AppConfig.Current;

    // ===== 对话框出口（internal 供测试注入静默实现；正常流程用 MessageBox）=====
    /// <summary>确认框：返回 true=确认。测试注入后可静默自动确认。</summary>
    internal Func<string, string, bool> ConfirmBox = (msg, title) =>
        MessageBox.Show(msg, title, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
    /// <summary>结果提示框。测试注入后记录消息不弹窗。</summary>
    internal Action<string, string, bool> InfoBox = (msg, title, ok) =>
        MessageBox.Show(msg, title, MessageBoxButtons.OK,
            ok ? MessageBoxIcon.Information : MessageBoxIcon.Warning);

    /// <summary>保存成功后回调（参数=流水号；主界面用于抓拍留证）。</summary>
    internal Action<string>? OnSaved;

    // ===== 控件 =====
    private TextBox _txtVehicle = null!;     // 车牌号码（只读，点按钮弹软键盘选牌）
    private TextBox _txtVariety = null!;     // 品种（存记录 Goods 列）
    private TextBox _txtOwner = null!;       // 地主姓名（ext_json 固定键）
    private TextBox _txtOwnerPhone = null!;  // 地主电话（ext_json 固定键）
    private TextBox _txtDriverPhone = null!; // 司机电话（ext_json 固定键）
    private TextBox _txtCargoOwner = null!;  // 货主姓名（ext_json 固定键）
    private TextBox _txtOperator = null!;    // 司磅员（当前登录用户，只读）
    private Label _lblSerialNo = null!;      // No：待配对时显示一次磅流水号
    private Label _lblPairHint = null!;      // 配对/新车提示
    private Label _lblGrossCell = null!;     // 票据右列：毛重预览
    private Label _lblTareCell = null!;      // 票据右列：皮重预览
    private Label _lblNetCell = null!;       // 票据右列：净重预览
    private Label _lblCurrent = null!;       // 实时重量（右栏大屏）
    private Label _lblLastWeight = null!;    // 上次重量（待配对一次磅）
    private Label _lblLastTime = null!;      // 上次时间
    private Label _lblNetPreview = null!;    // 预计净重（右栏）
    private Button _btnSave = null!;         // 保存（待配对时文字为"保存并结算"）
    private readonly System.Windows.Forms.Timer _tick = new() { Interval = 200 };

    public WeighEntryDialog(WeighManager manager, bool isTare, string? prefillVehicle = null)
    {
        _manager = manager;
        _isTare = isTare;
        Text = (isTare ? "空车去皮" : "重车过磅") + $"（本次按{(isTare ? "皮" : "毛")}重保存）";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(950, 640);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        Font = new Font("Microsoft YaHei UI", 10F);
        // 高 DPI 下按 DPI 缩放布局，避免文字截断与控件错位
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = Color.FromArgb(0, 145, 60);

        BuildTicket();
        BuildWeightPanel();

        _txtVehicle.Text = prefillVehicle ?? "";
        _txtVehicle.TextChanged += (_, _) => UpdateWeight();   // 车号变化即时刷新配对信息
        UpdateWeight();

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
        ticket.Controls.Add(new Label
        {
            Text = (_isTare ? "皮重" : "毛重") + "过磅单",
            Font = new Font("Microsoft YaHei UI", 12F),
            TextAlign = ContentAlignment.MiddleCenter,
            Location = new Point(0, 52),
            Size = new Size(650, 28),
        });
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
            Size = new Size(600, 306),
            ColumnCount = 4,
            RowCount = 6,
            CellBorderStyle = TableLayoutPanelCellBorderStyle.Single,
            BackColor = ticket.BackColor,
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 88));   // 字段名
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 248));  // 输入框
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 84));   // 重量名
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 176));  // 重量值
        for (int i = 0; i < 6; i++)
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));

        Label Head(string t) => new()
        {
            Text = t,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold),
            BackColor = ticket.BackColor,
            Margin = new Padding(0),
        };
        Label ValueCell() => new()
        {
            Text = "--",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleRight,
            Font = new Font("Consolas", 11F, FontStyle.Bold),
            BackColor = Color.White,
            Margin = new Padding(0),
            Padding = new Padding(0, 0, 6, 0),
        };
        TextBox Input() => new() { Dock = DockStyle.Fill, Margin = new Padding(4, 10, 4, 10) };

        // 行1：车牌号码（只读 + 软键盘选牌）｜ 毛重
        grid.Controls.Add(Head("车牌号码"), 0, 0);
        var platePanel = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Margin = new Padding(2) };
        _txtVehicle = new TextBox { Dock = DockStyle.Fill, ReadOnly = true };
        var btnVehicle = new Button
        {
            Text = "选择车牌",
            Dock = DockStyle.Right,
            Width = 88,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(0, 122, 204),
            ForeColor = Color.White,
            Font = new Font("Microsoft YaHei UI", 9F),
        };
        btnVehicle.Click += (_, _) =>
        {
            using var f = new VehiclePadDialog();
            if (f.ShowDialog(this) == DialogResult.OK && f.ResultVehicle != null)
                _txtVehicle.Text = f.ResultVehicle;
        };
        platePanel.Controls.Add(_txtVehicle);
        platePanel.Controls.Add(btnVehicle);
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
        ticket.Controls.Add(grid);

        // 表格下方：司磅员（当前登录用户，自动记录）
        ticket.Controls.Add(new Label
        {
            Text = "司磅员：",
            AutoSize = true,
            Location = new Point(25, 446),
            Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold),
        });
        _txtOperator = new TextBox
        {
            Location = new Point(113, 442), Size = new Size(200, 28),
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
        Controls.Add(new Label
        {
            Text = _isTare ? "本次皮重" : "本次毛重",
            Font = new Font("Microsoft YaHei UI", 14F, FontStyle.Bold),
            ForeColor = Color.White,
            AutoSize = true,
            Location = new Point(690, 22),
        });

        _lblCurrent = new Label
        {
            Text = "0 " + _cfg.Unit,
            Font = new Font("Consolas", 26F, FontStyle.Bold),
            ForeColor = Color.OrangeRed,
            BackColor = Color.Black,
            BorderStyle = BorderStyle.FixedSingle,
            TextAlign = ContentAlignment.MiddleCenter,
            Location = new Point(685, 58),
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
        Controls.Add(Info("上次重量：", 155));
        _lblLastWeight = Val(155);
        Controls.Add(_lblLastWeight);
        Controls.Add(Info("上次时间：", 185));
        _lblLastTime = Val(185);
        Controls.Add(_lblLastTime);
        Controls.Add(Info("预计净重：", 215));
        _lblNetPreview = Val(215);
        Controls.Add(_lblNetPreview);

        _btnSave = new Button
        {
            Text = "保存",
            Location = new Point(685, 280),
            Size = new Size(250, 56),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(235, 235, 235),
            Font = new Font("Microsoft YaHei UI", 14F, FontStyle.Bold),
        };
        _btnSave.Click += (_, _) => Save();
        Controls.Add(_btnSave);

        var btnCancel = new Button
        {
            Text = "取消",
            Location = new Point(685, 350),
            Size = new Size(250, 44),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(235, 235, 235),
            Font = new Font("Microsoft YaHei UI", 12F, FontStyle.Bold),
        };
        btnCancel.Click += (_, _) => Close();
        Controls.Add(btnCancel);
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
        _lblCurrent.ForeColor = _manager.IsStable ? Color.LimeGreen : Color.OrangeRed;

        string no = _txtVehicle.Text.Trim();
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
        string action = _isTare ? "空车去皮" : "重车过磅";
        string no = _txtVehicle.Text.Trim();

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
            if (!ConfirmBox($"{action}（第一次称重）\n车号：{_txtVehicle.Text}  重量：{weightText}", "确认操作"))
                return;

            var (ok, msg, newRecord) = _manager.SaveFirstWeigh(
                _txtVehicle.Text, _txtVariety.Text, null, null,
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
        return ext;
    }
}