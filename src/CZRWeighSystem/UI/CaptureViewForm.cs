using System.Diagnostics;
using CZRWeighSystem.Core;

namespace CZRWeighSystem.UI;

/// <summary>
/// 抓拍图片查看窗体（对应 PRD F7-03：查询记录时查看关联抓拍图片）。
/// 称重记录分「皮重」「毛重」两段展示，每段照片下方附带重量、时间、称重员信息；
/// 双击单张图片可用系统看图工具打开原图。
/// 另有通用构造函数（标题 + 文件列表）供黑匣子等非称重场景复用。
/// </summary>
public class CaptureViewForm : Form
{
    private readonly List<PictureBox> _pics = [];
    private bool _empty;   // 无任何抓拍图片时标记，OnShown 自动关闭

    // ===== 称重记录：皮重/毛重分段展示 =====
    public CaptureViewForm(WeighRecord record)
    {
        Text = $"抓拍图片 - {record.SerialNo}";
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;   // 高 DPI 下避免文字截断
        Font = new Font("Microsoft YaHei UI", 10F);

        var tareFiles = Video.CameraManager.FindTareCaptures(record.SerialNo);
        var grossFiles = Video.CameraManager.FindGrossCaptures(record.SerialNo);

        bool hasTare = tareFiles.Count > 0;
        bool hasGross = grossFiles.Count > 0;

        if (!hasTare && !hasGross)
        {
            _empty = true;
            MessageBox.Show($"记录 {record.SerialNo} 没有抓拍图片\n（抓拍图片保存在 exe 目录 captures/ 下）",
                "抓拍查询", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        const int picW = 300, picH = 220;
        const int pad = 16, gap = 16;
        int headerH = 28, infoH = 46, sectionGap = 24;
        int sectionH = headerH + picH + infoH + 8;

        int camCount = Math.Max(tareFiles.Count, grossFiles.Count);
        int sectionCount = (hasTare ? 1 : 0) + (hasGross ? 1 : 0);

        ClientSize = new Size(
            Math.Min(1400, picW * camCount + pad * (camCount + 1)),
            sectionH * sectionCount + sectionGap * (sectionCount - 1) + pad * 2);
        MinimumSize = new Size(600, 300);

        int y = pad;
        if (hasTare)
        {
            BuildSection("【皮重】", tareFiles, record.TareKg, record.TareTime, record.Operator,
                y, picW, picH, pad, gap, headerH);
            y += sectionH + sectionGap;
        }
        if (hasGross)
        {
            BuildSection("【毛重】", grossFiles, record.GrossKg, record.GrossTime, record.Operator,
                y, picW, picH, pad, gap, headerH);
        }

        FormClosed += (_, _) =>
        {
            foreach (var p in _pics) p.Image?.Dispose();
        };
    }

    // ===== 通用：黑匣子等只展示一组图片 =====
    public CaptureViewForm(string title, List<string> files)
    {
        Text = title;
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;
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
                Image = Image.FromFile(files[i]),
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

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        if (_empty) Close();   // 构造时已提示"无抓拍图片"，直接关闭
    }

    /// <summary>
    /// 构建一个称重段（皮重或毛重）：标题 + 横向图片 + 下方重量/时间/称重员信息。
    /// </summary>
    private void BuildSection(string header, List<string> files, double? kg,
        DateTime? time, string? op, int y, int picW, int picH, int pad, int gap, int headerH)
    {
        Controls.Add(new Label
        {
            Text = header,
            Location = new Point(pad, y),
            AutoSize = true,
            Font = new Font(Font.FontFamily, 12F, FontStyle.Bold),
        });

        for (int i = 0; i < files.Count; i++)
        {
            var pic = new PictureBox
            {
                Location = new Point(pad + i * (picW + gap), y + headerH),
                Size = new Size(picW, picH),
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Black,
                Image = Image.FromFile(files[i]),
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

        string weight = kg.HasValue ? AppConfig.Current.FormatWeight(kg.Value) : "--";
        string timeStr = time?.ToString("yyyy-MM-dd HH:mm:ss") ?? "--";
        Controls.Add(new Label
        {
            Text = $"重量：{weight}    时间：{timeStr}    称重员：{op ?? "--"}",
            Location = new Point(pad, y + headerH + picH + 10),
            AutoSize = true,
        });
    }
}
