namespace CZRWeighSystem.UI;

/// <summary>
/// 收费功能设置窗体（限管理员，参考行业"代磅收费"版式，仅此一种模式）：
/// 按净重吨数匹配重量分段取固定金额；分段固定金额填 0 表示该段按"其余每吨"标准计费；
/// 分段之外的重量按"其余收费每 吨 X 元"计费。保存到 config.json，下次结算立即生效。
/// </summary>
public class FeeSettingsDialog : Form
{
    private CheckBox _chkEnabled = null!;                 // 启用收费
    private NumericUpDown _numFallback = null!;           // 其余收费（元/吨）
    private readonly List<(NumericUpDown From, NumericUpDown To, NumericUpDown Amount)> _rows = [];
    private const int BracketRowCount = 5;                // 分段行数（与参考版式一致）

    public FeeSettingsDialog()
    {
        Text = "收费功能设置";
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;   // 高 DPI 下避免文字截断
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(580, 460);   // 底部预留说明文字（三行）与按钮的完整空间
        Font = new Font("Microsoft YaHei UI", 10F);

        BuildUi();
    }

    private void BuildUi()
    {
        var lblTitle = new Label
        {
            Text = "按重量区分收费：净重落入分段取固定金额，分段之外按每吨标准收费",
            AutoSize = true,
            Location = new Point(25, 18),
            ForeColor = Color.FromArgb(0, 102, 204),
        };
        Controls.Add(lblTitle);

        // ---- 启用收费 ----
        _chkEnabled = new CheckBox
        {
            Text = "启用收费（二次磅结算时按净重自动计算应收金额）",
            AutoSize = true,
            Location = new Point(25, 48),
        };
        Controls.Add(_chkEnabled);

        // ---- 其余收费每 X 元/吨 ----
        // 行 y=92：与上方"启用收费"复选框（AutoSize，高 DPI 下更高）拉开 10px 间距
        Controls.Add(new Label { Text = "其余收费每", AutoSize = true, Location = new Point(25, 96) });
        _numFallback = Num(0, 9999, 2);
        _numFallback.Location = new Point(112, 92);
        _numFallback.Size = new Size(88, 30);   // 两位小数值（如 10.00）在高 DPI 下也需要完整显示
        Controls.Add(_numFallback);
        Controls.Add(new Label
        {
            Text = "元/吨　（分段之外的重量都按此标准收费）",
            AutoSize = true,
            Location = new Point(208, 96),
        });

        // ---- 重量分段（5 行：起点 至 终点 吨 固定 X 元）----
        Controls.Add(new Label
        {
            Text = "重量分段（吨）",
            AutoSize = true,
            Location = new Point(112, 130),
            ForeColor = Color.Gray,
        });
        Controls.Add(new Label
        {
            Text = "固定金额（元）",
            AutoSize = true,
            Location = new Point(388, 130),
            ForeColor = Color.Gray,
        });
        for (int i = 0; i < BracketRowCount; i++)
        {
            int y = 160 + i * 36;

            var from = Num(0, 999);
            from.Location = new Point(112, y);
            Controls.Add(from);
            Controls.Add(new Label { Text = "至", AutoSize = true, Location = new Point(196, y + 4) });
            var to = Num(0, 999);
            to.Location = new Point(228, y);
            Controls.Add(to);
            Controls.Add(new Label { Text = "吨　固定", AutoSize = true, Location = new Point(312, y + 4) });
            var amount = Num(0, 99999, 2);
            amount.Location = new Point(388, y);
            amount.Size = new Size(94, 30);   // 两位小数值（如 10.00）在高 DPI 下也需要完整显示
            Controls.Add(amount);
            Controls.Add(new Label { Text = "元", AutoSize = true, Location = new Point(488, y + 4) });

            _rows.Add((from, to, amount));
        }

        // ---- 说明 ----
        var lblTip = new Label
        {
            Text = "净重匹配分段：含起点、不含终点；最后一行「至」填 0 表示不封顶。\r\n" +
                   "固定金额填 0 表示该段按上方「其余每吨」标准计费；\r\n" +
                   "如仅想按每吨收费，可把各段「固定金额」都改为 0。",
            AutoSize = true,
            Location = new Point(25, 344),
            ForeColor = Color.Gray,
        };
        Controls.Add(lblTip);

        // ---- 保存 / 取消 ----
        var btnSave = new Button
        {
            Text = "保 存",
            Location = new Point(330, 415),
            Size = new Size(110, 34),
            BackColor = Color.FromArgb(0, 122, 204),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
        };
        var btnCancel = new Button
        {
            Text = "退 出",
            Location = new Point(455, 415),
            Size = new Size(100, 34),
            FlatStyle = FlatStyle.Flat,
        };
        btnSave.Click += (_, _) => Save();
        btnCancel.Click += (_, _) => Close();
        Controls.AddRange([btnSave, btnCancel]);

        LoadConfig();
    }

    /// <summary>数值输入框（范围 min~max，可选小数位，居中显示）。</summary>
    private static NumericUpDown Num(decimal min, decimal max, int decimals = 0)
    {
        return new NumericUpDown
        {
            Size = new Size(78, 30),
            Minimum = min,
            Maximum = max,
            DecimalPlaces = decimals,
            TextAlign = HorizontalAlignment.Center,
        };
    }

    /// <summary>从当前配置读取收费设置到界面。</summary>
    private void LoadConfig()
    {
        var cfg = AppConfig.Current;
        _chkEnabled.Checked = cfg.FeeEnabled;
        _numFallback.Value = cfg.FeeFallbackPerTon;

        var brackets = cfg.FeeBrackets;
        for (int i = 0; i < BracketRowCount; i++)
        {
            var b = i < brackets.Count ? brackets[i] : new FeeBracket { FromTon = 0, ToTon = 0 };
            _rows[i].From.Value = Clamp((decimal)b.FromTon, _rows[i].From);
            // 最后一行终点固定显示 0（不封顶），中间行原样回显
            _rows[i].To.Value = i == BracketRowCount - 1 ? 0 : Clamp((decimal)b.ToTon, _rows[i].To);
            _rows[i].Amount.Value = Clamp(b.AmountYuan, _rows[i].Amount);
        }
    }

    private static decimal Clamp(decimal v, NumericUpDown n) =>
        Math.Clamp(v, n.Minimum, n.Maximum);

    /// <summary>校验并保存收费设置到 config.json（下次结算立即生效）。</summary>
    private void Save()
    {
        // 中间行（非最后一行）必须 终点 > 起点，避免分段重叠/断档
        for (int i = 0; i < BracketRowCount - 1; i++)
        {
            if (_rows[i].To.Value <= _rows[i].From.Value)
            {
                MessageBox.Show($"第 {i + 1} 行分段终点（{_rows[i].To.Value} 吨）必须大于起点（{_rows[i].From.Value} 吨）。",
                    "分段有误", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
        }

        var cfg = AppConfig.Current;
        cfg.FeeEnabled = _chkEnabled.Checked;
        cfg.FeeFallbackPerTon = _numFallback.Value;
        cfg.FeeBrackets = _rows.Select((r, i) => new FeeBracket
        {
            FromTon = (double)r.From.Value,
            ToTon = (double)(i == BracketRowCount - 1 ? 0 : r.To.Value),   // 最后一行不封顶
            AmountYuan = r.Amount.Value,
        }).ToList();
        cfg.Save();
        Log.Info($"收费设置已保存：启用={cfg.FeeEnabled} 其余 {cfg.FeeFallbackPerTon} 元/吨，" +
                 $"分段 {string.Join("；", cfg.FeeBrackets.Select(b => $"{b.FromTon:0.##}-{b.ToTon:0.##}吨 {b.AmountYuan:0.##}元"))}");
        DialogResult = DialogResult.OK;
        Close();
    }
}
