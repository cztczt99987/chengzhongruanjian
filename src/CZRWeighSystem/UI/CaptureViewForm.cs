using System.Diagnostics;

namespace CZRWeighSystem.UI;

/// <summary>
/// 抓拍图片查看窗体（对应 PRD F7-03：查询记录时查看关联抓拍图片）。
/// 多张图片横向排列缩放展示，双击可用系统看图工具打开原图。
/// </summary>
public class CaptureViewForm : Form
{
    private readonly List<PictureBox> _pics = [];

    public CaptureViewForm(string serialNo, List<string> files)
    {
        Text = $"抓拍图片 - {serialNo}";
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;   // 高 DPI 下避免文字截断
        Size = new Size(Math.Min(1400, 340 * files.Count + 40), 420);
        MinimumSize = new Size(600, 300);
        Font = new Font("Microsoft YaHei UI", 10F);

        int pw = (ClientSize.Width - 16 * (files.Count + 1)) / files.Count;
        for (int i = 0; i < files.Count; i++)
        {
            var pic = new PictureBox
            {
                Location = new Point(16 + i * (pw + 16), 16),
                Size = new Size(pw, ClientSize.Height - 32),
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Black,
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                Image = Image.FromFile(files[i]),   // 载入副本，窗体关闭时释放
                Tag = files[i],
            };
            pic.DoubleClick += (_, _) =>
            {
                if (pic.Tag is string path)
                    Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            };
            Controls.Add(pic);
            _pics.Add(pic);
        }

        FormClosed += (_, _) =>
        {
            foreach (var p in _pics) p.Image?.Dispose();
        };
    }
}
