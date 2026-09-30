using System.Drawing.Drawing2D;

namespace CZRWeighSystem.UI;

/// <summary>
/// 空/重车自卸车图标（现代扁平渐变风格，纯代码绘制、透明背景，免维护外部图片资源）：
/// 橙色渐变圆角车身 + 平滑货堆 + 银毂车轮 + 地面投影；
/// 空车 = 车厢举升卸货姿态，重车 = 车厢满载货堆冒尖。
/// </summary>
internal static class TruckIcon
{
    public static Bitmap Draw(bool loaded, int size = 56)
    {
        var bmp = new Bitmap(size, size);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float s = size / 128f;   // 图形按 128×128 画布设计，等比缩放到目标尺寸
        g.ScaleTransform(s, s);

        // 地面投影（提升立体感）
        using (var shadow = new SolidBrush(Color.FromArgb(26, 40, 40, 40)))
            g.FillEllipse(shadow, 16, 108, 98, 12);

        if (loaded)
        {
            // 重车：水平车厢 + 平滑货堆冒尖
            using (var path = RoundedRect(16, 44, 66, 36, 9))
            using (var brush = BodyBrush(16, 40, 66, 48))
                g.FillPath(brush, path);
            var mound = Pt(20, 46, 32, 28, 48, 24, 64, 30, 78, 46);
            using (var brush = new SolidBrush(Color.FromArgb(141, 110, 60)))
                g.FillClosedCurve(brush, mound, FillMode.Winding, 0.85f);
        }
        else
        {
            // 空车：车厢举升卸货姿态（旋转的圆角车厢）
            var state = g.Save();
            g.TranslateTransform(50, 62);
            g.RotateTransform(-36);
            using (var path = RoundedRect(-34, -14, 68, 28, 9))
            using (var brush = BodyBrush(-34, -14, 34, 14))
                g.FillPath(brush, path);
            g.Restore(state);
        }

        // 驾驶室（车头朝右）与车窗
        var cab = Pt(84, 86, 84, 50, 102, 50, 116, 72, 116, 86);
        using (var brush = BodyBrush(84, 50, 116, 86))
            g.FillPolygon(brush, cab);
        using (var win = RoundedRect(88, 55, 20, 15, 5))
        using (var brush = new LinearGradientBrush(new Rectangle(88, 55, 20, 15),
                   Color.FromArgb(214, 234, 248), Color.FromArgb(150, 200, 235), 90f))
            g.FillPath(brush, win);

        // 底盘与车轮（深灰胎 + 银色轮毂）
        using (var dark = new SolidBrush(Color.FromArgb(66, 70, 76)))
        {
            g.FillRectangle(dark, 30, 82, 74, 10);
            g.FillEllipse(dark, 33, 83, 26, 26);
            g.FillEllipse(dark, 83, 83, 26, 26);
        }
        using (var rim = new SolidBrush(Color.FromArgb(208, 213, 220)))
        {
            g.FillEllipse(rim, 39, 89, 14, 14);
            g.FillEllipse(rim, 89, 89, 14, 14);
        }
        using (var hub = new SolidBrush(Color.FromArgb(120, 126, 134)))
        {
            g.FillEllipse(hub, 43, 93, 6, 6);
            g.FillEllipse(hub, 93, 93, 6, 6);
        }
        return bmp;
    }

    /// <summary>橙色渐变车身画刷（上浅下深，随形状包围盒取向）。</summary>
    private static Brush BodyBrush(int x, int y, int x2, int y2) =>
        new LinearGradientBrush(new Rectangle(x, y, x2 - x, y2 - y),
            Color.FromArgb(255, 196, 92), Color.FromArgb(243, 122, 12), 90f);

    /// <summary>圆角矩形路径。</summary>
    private static GraphicsPath RoundedRect(int x, int y, int w, int h, int r)
    {
        var path = new GraphicsPath();
        int d = r * 2;
        path.AddArc(x, y, d, d, 180, 90);
        path.AddArc(x + w - d, y, d, d, 270, 90);
        path.AddArc(x + w - d, y + h - d, d, d, 0, 90);
        path.AddArc(x, y + h - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    /// <summary>扁平整数对 → PointF 数组。</summary>
    private static PointF[] Pt(params int[] v)
    {
        var pts = new PointF[v.Length / 2];
        for (int i = 0; i < pts.Length; i++)
            pts[i] = new PointF(v[i * 2], v[i * 2 + 1]);
        return pts;
    }
}
