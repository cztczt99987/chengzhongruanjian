namespace CZRWeighSystem.UI;

/// <summary>
/// 车牌号软键盘弹窗：点击字符按钮逐位组成车牌，确认后回填录入区。
/// 键区按分类标签页组织：省份简称 / 字母与数字（车牌不含 I、O，含挂学警港澳特殊标识），
/// 其中"字母 / 数字"页左右分栏：左侧字母、右侧数字与特殊标识，方便查找。
/// 点击省份后自动跳到字母/数字页，贴合"省份 + 发牌机关代号 + 编号"的实际输入顺序。
/// 省份仅允许出现在开头：重新选择省份时替换旧省份，已输入的编号保留。
/// </summary>
public class VehiclePadDialog : Form
{
    /// <summary>确认后的车牌号（取消为 null）。</summary>
    public string? ResultVehicle { get; private set; }

    private readonly Label _lblPreview = new()
    {
        Text = "",
        Font = new Font("Consolas", 26F, FontStyle.Bold),
        ForeColor = Color.FromArgb(0, 102, 204),
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleCenter,
        BackColor = Color.FromArgb(245, 246, 248),
    };

    private static readonly string Provinces = "京津冀晋蒙辽吉黑沪苏浙皖闽赣鲁豫鄂湘粤桂琼渝川贵云藏陕甘青宁新";
    private static readonly string Letters = "ABCDEFGHJKLMNPQRSTUVWXYZ";   // 车牌不用 I/O
    private static readonly string Digits = "0123456789";
    private static readonly string Marks = "挂学警港澳";

    public VehiclePadDialog()
    {
        Text = "选择车牌号";
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;   // 高 DPI 下避免文字截断
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(640, 470);
        Font = new Font("Microsoft YaHei UI", 10F);

        BuildUi();
    }

    private void BuildUi()
    {
        // ---- 顶部预览 + 删除/清空（浅红大按钮，醒目易点）----
        var top = new Panel { Location = new Point(15, 15), Size = new Size(610, 60) };
        _lblPreview.Parent = top;
        var btnBack = new Button
        {
            Text = "⌫ 删除", Location = new Point(424, 10), Size = new Size(92, 40),
            BackColor = Color.FromArgb(255, 214, 214),
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold),
        };
        var btnClear = new Button
        {
            Text = "清空", Location = new Point(522, 10), Size = new Size(78, 40),
            BackColor = Color.FromArgb(255, 214, 214),
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold),
        };
        btnBack.Click += (_, _) => { if (_lblPreview.Text.Length > 0) _lblPreview.Text = _lblPreview.Text[..^1]; };
        btnClear.Click += (_, _) => _lblPreview.Text = "";
        top.Controls.AddRange([btnBack, btnClear]);
        Controls.Add(top);

        // ---- 键区：分类标签页（省份 / 字母·数字），每页按键都完整可见 ----
        var tabs = new TabControl
        {
            Location = new Point(15, 85),
            Size = new Size(610, 310),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
        };
        Controls.Add(tabs);

        // 每页放一个流式布局面板；AutoScroll 兜底，高 DPI 极端缩放时也不丢按键
        TabPage Page(string title, out FlowLayoutPanel flow)
        {
            var page = new TabPage(title) { Padding = new Padding(8) };
            flow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true };
            page.Controls.Add(flow);
            tabs.TabPages.Add(page);
            return page;
        }
        var pageProvince = Page("省份简称", out var flowProvince);
        var pageAlnum = Page("字母 / 数字", out _);

        // 字母 / 数字页分栏：左侧字母、右侧数字与特殊标识，分区排列方便查找
        FlowLayoutPanel FlowIn(GroupBox box)
        {
            var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true };
            box.Controls.Add(flow);
            pageAlnum.Controls.Add(box);
            return flow;
        }
        var flowLetters = FlowIn(new GroupBox
        { Text = "字母", Location = new Point(2, 4), Size = new Size(310, 250) });
        var flowDigits = FlowIn(new GroupBox
        { Text = "数字 / 特殊", Location = new Point(316, 4), Size = new Size(262, 250) });

        // click 为空时默认行为=追加字符；省份键等特殊逻辑通过 click 完全接管
        Button Key(string text, Color back, FlowLayoutPanel page, int width = 56, Action? click = null)
        {
            var b = new Button
            {
                Text = text, Size = new Size(width, 44),
                BackColor = back, FlatStyle = FlatStyle.Flat,
                Margin = new Padding(3),
                Font = new Font("Microsoft YaHei UI", 11F),
            };
            b.Click += (_, _) =>
            {
                if (click != null) click();
                else if (_lblPreview.Text.Length < 10) _lblPreview.Text += text;
            };
            page.Controls.Add(b);
            return b;
        }

        // 省份简称（黄底）：省份只能出现在开头——重新选省份时替换旧省份、保留已输编号；
        // 选完自动跳到字母/数字页
        foreach (var p in Provinces)
            Key(p.ToString(), Color.FromArgb(255, 236, 153), flowProvince,
                click: () =>
                {
                    string s = _lblPreview.Text;
                    _lblPreview.Text = s.Length > 0 && Provinces.Contains(s[0])
                        ? p.ToString() + s[1..]     // 已有省份 → 替换第一个字符
                        : p.ToString() + s;         // 尚无省份 → 插入到开头
                    tabs.SelectedTab = pageAlnum;
                });
        // 字母（车牌不用 I/O）在左栏；数字与挂/学/警/港/澳特殊标识在右栏
        foreach (var ch in Letters)
            Key(ch.ToString(), Color.White, flowLetters, width: 44);
        foreach (var ch in Digits)
            Key(ch.ToString(), Color.White, flowDigits, width: 44);
        foreach (var m in Marks)
            Key(m.ToString(), Color.White, flowDigits, width: 44);

        // ---- 底部确认/取消 ----
        var btnOk = new Button
        {
            Text = "确 认", Location = new Point(180, 408), Size = new Size(130, 40),
            BackColor = Color.FromArgb(0, 122, 204), ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
        };
        var btnCancel = new Button
        {
            Text = "取 消", Location = new Point(330, 408), Size = new Size(130, 40),
            FlatStyle = FlatStyle.Flat,
        };
        btnOk.Click += (_, _) =>
        {
            if (_lblPreview.Text.Length < 2)
            {
                MessageBox.Show("请至少选择省份简称和一位编号", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            ResultVehicle = _lblPreview.Text;
            DialogResult = DialogResult.OK;
            Close();
        };
        btnCancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        Controls.Add(btnOk);
        Controls.Add(btnCancel);
        AcceptButton = btnOk;
    }
}
