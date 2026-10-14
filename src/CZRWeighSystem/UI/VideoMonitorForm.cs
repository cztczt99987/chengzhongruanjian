using CZRWeighSystem.Video;

namespace CZRWeighSystem.UI;

/// <summary>
/// 视频监控弹窗（对应 PRD F7-01 多路预览）。
/// 共享主界面 CameraManager 的画面源，仅作显示视图：
/// 关闭窗体不停止取流，保存称重时的自动抓拍（F7-02）不受影响。
/// focusCameraIndex 指定时只显示该路；为空则平铺显示全部路数（菜单入口的多路预览）。
/// fullscreen=true 时以无边框全屏呈现，仅用于首页单击某路画面进入单路沉浸查看；
/// 全屏下单击画面或按 Esc 退出，切换其他路需关闭弹窗后回首页点击对应画面。
/// </summary>
public class VideoMonitorForm : Form
{
    private readonly CameraManager _cameras;
    private readonly int? _focusCameraIndex;
    private readonly bool _fullscreen;
    private readonly DateTime _openedUtc = DateTime.UtcNow;
    private readonly List<(ICameraSource Src, Action Handler)> _bindings = [];

    /// <summary>打开瞬间的残留点击忽略窗口：在首页快速双击画面时，第二击会落进刚弹出的全屏窗，若响应会"开了又立刻关"。</summary>
    internal static readonly TimeSpan ExitClickGuard = TimeSpan.FromMilliseconds(400);

    public VideoMonitorForm(CameraManager cameras, int? focusCameraIndex = null, bool fullscreen = false)
    {
        _cameras = cameras;
        _focusCameraIndex = focusCameraIndex;
        _fullscreen = fullscreen;
        Text = "视频监控（保存称重时自动抓拍留证）";
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;   // 高 DPI 下避免文字截断
        MinimizeBox = false;
        ShowInTaskbar = false;
        Size = new Size(1200, 640);
        MinimumSize = new Size(640, 400);
        Font = new Font("Microsoft YaHei UI", 10F);

        BuildUi();

        if (_fullscreen)
        {
            ApplyFullscreen(this);
            KeyPreview = true;
            KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) Close(); };
            var exitHint = BuildExitHint();
            Controls.Add(exitHint);
            exitHint.BringToFront();   // 浮在平铺画面之上
        }

        FormClosed += (_, _) => UnbindAll();
    }

    /// <summary>
    /// 沉浸全屏：无边框铺满画面所在显示器（覆盖任务栏），配合 Esc / 双击画面退出。
    /// </summary>
    internal static void ApplyFullscreen(Form form)
    {
        form.FormBorderStyle = FormBorderStyle.None;
        form.StartPosition = FormStartPosition.Manual;
        form.Bounds = Screen.FromPoint(Cursor.Position).Bounds;   // 多显示器时落在当前点击的那块屏
        form.WindowState = FormWindowState.Maximized;
        form.MinimumSize = Size.Empty;
    }

    /// <summary>全屏退出提示（浮在画面上方，不影响观看）。</summary>
    private static Label BuildExitHint()
        => new()
        {
            Text = "按 Esc 或单击画面退出全屏",
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

        var sources = SelectSources(_cameras.Sources, _focusCameraIndex);
        if (sources.Count == 1)
            Text = $"视频监控 - {sources[0].Name}";

        int count = sources.Count;
        int cols = count == 1 ? 1 : 2;
        int rows = (int)Math.Ceiling(count / (double)cols);

        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = cols,
            RowCount = rows,
        };
        for (int i = 0; i < cols; i++)
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / cols));
        for (int i = 0; i < rows; i++)
            table.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / rows));

        for (int i = 0; i < count; i++)
        {
            var src = sources[i];
            var pic = new PictureBox
            {
                Dock = DockStyle.Fill,
                SizeMode = PictureBoxSizeMode.Normal,   // 帧由 Paint 等比填满（不留黑边、不拉伸变形）
                BackColor = SystemColors.Control,
                Margin = new Padding(0),
            };

            // 帧画面等比缩放填满整个槽位（居中裁剪）：去掉画面四周的黑色留边
            pic.Paint += (_, e) =>
            {
                if (src.CurrentFrame is not { } frame) return;
                MainForm.DrawFrameFit(e.Graphics, frame, pic.ClientSize);
            };

            // 摄像头名称角标（覆盖在画面左上角）
            pic.Controls.Add(new Label
            {
                Text = $" {src.Name} ",
                AutoSize = true,
                Location = new Point(4, 4),
                BackColor = Color.FromArgb(120, 0, 0, 0),
                ForeColor = Color.White,
            });

            // 全屏模式下单击画面即退出全屏（弹窗内不切换路数：看其他路请关闭后回首页点击）
            if (_fullscreen)
                pic.Click += (_, _) => { if (ShouldExitFullscreen(_openedUtc, DateTime.UtcNow, ExitClickGuard)) Close(); };

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
            table.Controls.Add(pic);
        }
        Controls.Add(table);
    }

    /// <summary>关闭窗体时解绑帧事件（摄像头由主界面管理，继续供抓拍使用）。</summary>
    private void UnbindAll()
    {
        foreach (var (src, handler) in _bindings)
            src.PreviewFrameUpdated -= handler;
        _bindings.Clear();
    }
}
