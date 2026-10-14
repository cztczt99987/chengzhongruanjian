using CZRWeighSystem.Video;

namespace CZRWeighSystem.UI;

/// <summary>
/// 视频监控弹窗（对应 PRD F7-01 多路预览）：平铺显示全部路数，仅供菜单入口 /「视频监控」快捷方式使用。
/// 共享主界面 CameraManager 的画面源，仅作显示视图：
/// 关闭窗体不停止取流，保存称重时的自动抓拍（F7-02）不受影响。
/// focusCameraIndex 指定时只显示该路；fullscreen=true 时以无边框全屏呈现——
/// 该组合仅用于首页放大画面右键菜单的「全屏放大」。全屏内右键可：
/// 退出全屏、双路展示（当前路 + 下一路左右并排，再次点击「单路展示」切回）、手动拍照
/// （拍右键所在的那一路）；单击画面或按 Esc 退出回首页。
/// </summary>
public class VideoMonitorForm : Form
{
    private readonly CameraManager _cameras;
    private readonly bool _fullscreen;
    private readonly DateTime _openedUtc = DateTime.UtcNow;
    private readonly List<(ICameraSource Src, Action Handler)> _bindings = [];
    private readonly List<ContextMenuStrip> _menus = [];   // 每次重建画面新建的右键菜单（重建时统一释放）

    private readonly bool _showAll;    // 菜单入口：平铺全部路数（无右键菜单、不支持单/双路切换）
    private int _primaryIndex;         // 单路/双路的第一路（Sources 全局序号）
    private int? _secondIndex;         // 双路时的第二路序号；null = 单路
    private TableLayoutPanel? _grid;

    private Label? _exitHint;                 // 全屏退出提示（手动拍照后临时改显保存结果）
    private System.Windows.Forms.Timer? _hintTimer;
    private const string ExitHintText = "Esc / 单击画面退出全屏　右键：双路展示 · 手动拍照";

    /// <summary>打开瞬间的残留点击忽略窗口：在首页快速双击画面时，第二击会落进刚弹出的全屏窗，若响应会"开了又立刻关"。</summary>
    internal static readonly TimeSpan ExitClickGuard = TimeSpan.FromMilliseconds(400);

    public VideoMonitorForm(CameraManager cameras, int? focusCameraIndex = null, bool fullscreen = false)
    {
        _cameras = cameras;
        _fullscreen = fullscreen;
        Text = "视频监控（保存称重时自动抓拍留证）";
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;   // 高 DPI 下避免文字截断
        MinimizeBox = false;
        ShowInTaskbar = false;
        Size = new Size(1200, 640);
        MinimumSize = new Size(640, 400);
        Font = new Font("Microsoft YaHei UI", 10F);

        // focus 为空或越界 → 回退为"平铺全部"（菜单入口行为）
        _showAll = focusCameraIndex is not int i || i < 0 || i >= cameras.Sources.Count;
        _primaryIndex = _showAll ? 0 : focusCameraIndex!.Value;

        BuildUi();

        if (_fullscreen)
        {
            ApplyFullscreen(this);
            KeyPreview = true;
            KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) Close(); };
            _exitHint = BuildExitHint();
            Controls.Add(_exitHint);
            _exitHint.BringToFront();   // 浮在画面之上
        }

        FormClosed += (_, _) =>
        {
            _hintTimer?.Stop();
            UnbindAll();
            DisposeMenus();
        };
    }

    /// <summary>
    /// 沉浸全屏：无边框铺满画面所在显示器（覆盖任务栏），配合 Esc / 单击画面退出。
    /// </summary>
    internal static void ApplyFullscreen(Form form)
    {
        form.FormBorderStyle = FormBorderStyle.None;
        form.StartPosition = FormStartPosition.Manual;
        form.Bounds = Screen.FromPoint(Cursor.Position).Bounds;   // 多显示器时落在当前点击的那块屏
        form.WindowState = FormWindowState.Maximized;
        form.MinimumSize = Size.Empty;
    }

    /// <summary>全屏退出提示（浮在画面上方，不影响观看；手动拍照后临时改显保存结果）。</summary>
    private static Label BuildExitHint()
        => new()
        {
            Text = ExitHintText,
            AutoSize = true,
            BackColor = Color.FromArgb(140, 0, 0, 0),
            ForeColor = Color.White,
            Location = new Point(12, 12),
        };

    /// <summary>全屏下单击画面是否应退出：打开后经过保护窗口（Guard）的单击才生效。</summary>
    internal static bool ShouldExitFullscreen(DateTime openedUtc, DateTime now, TimeSpan guard)
        => now - openedUtc >= guard;

    /// <summary>按聚焦索引选择要显示的摄像头源；索引为空或非法时回退显示全部。</summary>
    internal static IReadOnlyList<ICameraSource> SelectSources(IReadOnlyList<ICameraSource> all, int? focus)
        => focus is int i && i >= 0 && i < all.Count ? new[] { all[i] } : all;

    private void BuildUi()
    {
        if (!AppConfig.Current.CameraEnabled || _cameras.Sources.Count == 0)
        {
            Controls.Add(new Label
            {
                Text = "视频监控未启用或未配置摄像头。\r\n请用管理员账号在菜单「视频 → 摄像头设置」中添加。",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Color.Gray,
            });
            return;
        }

        RebuildGrid();
    }

    /// <summary>
    /// 画面布局的唯一重建入口（初始构建与全屏内单路/双路切换都走这里）：
    /// 先解绑旧画面帧事件并释放旧表格与右键菜单，再按当前模式建新表格——
    /// 切换不残留旧控件、旧事件绑定，避免多路画面串帧。
    /// </summary>
    private void RebuildGrid()
    {
        UnbindAll();
        DisposeMenus();
        if (_grid != null)
        {
            Controls.Remove(_grid);
            _grid.Dispose();
            _grid = null;
        }

        var all = _cameras.Sources;
        // 当前要显示的 (全局序号, 源)：菜单入口=全部；全屏=单路或双路
        var tiles = new List<(int Index, ICameraSource Src)>();
        if (_showAll)
        {
            for (int i = 0; i < all.Count; i++) tiles.Add((i, all[i]));
        }
        else
        {
            tiles.Add((_primaryIndex, all[_primaryIndex]));
            if (_secondIndex is int s) tiles.Add((s, all[s]));
        }

        Text = _showAll
            ? (tiles.Count == 1 ? $"视频监控 - {tiles[0].Src.Name}" : "视频监控（保存称重时自动抓拍留证）")
            : (_secondIndex.HasValue
                ? $"视频监控 - {tiles[0].Src.Name} + {tiles[1].Src.Name}（双路）"
                : $"视频监控 - {tiles[0].Src.Name}");

        int count = tiles.Count;
        // 菜单入口：1 路单列、其余双列；全屏：单路单列、双路双列
        int cols = count == 1 ? 1 : 2;
        int rows = (int)Math.Ceiling(count / (double)cols);

        _grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = cols,
            RowCount = rows,
        };
        for (int c = 0; c < cols; c++)
            _grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / cols));
        for (int r = 0; r < rows; r++)
            _grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / rows));

        foreach (var (camIndex, src) in tiles)
            _grid.Controls.Add(BuildTile(camIndex, src));

        Controls.Add(_grid);
        _exitHint?.BringToFront();   // 重建后提示重新浮到最上层
    }

    /// <summary>构建一块画面槽位（绑定帧事件；全屏模式下挂退出点击与右键菜单）。</summary>
    private PictureBox BuildTile(int camIndex, ICameraSource src)
    {
        var pic = new PictureBox
        {
            Dock = DockStyle.Fill,
            SizeMode = PictureBoxSizeMode.Normal,   // 帧由 Paint 等比填满（不留黑边、不拉伸变形）
            BackColor = SystemColors.Control,
            Margin = new Padding(0),
        };

        pic.Paint += (_, e) =>
        {
            if (src.CurrentFrame is not { } frame) return;
            MainForm.DrawFrameFit(e.Graphics, frame, pic.ClientSize);
        };

        // 摄像头名称角标（覆盖在画面左上角）
        var nameLabel = new Label
        {
            Text = $" {src.Name} ",
            AutoSize = true,
            Location = new Point(4, 4),
            BackColor = Color.FromArgb(120, 0, 0, 0),
            ForeColor = Color.White,
        };
        pic.Controls.Add(nameLabel);

        if (_fullscreen)
        {
            // 单击画面即退出全屏（打开 400ms 内的残留点击忽略，避免首页双击第二击"开了又关"）
            pic.Click += (_, _) => { if (ShouldExitFullscreen(_openedUtc, DateTime.UtcNow, ExitClickGuard)) Close(); };
            var menu = BuildFullscreenMenu(camIndex);
            pic.ContextMenuStrip = menu;
            nameLabel.ContextMenuStrip = menu;   // 名称标签是画面的子控件，不继承父级菜单
        }

        // 帧刷新：可能来自非 UI 线程，转发到 UI 线程（窗体关闭后不再处理）
        void OnFrame()
        {
            try
            {
                if (!IsHandleCreated || IsDisposed) return;
                _ = BeginInvoke(() =>
                {
                    if (IsDisposed) return;
                    pic.Invalidate();   // Paint 重新绘制当前帧
                });
            }
            catch (ObjectDisposedException) { /* 窗体关闭瞬间的竞争，忽略 */ }
        }

        src.PreviewFrameUpdated += OnFrame;
        _bindings.Add((src, OnFrame));
        return pic;
    }

    /// <summary>全屏画面右键菜单：退出全屏 / 双路展示（单路展示）/ 手动拍照（作用于右键所在的那一路）。</summary>
    private ContextMenuStrip BuildFullscreenMenu(int camIndex)
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("退出全屏", ShortcutIcon.Draw("shrink", 16), (_, _) => Close());

        bool dual = _secondIndex.HasValue;
        var toggle = new ToolStripMenuItem(
            dual ? "单路展示" : "双路展示",
            ShortcutIcon.Draw(dual ? "shrink" : "dual", 16),
            (_, _) => ToggleDual());
        // 只有一路摄像头时无法双路（单路模式下置灰；双路模式下始终可切回）
        if (!dual && _cameras.Sources.Count < 2) toggle.Enabled = false;
        menu.Items.Add(toggle);

        menu.Items.Add("手动拍照", ShortcutIcon.Draw("camera", 16), (_, _) => ManualShot(camIndex));
        _menus.Add(menu);
        return menu;
    }

    /// <summary>
    /// 单路 ↔ 双路切换：双路 = 当前路 + 下一路（最后一路回绕到第一路）；
    /// 再次调用即切回单路。切换只重建画面表格，取流源不受影响。
    /// </summary>
    internal void ToggleDual()
    {
        int count = _cameras.Sources.Count;
        if (count < 2) return;
        _secondIndex = _secondIndex.HasValue
            ? null
            : (_primaryIndex + 1) % count;
        RebuildGrid();
    }

    /// <summary>全屏内当前是否处于双路展示（测试用）。</summary>
    internal bool IsDual => _secondIndex.HasValue;

    /// <summary>手动拍照指定路：存到 manualCaptures，结果在左上角提示 2.5 秒（不弹窗打断观看）。</summary>
    private void ManualShot(int camIndex)
    {
        string? file = _cameras.CaptureManual(camIndex);
        FlashHint(file == null
            ? "暂无画面，无法拍照"
            : $"手动拍照已保存：{Path.GetFileName(file)}");
    }

    /// <summary>临时把左上角提示换成拍照结果，2.5 秒后恢复退出提示。</summary>
    private void FlashHint(string text)
    {
        if (_exitHint == null) return;
        _exitHint.Text = text;
        _hintTimer?.Stop();
        _hintTimer = new System.Windows.Forms.Timer { Interval = 2500 };
        _hintTimer.Tick += (_, _) =>
        {
            if (_exitHint != null) _exitHint.Text = ExitHintText;
            _hintTimer!.Stop();
        };
        _hintTimer.Start();
    }

    /// <summary>释放历次重建画面时创建的右键菜单及其图标（菜单不在 Controls 树里，需手动释放）。</summary>
    private void DisposeMenus()
    {
        foreach (var m in _menus)
        {
            foreach (ToolStripItem item in m.Items) item.Image?.Dispose();
            m.Dispose();
        }
        _menus.Clear();
    }

    /// <summary>关闭窗体/重建画面前解绑帧事件（摄像头由主界面管理，继续供抓拍使用）。</summary>
    private void UnbindAll()
    {
        foreach (var (src, handler) in _bindings)
            src.PreviewFrameUpdated -= handler;
        _bindings.Clear();
    }
}
