namespace CZRWeighSystem.UI;

/// <summary>
/// 车牌省份简称选择器：外观为扁平下拉按钮，点击弹出"多列紧凑网格"选省面板
/// （无省份 + 31 个省份简称，8 列 × 4 行全部可见，无需滚动）。
/// 替代原生 ComboBox 的超长单列下拉；成员命名与 ComboBox 保持一致
/// （SelectedIndex / SelectedItem / SelectedIndexChanged / Items），便于替换与测试。
/// </summary>
internal sealed class ProvincePicker : Button
{
    /// <summary>可选省份列表：索引 0 = 无省份（空串），其后为全部省份简称。</summary>
    public static readonly IList<string> Items =
        new[] { "" }.Concat(VehiclePadDialog.Provinces.Select(c => c.ToString())).ToList();

    private int _selectedIndex;
    private ToolStripDropDown? _drop;

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

        // 右侧下拉箭头：Label 不抢占焦点，点击箭头与点击按钮本体同样展开网格
        var arrow = new Label
        {
            Text = "▼",
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Microsoft YaHei UI", 6.5F),
            ForeColor = Color.FromArgb(90, 90, 90),
            BackColor = Color.White,
            Dock = DockStyle.Right,
            Width = 18,
        };
        arrow.Click += (_, _) => ShowDropDown();
        Controls.Add(arrow);
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

    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        ShowDropDown();
    }

    // 键盘 ↓ 直接展开（ComboBox 的 Alt+↓ 习惯近似）
    protected override bool IsInputKey(Keys keyData) =>
        keyData == Keys.Down || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Down)
        {
            ShowDropDown();
            e.Handled = true;
        }
        base.OnKeyDown(e);
    }

    /// <summary>弹出多列省份网格；再次点击按钮则收起。</summary>
    private void ShowDropDown()
    {
        if (_drop != null)
        {
            _drop.Close();
            return;
        }

        // 32 项（无 + 31 省）恰为 8 列 × 4 行；尺寸按当前 DPI 缩放
        const int cols = 8;
        const int rows = 4;
        int gap = LogicalToDeviceUnits(4);
        int pad = LogicalToDeviceUnits(5);
        int cellW = LogicalToDeviceUnits(38);
        int cellH = LogicalToDeviceUnits(32);

        var panel = new Panel
        {
            BackColor = Color.FromArgb(245, 246, 248),
            Size = new Size(
                pad * 2 + cols * cellW + (cols - 1) * gap,
                pad * 2 + rows * cellH + (rows - 1) * gap),
        };

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
                    pad + (i % cols) * (cellW + gap),
                    pad + (i / cols) * (cellH + gap)),
                BackColor = selected ? Color.FromArgb(0, 122, 204) : Color.White,
                ForeColor = selected ? Color.White : Color.Black,
                Cursor = Cursors.Hand,
            };
            cell.FlatAppearance.BorderColor = Color.FromArgb(210, 214, 220);
            cell.Click += (_, _) =>
            {
                SelectedIndex = index;
                _drop?.Close();
            };
            panel.Controls.Add(cell);
        }

        var host = new ToolStripControlHost(panel)
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
        _drop.Closed += (_, _) =>
        {
            _drop.Dispose();
            _drop = null;
            Focus();
        };
        _drop.Show(this, new Point(0, Height));
    }
}
