namespace CZRWeighSystem.UI;

/// <summary>
/// 车牌号软键盘弹窗：点击字符按钮逐位组成车牌，确认后回填录入区。
/// 键区：省份简称 / 字母（车牌不含 I、O）/ 数字与特殊标识（挂学警港澳）。
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
        // ---- 顶部预览 + 退格/清空 ----
        var top = new Panel { Location = new Point(15, 15), Size = new Size(610, 60) };
        _lblPreview.Parent = top;
        var btnBack = new Button
        {
            Text = "⌫ 退格", Location = new Point(460, 10), Size = new Size(70, 40),
            FlatStyle = FlatStyle.Flat,
        };
        var btnClear = new Button
        {
            Text = "清空", Location = new Point(536, 10), Size = new Size(64, 40),
            FlatStyle = FlatStyle.Flat,
        };
        btnBack.Click += (_, _) => { if (_lblPreview.Text.Length > 0) _lblPreview.Text = _lblPreview.Text[..^1]; };
        btnClear.Click += (_, _) => _lblPreview.Text = "";
        top.Controls.AddRange([btnBack, btnClear]);
        Controls.Add(top);

        // ---- 键区（流式布局，自动换行对齐）----
        var flow = new FlowLayoutPanel
        {
            Location = new Point(15, 85),
            Size = new Size(610, 310),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
        };

        Button Key(string text, Color back, int width = 56)
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
                if (_lblPreview.Text.Length < 10) _lblPreview.Text += text;
            };
            return b;
        }

        // 省份简称（黄底）
        foreach (var p in Provinces) flow.Controls.Add(Key(p.ToString(), Color.FromArgb(255, 236, 153)));
        // 字母（白底）
        foreach (var ch in Letters) flow.Controls.Add(Key(ch.ToString(), Color.White));
        // 数字（白底）
        foreach (var ch in Digits) flow.Controls.Add(Key(ch.ToString(), Color.White));
        // 特殊标识（白底）
        foreach (var m in Marks) flow.Controls.Add(Key(m.ToString(), Color.White));

        Controls.Add(flow);

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
