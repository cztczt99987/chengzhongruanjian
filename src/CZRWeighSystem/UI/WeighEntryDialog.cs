using CZRWeighSystem.Core;

namespace CZRWeighSystem.UI;

/// <summary>
/// 称重录入弹窗（2026-09-30 二次改版：首页信息录入区移入弹窗）。
/// 点首页「空车去皮 / 重车过磅」弹出，操作员在票据样式表单中填写车号等信息；
/// 右侧实时显示当前重量、待配对一次磅（上次重量/时间）与预计净重；
/// 保存走统一皮/毛流程（车号有待配对记录时自动二次磅结算）。
/// </summary>
public class WeighEntryDialog : Form
{
    private readonly WeighManager _manager;
    private readonly bool _isTare;
    private readonly AppConfig _cfg = AppConfig.Current;
    private readonly Database.CustomFieldRepository _fieldRepo = new();
    private readonly List<(CustomField Def, Control Input)> _customInputs = [];

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
    private TextBox _txtGoods = null!;       // 货物
    private TextBox _txtDeduct = null!;      // 扣重
    private TextBox _txtRemark = null!;      // 备注
    private TextBox _txtOperator = null!;    // 司磅员（当前登录用户，只读）
    private Label _lblSerialNo = null!;      // No：待配对时显示一次磅流水号
    private Label _lblPairHint = null!;      // 配对/新车提示
    private Label _lblCurrent = null!;       // 实时重量
    private Label _lblLastWeight = null!;    // 上次重量（待配对一次磅）
    private Label _lblLastTime = null!;      // 上次时间
    private Label _lblNetPreview = null!;    // 预计净重
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

    // ---------- 票据样式信息录入区（左侧浅黄过磅单） ----------
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

        Label L(string t, int y) => new()
        { Text = t, AutoSize = true, Location = new Point(25, y + 4), Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold) };

        // 行1：车牌号码（只读 + 软键盘选牌）
        ticket.Controls.Add(L("车牌号码：", 130));
        _txtVehicle = new TextBox { Location = new Point(115, 130), Size = new Size(200, 28), ReadOnly = true };
        var btnVehicle = new Button
        {
            Text = "选择车牌", Location = new Point(320, 129), Size = new Size(92, 30),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(0, 122, 204), ForeColor = Color.White,
        };
        btnVehicle.Click += (_, _) =>
        {
            using var f = new VehiclePadDialog();
            if (f.ShowDialog(this) == DialogResult.OK && f.ResultVehicle != null)
                _txtVehicle.Text = f.ResultVehicle;
        };
        ticket.Controls.AddRange(new Control[] { _txtVehicle, btnVehicle });

        // 行2：货物
        ticket.Controls.Add(L("货物：", 172));
        _txtGoods = new TextBox { Location = new Point(115, 172), Size = new Size(320, 28) };
        ticket.Controls.Add(_txtGoods);

        // 行3：扣重
        ticket.Controls.Add(L("扣重：", 214));
        _txtDeduct = new TextBox { Location = new Point(115, 214), Size = new Size(150, 28) };
        ticket.Controls.Add(_txtDeduct);
        ticket.Controls.Add(new Label
        { Text = _cfg.Unit, AutoSize = true, Location = new Point(272, 218) });

        // 行4：备注
        ticket.Controls.Add(L("备注：", 256));
        _txtRemark = new TextBox { Location = new Point(115, 256), Size = new Size(320, 28) };
        ticket.Controls.Add(_txtRemark);

        // 行5：司磅员（当前登录用户，自动记录）
        ticket.Controls.Add(L("司磅员：", 298));
        _txtOperator = new TextBox
        {
            Location = new Point(115, 298), Size = new Size(200, 28),
            ReadOnly = true, Text = AppSession.DisplayName,
        };
        ticket.Controls.Add(_txtOperator);

        // 自定义字段从行 6 起延续排布
        var fields = _fieldRepo.GetAll(enabledOnly: true);
        for (int i = 0; i < fields.Count; i++)
        {
            var def = fields[i];
            int y = 340 + i * 40;
            var lbl = new Label
            {
                Text = def.Name + "：", AutoSize = true,
                Location = new Point(25, y + 4),
                Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold),
            };
            Control input = def.FieldType switch
            {
                "下拉" => NewCombo(115, y, 320, def.OptionList),
                "日期" => new DateTimePicker
                {
                    Location = new Point(115, y), Size = new Size(320, 28),
                    Format = DateTimePickerFormat.Custom, CustomFormat = "yyyy-MM-dd",
                },
                _ => new TextBox { Location = new Point(115, y), Size = new Size(320, 28) },
            };
            ticket.Controls.AddRange([lbl, input]);
            _customInputs.Add((def, input));
        }

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
    /// 刷新实时重量与配对信息：稳定绿/动态橙；车号匹配到未完成一次磅时
    /// 显示其流水号/上次重量/上次时间与预计净重，保存按钮切换为"保存并结算"。
    /// </summary>
    private void UpdateWeight()
    {
        _lblCurrent.Text = _cfg.FormatWeight(_manager.CurrentKg);
        _lblCurrent.ForeColor = _manager.IsStable ? Color.LimeGreen : Color.OrangeRed;

        string no = _txtVehicle.Text.Trim();
        var first = no.Length == 0 ? null
            : _manager.GetUnfinishedList().FirstOrDefault(r => r.VehicleNo == no);

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

        double? deduct = ParseDeduct();
        if (_txtDeduct.TextLength > 0 && deduct == null) return;

        // 自定义字段采集与校验（必填/数字格式）
        var ext = CollectExt();
        if (ext == null) return;

        var first = no.Length == 0 ? null
            : _manager.GetUnfinishedList().FirstOrDefault(r => r.VehicleNo == no);
        string weightText = _cfg.FormatWeight(_manager.StableKg);

        if (first != null)
        {
            // 二次磅结算
            if (!ConfirmBox($"{action}（第二次称重·结算）\n车号：{first.VehicleNo}  " +
                $"一次磅：{_cfg.FormatWeight(first.GrossKg ?? 0)}  本次：{weightText}", "确认操作"))
                return;

            var (ok, msg, record) = _manager.SaveSecondWeigh(first, deduct, _txtOperator.Text);
            InfoBox(msg, ok ? "结算成功" : "提示", ok);
            if (!ok) return;

            OnSaved?.Invoke(record!.SerialNo);
            TryPrint(record!);
            DialogResult = DialogResult.OK;
        }
        else
        {
            // 第一次称重
            if (!ConfirmBox($"{action}（第一次称重）\n车号：{_txtVehicle.Text}  重量：{weightText}", "确认操作"))
                return;

            var (ok, msg, newRecord) = _manager.SaveFirstWeigh(
                _txtVehicle.Text, _txtGoods.Text, null, null,
                null, null, _txtRemark.Text,
                deduct, _txtOperator.Text, ext);
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

    /// <summary>解析扣重输入；留空 = 无扣重；非法输入时提示并返回 null。</summary>
    private double? ParseDeduct()
    {
        if (_txtDeduct.TextLength == 0) return null;
        if (!double.TryParse(_txtDeduct.Text, out double v) || v < 0)
        {
            InfoBox("扣重格式不正确（应为不小于 0 的数字）", "提示", false);
            return null;
        }
        // 输入按显示单位换算为千克
        return _cfg.Unit == "t" ? v * 1000 : v;
    }

    /// <summary>采集自定义字段值；校验失败（必填缺失/数字格式错误）提示并返回 null。</summary>
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
                    InfoBox($"[{def.Name}] 为必填项", "提示", false);
                    input.Focus();
                    return null;
                }
                continue;
            }
            if (def.FieldType == "数字" && !double.TryParse(val, out _))
            {
                InfoBox($"[{def.Name}] 需填写数字", "提示", false);
                input.Focus();
                return null;
            }
            ext[def.Name] = val;
        }
        return ext;
    }
}
