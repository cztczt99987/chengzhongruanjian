namespace CZRWeighSystem.UI;

/// <summary>
/// 综合设置窗体（对应目标软件"综合设置 [F3]"对话框，限管理员）。
/// 顶部工具条：保存 / 退出；共 4 个页签：
///   基本设置  —— 按目标软件版式实现（公司信息/打印次数/默认省份/数据稳定/锁屏/归零拦截/自动清除/二次称重禁止/黑匣子）
///   高级设置  —— 内嵌原「串口参数设置」
///   联网设置  —— 内嵌原「数据库设置」
///   视频设置  —— 内嵌原「摄像头设置」
/// 内嵌窗体保存逻辑照旧执行（写 config.json），FormClosing 被拦截以保持在页签内；
/// 串口/摄像头保存后经 SerialSaved / CamerasSaved 事件通知主界面重启取流。
/// </summary>
public class ComprehensiveSettingsDialog : Form
{
    // ===== 基本设置页控件（internal 供测试读写）=====
    internal TextBox TxtCompany = null!;
    internal TextBox TxtHeader = null!;
    internal TextBox TxtFooter = null!;
    internal CheckBox ChkPrintCount = null!;
    internal NumericUpDown NumMaxPrint = null!;
    internal ComboBox CboProvince = null!;
    internal ComboBox CboLetter = null!;
    internal CheckBox ChkUseProvinceLetter = null!;
    internal CheckBox ChkStableSeconds = null!;
    internal ComboBox CboStableSeconds = null!;
    internal NumericUpDown NumIdleLock = null!;
    internal CheckBox ChkBlockZero = null!;
    internal ComboBox CboExpireDays = null!;
    internal ComboBox CboForbidMinutes = null!;
    internal CheckBox ChkBlackBox = null!;          // 黑匣子启用（原称重设置迁入）
    internal NumericUpDown NumBlackBoxMinKg = null!; // 黑匣子触发重量 kg
    internal NumericUpDown NumBlackBoxSeconds = null!; // 黑匣子持续稳定秒数
    internal NumericUpDown NumTareDiffWarnTon = null!;  // 空车去皮差值预警（吨，0=关闭）
    internal TabControl Tabs = null!;

    /// <summary>结果提示框（测试注入后记录消息不弹窗，同 WeighEntryDialog 模式）。</summary>
    internal Action<string, string, bool> InfoBox = (msg, title, ok) =>
        MessageBox.Show(msg, title, MessageBoxButtons.OK,
            ok ? MessageBoxIcon.Information : MessageBoxIcon.Warning);

    /// <summary>页签内保存串口参数后触发（主界面据此重连串口）。</summary>
    internal event Action? SerialSaved;
    /// <summary>页签内保存摄像头设置后触发（主界面据此重启取流）。</summary>
    internal event Action? CamerasSaved;

    private readonly List<Form> _embedded = [];   // 内嵌窗体（外层关闭时统一释放）

    public ComprehensiveSettingsDialog()
    {
        Text = "综合设置 [F3]";
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;   // 高 DPI 下避免文字截断
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(900, 700);
        Font = new Font("Microsoft YaHei UI", 10F);

        BuildToolbar();
        BuildTabs();

        FormClosed += (_, _) =>
        {
            foreach (var f in _embedded) f.Dispose();   // 内嵌窗体不随 Close 释放，此处统一清理
        };
    }

    // ---------- 顶部工具条：保存 / 退出（右对齐，仿目标软件） ----------
    private void BuildToolbar()
    {
        var top = new Panel { Dock = DockStyle.Top, Height = 52 };
        var btnSave = new Button
        {
            Text = "保存",
            Size = new Size(72, 44),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(0, 122, 204),
            ForeColor = Color.White,
        };
        var btnExit = new Button
        {
            Text = "退出",
            Size = new Size(72, 44),
            FlatStyle = FlatStyle.Flat,
        };
        btnSave.Click += (_, _) => SaveBasic();
        btnExit.Click += (_, _) => Close();
        top.Controls.Add(btnSave);
        top.Controls.Add(btnExit);
        // 右对齐布局（窗口缩放时保持贴右）
        void LayoutButtons()
        {
            btnExit.Location = new Point(top.Width - btnExit.Width - 16, 4);
            btnSave.Location = new Point(btnExit.Left - btnSave.Width - 8, 4);
        }
        top.Resize += (_, _) => LayoutButtons();
        LayoutButtons();
        Controls.Add(top);
    }

    // ---------- 页签骨架 ----------
    private void BuildTabs()
    {
        Tabs = new TabControl { Dock = DockStyle.Fill };

        var basic = new TabPage("『基本设置』");
        BuildBasicPage(basic);
        Tabs.TabPages.Add(basic);

        // 内嵌原有设置窗体；其保存事件转发给主界面（重连串口/重启取流）
        var serial = new SerialSettingsDialog();
        serial.Saved += () => SerialSaved?.Invoke();
        Tabs.TabPages.Add(EmbedPage(serial, "『高级设置』"));

        Tabs.TabPages.Add(EmbedPage(new DatabaseSettingsDialog(), "『联网设置』"));

        Tabs.TabPages.Add(EmbedPage(new PlatformSyncSettingsDialog(), "『平台对接』"));

        var camera = new CameraSettingsDialog();
        camera.Saved += () => CamerasSaved?.Invoke();
        Tabs.TabPages.Add(EmbedPage(camera, "『视频设置』"));

        Controls.Add(Tabs);
        Tabs.BringToFront();
    }

    /// <summary>把现有设置窗体内嵌进页签：无边框、充满页签；保存/取消后的 Close 被拦截（避免控件被释放）。</summary>
    private TabPage EmbedPage(Form inner, string title)
    {
        inner.TopLevel = false;
        inner.FormBorderStyle = FormBorderStyle.None;
        inner.Dock = DockStyle.Fill;
        inner.FormClosing += (_, e) => e.Cancel = true;   // 内嵌状态：保存/取消只执行逻辑，窗体保留在页签内
        var page = new TabPage(title);
        page.Controls.Add(inner);
        _embedded.Add(inner);
        inner.Show();   // TopLevel=false 的窗体需显式 Show 才在页签内可见
        return page;
    }

    // ---------- 基本设置页（按目标软件版式，6 个分组区块） ----------

    /// <summary>
    /// 行内紧排：items 依次紧随 anchor 右缘排布（只改 Left，不动作 y 与宽）。
    /// AutoSize 标签的实测宽度随 DPI 字体缩放变化，固定坐标会在高 DPI 下被右侧输入框
    /// 压住文字（用户截图反馈的"标签遮挡"）；以 SizeChanged 驱动重排，宽度一变立即纠正。
    /// </summary>
    private static void FlowRow(Control anchor, params Control[] items)
    {
        void LayoutRow()
        {
            var prev = anchor;
            foreach (var c in items)
            {
                c.Left = prev.Right + 8;
                prev = c;
            }
        }
        anchor.SizeChanged += (_, _) => LayoutRow();
        foreach (var c in items) c.SizeChanged += (_, _) => LayoutRow();
        LayoutRow();
    }

    private void BuildBasicPage(TabPage page)
    {
        var cfg = AppConfig.Current;

        // ---- 区块 1：公司信息（公司名称 / 磅单抬头 / 磅单底部） ----
        var grpInfo = NewGroup(page, 15, 172);
        TxtCompany = AddRow(grpInfo, "公司名称：", cfg.CompanyName, 15);
        TxtHeader = AddRow(grpInfo, "磅单抬头：", cfg.SlipHeader, 60);
        TxtFooter = AddRow(grpInfo, "磅单底部：", cfg.SlipFooter, 105);

        // ---- 区块 2：磅单打印次数累计 ----
        var grpPrint = NewGroup(page, 202, 56);
        ChkPrintCount = new CheckBox
        {
            Text = "启用磅单\"打印次数\"累计，同时设置每个磅单最多允许打印",
            AutoSize = true,
            Location = new Point(18, 20),
            Checked = cfg.PrintCountEnabled,
        };
        NumMaxPrint = new NumericUpDown
        {
            Minimum = 1, Maximum = 99,
            Value = cfg.MaxPrintCount,
            Location = new Point(460, 15),
            Width = 60,
            TextAlign = HorizontalAlignment.Center,
        };
        var lblCi = new Label { Text = "次", AutoSize = true, Location = new Point(528, 20) };
        grpPrint.Controls.Add(ChkPrintCount);
        grpPrint.Controls.Add(NumMaxPrint);
        grpPrint.Controls.Add(lblCi);
        FlowRow(ChkPrintCount, NumMaxPrint, lblCi);

        // ---- 区块 3：默认省份 / 数据稳定 ----
        var grpProvince = NewGroup(page, 272, 56);
        var lblProvince = new Label
        { Text = "默认省份", AutoSize = true, Location = new Point(18, 21) };
        CboProvince = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(88, 16), Width = 64,
        };
        CboProvince.Items.AddRange(
            VehiclePadDialog.Provinces.Select(p => p.ToString()).Cast<object>().ToArray());
        SelectOrDefault(CboProvince, cfg.DefaultProvince);

        CboLetter = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(160, 16), Width = 64,
        };
        CboLetter.Items.AddRange(
            VehiclePadDialog.Letters.Select(l => l.ToString()).Cast<object>().ToArray());
        SelectOrDefault(CboLetter, cfg.DefaultLetter);

        ChkUseProvinceLetter = new CheckBox
        {
            Text = "使用省、字母",
            AutoSize = true,
            Location = new Point(238, 19),
            Checked = cfg.UseProvinceLetter,
        };
        ChkStableSeconds = new CheckBox
        {
            Text = "数据稳定",
            AutoSize = true,
            Location = new Point(392, 19),
            Checked = cfg.StableSeconds > 0,
        };
        CboStableSeconds = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(470, 16), Width = 64,
            Enabled = cfg.StableSeconds > 0,
            BackColor = cfg.StableSeconds > 0 ? SystemColors.Window : SystemColors.Control,
        };
        CboStableSeconds.Items.AddRange([3, 5, 10, 15, 30]);
        CboStableSeconds.SelectedItem =
            CboStableSeconds.Items.Contains(cfg.StableSeconds) ? cfg.StableSeconds : 5;
        ChkStableSeconds.CheckedChanged += (_, _) =>
        {
            CboStableSeconds.Enabled = ChkStableSeconds.Checked;
            CboStableSeconds.BackColor =
                ChkStableSeconds.Checked ? SystemColors.Window : SystemColors.Control;
        };
        var lblStableTail = new Label
        {
            Text = "秒 绿灯亮，方可保存",
            AutoSize = true,
            Location = new Point(542, 21),
        };
        grpProvince.Controls.AddRange([lblProvince, CboProvince, CboLetter,
            ChkUseProvinceLetter, ChkStableSeconds, CboStableSeconds, lblStableTail]);
        FlowRow(lblProvince, CboProvince, CboLetter, ChkUseProvinceLetter,
            ChkStableSeconds, CboStableSeconds, lblStableTail);

        // ---- 区块 4：闲置锁屏 / 归零拦截 ----
        var grpIdle = NewGroup(page, 342, 56);
        var lblIdle = new Label
        { Text = "电脑闲置", AutoSize = true, Location = new Point(18, 21) };
        NumIdleLock = new NumericUpDown
        {
            Minimum = 0, Maximum = 10000,
            Value = cfg.IdleLockMinutes,
            Location = new Point(88, 16), Width = 80,
            TextAlign = HorizontalAlignment.Center,
        };
        var lblIdleTail = new Label
        { Text = "分钟屏幕锁定", AutoSize = true, Location = new Point(176, 21) };
        ChkBlockZero = new CheckBox
        {
            Text = "仪表不归零，不允许保存数据，防止重复称重",
            AutoSize = true,
            Location = new Point(420, 19),
            Checked = cfg.BlockSaveUntilZero,
        };
        grpIdle.Controls.AddRange([lblIdle, NumIdleLock, lblIdleTail, ChkBlockZero]);
        FlowRow(lblIdle, NumIdleLock, lblIdleTail);   // 第二列 ChkBlockZero 固定 420 与区块 5 对齐

        // ---- 区块 5：自动清除 / 严格禁止二次称重 ----
        var grpClear = NewGroup(page, 412, 56);
        var lblClear = new Label
        { Text = "自动清除", AutoSize = true, Location = new Point(18, 21) };
        CboExpireDays = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(88, 16), Width = 80,
        };
        CboExpireDays.Items.AddRange(Enumerable.Range(1, 30).Cast<object>().ToArray());
        // 仅允许 1~30 天；配置值超出范围（含 0=历史"不启用"值）时回落到 30
        CboExpireDays.SelectedItem =
            cfg.FirstWeighExpireDays is >= 1 and <= 30 ? cfg.FirstWeighExpireDays : 30;
        var lblClearTail = new Label
        { Text = "天内未使用的一次称重", AutoSize = true, Location = new Point(176, 21) };
        var lblForbid = new Label
        { Text = "严格禁止", AutoSize = true, Location = new Point(420, 21) };
        CboForbidMinutes = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(486, 16), Width = 80,
        };
        CboForbidMinutes.Items.AddRange([0, 5, 10, 15, 30, 60]);
        CboForbidMinutes.SelectedItem = cfg.ForbidReweighMinutes;
        if (CboForbidMinutes.SelectedIndex < 0) CboForbidMinutes.SelectedItem = 0;
        var lblForbidTail = new Label
        { Text = "分钟内二次称重", AutoSize = true, Location = new Point(574, 21) };
        grpClear.Controls.AddRange([lblClear, CboExpireDays, lblClearTail,
            lblForbid, CboForbidMinutes, lblForbidTail]);
        FlowRow(lblClear, CboExpireDays, lblClearTail);   // 第二列 lblForbid 固定 420 与区块 4 对齐
        FlowRow(lblForbid, CboForbidMinutes, lblForbidTail);

        // ---- 区块 6：黑匣子（自原「称重设置」迁入：稳定持续设定秒数后自动记录 + 抓拍） ----
        // 两行布局：复选框独占首行，数值输入放第二行，避免长文案在不同 DPI 下遮挡输入框
        var grpBlackBox = NewGroup(page, 482, 90);
        ChkBlackBox = new CheckBox
        {
            Text = "启用黑匣子（稳定达到触发重量并持续秒数后自动记录 + 抓拍）",
            AutoSize = true,
            Location = new Point(18, 17),
            Checked = cfg.BlackBoxEnabled,
        };
        var lblMinKg = new Label
        { Text = "触发重量（kg，不小于）：", AutoSize = true, Location = new Point(30, 56) };
        NumBlackBoxMinKg = new NumericUpDown
        {
            DecimalPlaces = 1,
            Minimum = 0, Maximum = 1000000,
            Value = (decimal)cfg.BlackBoxMinKg,
            Location = new Point(200, 51), Width = 110,
            TextAlign = HorizontalAlignment.Center,
        };
        var lblSeconds = new Label
        { Text = "持续稳定（秒）：", AutoSize = true, Location = new Point(360, 56) };
        NumBlackBoxSeconds = new NumericUpDown
        {
            Minimum = 1, Maximum = 86400,
            Value = cfg.BlackBoxSeconds,
            Location = new Point(480, 51), Width = 90,
            TextAlign = HorizontalAlignment.Center,
        };
        grpBlackBox.Controls.AddRange([ChkBlackBox, lblMinKg, NumBlackBoxMinKg,
            lblSeconds, NumBlackBoxSeconds]);
        FlowRow(lblMinKg, NumBlackBoxMinKg, lblSeconds, NumBlackBoxSeconds);

        // ---- 区块 7：空车去皮差值预警（本次皮重与本车上次已完成皮重相差超阈值时仅弹窗预警） ----
        var grpTareDiff = NewGroup(page, 578, 50);
        var lblTareDiff = new Label
        { Text = "空车去皮差值预警：本次皮重与上次相差超过", AutoSize = true, Location = new Point(18, 16) };
        NumTareDiffWarnTon = new NumericUpDown
        {
            DecimalPlaces = 2,
            Minimum = 0, Maximum = 100,
            Value = (decimal)cfg.TareDiffWarnTon,
            Width = 70,
            TextAlign = HorizontalAlignment.Center,
        };
        var lblTareDiffTail = new Label
        { Text = "吨时提示（0=关闭，仅预警不阻止保存）", AutoSize = true, Location = new Point(330, 16) };
        grpTareDiff.Controls.AddRange([lblTareDiff, NumTareDiffWarnTon, lblTareDiffTail]);
        FlowRow(lblTareDiff, NumTareDiffWarnTon, lblTareDiffTail);
    }

    /// <summary>在页签上创建一个分组区块（仿目标软件的边框分节）。</summary>
    private static GroupBox NewGroup(TabPage page, int y, int height)
    {
        var grp = new GroupBox
        {
            Location = new Point(15, y),
            Size = new Size(856, height),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
        };
        page.Controls.Add(grp);
        return grp;
    }

    /// <summary>单行"标签 + 文本框"（标签与输入框间距固定，返回文本框）。</summary>
    private static TextBox AddRow(GroupBox grp, string title, string value, int y)
    {
        var lbl = new Label
        { Text = title, AutoSize = true, Location = new Point(18, y + 5) };
        var txt = new TextBox
        {
            Text = value,
            Location = new Point(110, y),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
        };
        txt.Width = grp.Width - 130;
        grp.Controls.Add(lbl);
        grp.Controls.Add(txt);
        FlowRow(lbl, txt);   // 高 DPI 下标签实测变宽时输入框自动右移，避免压住文字
        return txt;
    }

    /// <summary>下拉框选中指定项，不存在时回落到第一项。</summary>
    private static void SelectOrDefault(ComboBox cbo, string value)
    {
        cbo.SelectedItem = value;
        if (cbo.SelectedIndex < 0 && cbo.Items.Count > 0) cbo.SelectedIndex = 0;
    }

    /// <summary>保存基本设置页到 config.json（立即生效；页签内嵌窗体各自有保存按钮）。</summary>
    internal void SaveBasic()
    {
        var cfg = AppConfig.Current;
        cfg.CompanyName = TxtCompany.Text.Trim();
        cfg.SlipHeader = TxtHeader.Text.Trim();
        cfg.SlipFooter = TxtFooter.Text.Trim();
        cfg.PrintCountEnabled = ChkPrintCount.Checked;
        cfg.MaxPrintCount = (int)NumMaxPrint.Value;
        cfg.DefaultProvince = CboProvince.SelectedItem?.ToString() ?? "";
        cfg.DefaultLetter = CboLetter.SelectedItem?.ToString() ?? "";
        cfg.UseProvinceLetter = ChkUseProvinceLetter.Checked;
        cfg.StableSeconds = ChkStableSeconds.Checked ? (int)CboStableSeconds.SelectedItem! : 0;
        cfg.IdleLockMinutes = (int)NumIdleLock.Value;
        cfg.BlockSaveUntilZero = ChkBlockZero.Checked;
        cfg.FirstWeighExpireDays = (int)CboExpireDays.SelectedItem!;
        cfg.ForbidReweighMinutes = (int)CboForbidMinutes.SelectedItem!;
        cfg.BlackBoxEnabled = ChkBlackBox.Checked;
        cfg.BlackBoxMinKg = (double)NumBlackBoxMinKg.Value;
        cfg.BlackBoxSeconds = (int)NumBlackBoxSeconds.Value;
        cfg.TareDiffWarnTon = (double)NumTareDiffWarnTon.Value;
        cfg.Save();
        Log.Info($"综合设置已保存（基本设置页）");
        InfoBox("设置已保存。", "成功", true);
        DialogResult = DialogResult.OK;
        Close();
    }
}
