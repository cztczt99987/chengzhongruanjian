namespace CZRWeighSystem.UI;

/// <summary>
/// 车牌省份简称选择器：外观为扁平下拉按钮，点击弹出"多列紧凑网格"选省面板
/// （无省份 + 31 个省份简称，8 列 × 4 行全部可见，无需滚动）。
/// 替代原生 ComboBox 的超长单列下拉；成员命名与 ComboBox 保持一致
/// （SelectedIndex / SelectedItem / SelectedIndexChanged / Items），便于替换与测试。
///
/// 消息时序要点（三处易踩坑，改动须谨慎）：
/// 1. 打开必须 BeginInvoke 延迟到"当前这次鼠标点击的消息处理完"之后——
///    若在 MouseDown 中同步 Show，同一笔 MouseUp 会被 ToolStrip 的菜单消息过滤器
///    判为"外部点击"而立即关闭，第二次起就再也弹不出来；
/// 2. 弹窗每次新建、不重用：菜单过滤器会抑制"因外部点击关闭的同一实例在同一消息
///    周期内重新打开"；
/// 3. Closed 中只摘引用，Dispose 也要延迟——关闭瞬间仍处于菜单内部消息链路，
///    立即释放会抛 ObjectDisposedException。
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
        // 右侧留 14px 给自绘 ▼ 箭头（箭头画在 Width-13 起 8px 宽）；
        // 原 20px 挤得文字可用宽仅约 27px，目标机字体渲染偏宽时"黑"字被右缘裁切（2026-10-04 实机反馈）
        Padding = new Padding(7, 0, 14, 0);
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

    // 唯一鼠标入口：弹窗打开期间点击本按钮，这次点击会被菜单当作"外部点击"吞掉
    // 并自动关窗（标准菜单行为），按钮收不到消息，故此处只需处理"未打开→打开"。
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Left) RequestOpen();
    }

    // 键盘 ↓ / 空格 / 回车打开（键盘消息无 MouseUp 判定问题，直接排队即可）
    protected override bool IsInputKey(Keys keyData) =>
        keyData == Keys.Down || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Down or Keys.Space or Keys.Enter)
        {
            RequestOpen();
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
            // 选择器释放时连带关掉弹窗（弹窗内部项会随它一并释放）。
            // 宿主窗体关闭时框架可能已销毁弹层窗口（句柄消失），此时再 Close 会经
            // SetVisibleCore → Handle → CreateHandle 抛 ObjectDisposedException:
            // 'System.Windows.Forms.ToolStripDropDown'（2026-10-04 实机复现）；
            // 窗口已销毁则无需再关，直接 Dispose 做托管清理
            var drop = _drop;
            _drop = null;
            _grid = null;
            if (drop is { IsDisposed: false })
            {
                if (drop.IsHandleCreated) drop.Close();
                drop.Dispose();
            }
        }
        base.Dispose(disposing);
    }

    /// <summary>请求打开：延迟到当前鼠标/键盘消息周期之外再建窗，避免被菜单过滤器立即关闭。</summary>
    private void RequestOpen()
    {
        if (_drop != null || IsDisposed || !IsHandleCreated) return;
        BeginInvoke(new Action(CreateDropDown));
    }

    /// <summary>新建并弹出省份网格（internal 可见性仅供测试同步调用）。</summary>
    internal void CreateDropDown()
    {
        if (_drop != null || IsDisposed) return;

        _grid = BuildGrid();
        var host = new ToolStripControlHost(_grid)
        {
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };
        var drop = new ToolStripDropDown
        {
            Padding = Padding.Empty,
            Margin = Padding.Empty,
            DropShadowEnabled = true,
        };
        drop.Items.Add(host);
        drop.Closed += OnDropClosed;
        // 弹层可能被框架绕过 Closed 直接释放（宿主窗体关闭时菜单系统先回收活动弹层）：
        // 释放即摘除陈旧引用，否则 _drop != null 会一直挡住重开、Dispose 还会访问已释放对象
        drop.Disposed += (_, _) =>
        {
            if (ReferenceEquals(_drop, drop))
            {
                _drop = null;
                _grid = null;
            }
        };
        _drop = drop;
        drop.Show(this, new Point(0, Height));
    }

    private void OnDropClosed(object? sender, EventArgs e)
    {
        var drop = (ToolStripDropDown)sender!;
        drop.Closed -= OnDropClosed;
        if (ReferenceEquals(_drop, drop))
        {
            _drop = null;
            _grid = null;
        }
        // 关闭瞬间仍在菜单消息链路中：延迟释放，避免重入访问已释放对象
        if (IsHandleCreated && !IsDisposed)
            BeginInvoke(new Action(() =>
            {
                if (!drop.IsDisposed) drop.Dispose();
                if (CanFocus) Focus();   // 焦点还给选择器，便于键盘继续操作
            }));
        else if (!drop.IsDisposed)
            drop.Dispose();
    }

    /// <summary>构建 8 列×4 行省份网格面板。</summary>
    private Panel BuildGrid()
    {
        int gap = LogicalToDeviceUnits(4);
        int pad = LogicalToDeviceUnits(5);
        int cellW = LogicalToDeviceUnits(38);
        int cellH = LogicalToDeviceUnits(32);

        var grid = new Panel
        {
            BackColor = Color.FromArgb(245, 246, 248),
            Size = new Size(
                pad * 2 + Cols * cellW + (Cols - 1) * gap,
                pad * 2 + Rows * cellH + (Rows - 1) * gap),
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
                _drop?.Close();
            };
            grid.Controls.Add(cell);
        }
        return grid;
    }
}
