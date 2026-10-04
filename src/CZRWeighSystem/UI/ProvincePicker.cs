namespace CZRWeighSystem.UI;

/// <summary>
/// 车牌省份简称选择器：外观为扁平下拉按钮，点击弹出"多列紧凑网格"选省面板
/// （无省份 + 31 个省份简称，8 列 × 4 行全部可见，无需滚动）。
/// 替代原生 ComboBox 的超长单列下拉；成员命名与 ComboBox 保持一致
/// （SelectedIndex / SelectedItem / SelectedIndexChanged / Items），便于替换与测试。
/// 下拉弹窗只创建一次并反复 Show/Close 复用——绝不在 Closed 中 Dispose，
/// 否则"下拉自动关闭"与"按钮手动关闭"两条路径重入会访问已释放对象。
/// </summary>
internal sealed class ProvincePicker : Button
{
    /// <summary>可选省份列表：索引 0 = 无省份（空串），其后为全部省份简称。</summary>
    public static readonly IList<string> Items =
        new[] { "" }.Concat(VehiclePadDialog.Provinces.Select(c => c.ToString())).ToList();

    private const int Cols = 8;
    private const int Rows = 4;

    private int _selectedIndex;
    private ToolStripDropDown? _drop;
    private Panel? _grid;

    public ProvincePicker()
    {
        FlatStyle = FlatStyle.Flat;
        BackColor = Color.White;
        TextAlign = ContentAlignment.MiddleLeft;
        Padding = new Padding(7, 0, 20, 0);
        FlatAppearance.BorderColor = Color.FromArgb(171, 173, 179);
        FlatAppearance.BorderSize = 1;
        Cursor = Cursors.Hand;
        UpdateText();
    }

    /// <summary>当前选中项索引（0 = 无省份）。</summary>
    [System.ComponentModel.DesignerSerializationVisibility(
        System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            if (value < 0 || value >= Items.Count)
                throw new ArgumentOutOfRangeException(nameof(value));
            if (_selectedIndex == value) return;
            _selectedIndex = value;
            UpdateText();
            SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>当前选中省份简称（无省份为空串）。</summary>
    public string SelectedItem => Items[_selectedIndex];

    /// <summary>选中项变化（语义同 ComboBox.SelectedIndexChanged）。</summary>
    public event EventHandler? SelectedIndexChanged;

    private void UpdateText() => Text = Items[_selectedIndex];

    // 鼠标按下时开关：下拉打开期间点击外部会被下拉框吞掉并自动关闭（按钮收不到点击）；
    // 若点击到达本控件且下拉仍可见，则手动关闭。两种消息顺序下都不会重入已释放对象。
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Left) ToggleDropDown();
    }

    // 键盘空格/回车（Button 在鼠标 UP 时也会触发 Click：此时下拉已由 MouseDown 打开，
    // Visible=true 不重复处理）
    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        if (_drop is not { Visible: true }) ShowDropDown();
    }

    // 键盘 ↓ 直接展开/收起（近似 ComboBox 的 Alt+↓）
    protected override bool IsInputKey(Keys keyData) =>
        keyData == Keys.Down || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Down)
        {
            ToggleDropDown();
            e.Handled = true;
        }
        base.OnKeyDown(e);
    }

    // 右侧下拉箭头由 OnPaint 自绘（不用子 Label，避免父子点击分裂）
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        int x = Width - 13;
        int y = Height / 2 - 2;
        using var brush = new SolidBrush(Color.FromArgb(90, 90, 90));
        e.Graphics.FillPolygon(brush, new[]
        {
            new Point(x, y),
            new Point(x + 8, y),
            new Point(x + 4, y + 5),
        });
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            // 弹窗生命周期跟随选择器：仅在这里释放，关闭时不释放以便复用
            _drop?.Dispose();
            _drop = null;
            _grid = null;
        }
        base.Dispose(disposing);
    }

    private void ToggleDropDown()
    {
        EnsureDrop();
        if (_drop!.Visible)
            _drop.Close();
        else
            ShowDropDown();
    }

    private void ShowDropDown()
    {
        EnsureDrop();
        if (_drop!.Visible || _drop.IsDisposed) return;
        BuildGrid();
        _drop.Show(this, new Point(0, Height));
    }

    /// <summary>懒创建唯一弹窗（宿主面板复用，每次打开前重建网格以刷新选中高亮）。</summary>
    private void EnsureDrop()
    {
        if (_drop is { IsDisposed: false }) return;

        _grid = new Panel { BackColor = Color.FromArgb(245, 246, 248) };
        var host = new ToolStripControlHost(_grid)
        {
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };
        _drop = new ToolStripDropDown
        {
            Padding = Padding.Empty,
            Margin = Padding.Empty,
            DropShadowEnabled = true,
        };
        _drop.Items.Add(host);
        // 故意不订阅 Closed 做释放/置空：弹窗复用，关闭后仍可再次 Show
    }

    /// <summary>重建 8 列×4 行省份网格（Clear 会释放旧单元格）。</summary>
    private void BuildGrid()
    {
        int gap = LogicalToDeviceUnits(4);
        int pad = LogicalToDeviceUnits(5);
        int cellW = LogicalToDeviceUnits(38);
        int cellH = LogicalToDeviceUnits(32);

        _grid!.SuspendLayout();
        _grid.Controls.Clear();
        _grid.Size = new Size(
            pad * 2 + Cols * cellW + (Cols - 1) * gap,
            pad * 2 + Rows * cellH + (Rows - 1) * gap);

        for (int i = 0; i < Items.Count; i++)
        {
            int index = i;
            bool selected = i == _selectedIndex;
            var cell = new Button
            {
                Text = i == 0 ? "无" : Items[i],
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Microsoft YaHei UI", 11F,
                    selected ? FontStyle.Bold : FontStyle.Regular),
                Size = new Size(cellW, cellH),
                Location = new Point(
                    pad + (i % Cols) * (cellW + gap),
                    pad + (i / Cols) * (cellH + gap)),
                BackColor = selected ? Color.FromArgb(0, 122, 204) : Color.White,
                ForeColor = selected ? Color.White : Color.Black,
                Cursor = Cursors.Hand,
            };
            cell.FlatAppearance.BorderColor = Color.FromArgb(210, 214, 220);
            cell.Click += (_, _) =>
            {
                SelectedIndex = index;
                _drop?.Close();   // 复用对象，Close 可重复调用，不 Dispose
                Focus();
            };
            _grid.Controls.Add(cell);
        }
        _grid.ResumeLayout();
    }
}
