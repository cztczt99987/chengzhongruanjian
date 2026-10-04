namespace CZRWeighSystem.UI;

/// <summary>
/// 车牌编号字母触摸面板（仿老软件：双击车牌编号框弹出）。
/// 5 行 × 6 列：A~Z 共 26 个字母 + 挂/头/尾 3 个特殊项，红 ✕ 关闭。
/// 点选字符只回调、不关闭面板，支持连续点选；Esc 或红 ✕ 关闭。
/// 面板按钮不获取焦点（ControlStyles.Selectable=false）：编号框始终保持输入焦点，
/// 物理键盘可同时输入，光标位置由编号框自己维护。
/// </summary>
public class PlateKeypadForm : Form
{
    /// <summary>点选字符（字母或挂/头/尾）；面板不关闭，可连续回调。</summary>
    public event Action<string>? CharPicked;

    // 行序严格按老软件面板截图（✕ = 关闭，不是字母 X）
    private static readonly string?[][] _layout =
    [
        ["A", "B", "C", "D", "挂", null],
        ["E", "F", "G", "H", "头", "尾"],
        ["I", "J", "K", "L", "M", "N"],
        ["O", "P", "Q", "R", "S", "T"],
        ["U", "V", "W", "X", "Y", "Z"],
    ];

    private const int CellW = 56;
    private const int CellH = 50;
    private const int Gap = 6;
    private const int Pad = 8;

    public PlateKeypadForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.Dpi;   // 高 DPI 触摸机下按钮随之放大
        Font = new Font("Microsoft YaHei UI", 13F, FontStyle.Bold);
        BackColor = Color.FromArgb(224, 224, 218);
        KeyPreview = true;   // Esc 关闭
        Size = new Size(
            Pad * 2 + 6 * CellW + 5 * Gap,
            Pad * 2 + 5 * CellH + 4 * Gap);

        BuildButtons();
    }

    private void BuildButtons()
    {
        for (int r = 0; r < _layout.Length; r++)
        {
            for (int c = 0; c < _layout[r].Length; c++)
            {
                string? ch = _layout[r][c];
                int x = Pad + c * (CellW + Gap);
                int y = Pad + r * (CellH + Gap);

                if (r == 0 && c == 5)
                {
                    // 关闭格（红 ✕，带浅色边框仿老软件纸张折角样式）
                    var close = new KeypadButton
                    {
                        Text = "✕",
                        Font = new Font("Microsoft YaHei UI", 17F, FontStyle.Bold),
                        ForeColor = Color.FromArgb(204, 0, 0),
                        Bounds = new Rectangle(x, y, CellW, CellH),
                        Name = "btnClose",
                    };
                    close.Click += (_, _) => Close();
                    Controls.Add(close);
                }
                else if (ch != null)
                {
                    var btn = new KeypadButton
                    {
                        Text = ch,
                        Bounds = new Rectangle(x, y, CellW, CellH),
                        Name = "char_" + ch,
                    };
                    btn.Click += (_, _) => CharPicked?.Invoke(ch);
                    Controls.Add(btn);
                }
            }
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Escape) Close();
    }

    /// <summary>
    /// 定位到车牌编号框附近：优先对齐编号框上沿显示在其上方（与老软件一致），
    /// 上方超出屏幕工作区时改到下方；水平方向超出右边则回拉。
    /// </summary>
    public void PlaceNear(Control anchor)
    {
        var origin = anchor.PointToScreen(Point.Empty);
        var area = Screen.FromPoint(origin).WorkingArea;

        int x = origin.X;
        int y = origin.Y - Height - 4;   // 编号框上方
        if (y < area.Top + 2)
            y = origin.Y + anchor.Height + 4;   // 上方放不下 → 下方

        if (x + Width > area.Right - 2) x = area.Right - Width - 2;
        if (x < area.Left + 2) x = area.Left + 2;

        Location = new Point(x, y);
    }

    /// <summary>触摸按钮：不参与焦点 Tab、点击不抢走编号框的输入焦点。</summary>
    private sealed class KeypadButton : Button
    {
        public KeypadButton()
        {
            SetStyle(ControlStyles.Selectable, false);
            FlatStyle = FlatStyle.Standard;   // 3D 凸起，贴近老软件按钮
            BackColor = SystemColors.Control;
            UseVisualStyleBackColor = true;
        }
    }
}
