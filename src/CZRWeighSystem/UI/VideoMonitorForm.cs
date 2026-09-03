using CZRWeighSystem.Video;

namespace CZRWeighSystem.UI;

/// <summary>
/// 视频监控弹窗（对应 PRD F7-01 多路预览）。
/// 共享主界面 CameraManager 的画面源，仅作显示视图：
/// 关闭窗体不停止取流，保存称重时的自动抓拍（F7-02）不受影响。
/// </summary>
public class VideoMonitorForm : Form
{
    private readonly CameraManager _cameras;
    private readonly List<(ICameraSource Src, Action Handler)> _bindings = [];

    public VideoMonitorForm(CameraManager cameras)
    {
        _cameras = cameras;
        Text = "视频监控（保存称重时自动抓拍留证）";
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;   // 高 DPI 下避免文字截断
        MinimizeBox = false;
        ShowInTaskbar = false;
        Size = new Size(1200, 640);
        MinimumSize = new Size(640, 400);
        Font = new Font("Microsoft YaHei UI", 10F);

        BuildUi();
        FormClosed += (_, _) => UnbindAll();
    }

    private void BuildUi()
    {
        if (!AppConfig.Current.CameraEnabled || _cameras.Sources.Count == 0)
        {
            Controls.Add(new Label
            {
                Text = "视频监控未启用或无可用摄像头。\r\n可在 config.json 中设置 CameraEnabled / CameraRtspUrls。",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Color.Gray,
            });
            return;
        }

        var sources = _cameras.Sources;
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
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Black,
                Margin = new Padding(3),
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

            // 帧刷新：可能来自非 UI 线程，转发到 UI 线程（窗体关闭后不再处理）
            void OnFrame()
            {
                try
                {
                    if (!IsHandleCreated || IsDisposed) return;
                    _ = BeginInvoke(() =>
                    {
                        if (IsDisposed) return;
                        pic.Image = src.CurrentFrame;
                        pic.Refresh();
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
