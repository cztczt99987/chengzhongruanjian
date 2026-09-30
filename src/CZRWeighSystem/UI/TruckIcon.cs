using System.Drawing.Drawing2D;

namespace CZRWeighSystem.UI;

/// <summary>
/// 空/重车自卸车图标（参考目标版式工具栏的自卸车剪影，纯代码绘制、透明背景，
/// 免维护外部图片资源）：空车 = 车厢举升卸货，重车 = 车厢满载货堆。
/// </summary>
internal static class TruckIcon
{
    /// <summary>绘制自卸车图标。loaded=false 空车（空厢举升），true 重车（满载）。</summary>
    public static Bitmap Draw(bool loaded, int size = 56)
    {
        var bmp = new Bitmap(size, size);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float s = size / 128f;   // 图形按 128×128 画布设计，等比缩放到目标尺寸
        g.ScaleTransform(s, s);

        using var body = new SolidBrush(Color.FromArgb(245, 154, 35));   // 车身橙
        using var dark = new SolidBrush(Color.FromArgb(80, 80, 80));     // 车轮/底盘
        using var glass = new SolidBrush(Color.FromArgb(190, 224, 245)); // 车窗/轮毂
        using var heap = new SolidBrush(Color.FromArgb(170, 110, 40));   // 货堆棕
        using var pen = new Pen(Color.FromArgb(120, 72, 20), 4);

        if (loaded)
        {
            // 重车：水平车厢装满 + 货堆冒尖
            var bed = Pt(18, 56, 80, 56, 76, 42, 22, 42);
            g.FillPolygon(body, bed);
            g.DrawPolygon(pen, bed);
            var mound = Pt(22, 42, 34, 28, 52, 26, 68, 34, 76, 42);
            g.FillPolygon(heap, mound);
            g.DrawPolygon(pen, mound);
        }
        else
        {
            // 空车：车厢举升（卸货姿态）
            var bed = Pt(12, 78, 56, 30, 68, 40, 24, 88);
            g.FillPolygon(body, bed);
            g.DrawPolygon(pen, bed);
        }

        // 驾驶室与车窗（车头朝右）
        var cab = Pt(82, 84, 82, 52, 100, 52, 114, 72, 114, 84);
        g.FillPolygon(body, cab);
        g.DrawPolygon(pen, cab);
        var win = Pt(87, 57, 99, 57, 107, 68, 87, 68);
        g.FillPolygon(glass, win);

        // 底盘与车轮
        g.FillRectangle(dark, 26, 84, 82, 8);
        g.FillEllipse(dark, 34, 86, 24, 24);
        g.FillEllipse(dark, 82, 86, 24, 24);
        g.FillEllipse(glass, 41, 93, 10, 10);
        g.FillEllipse(glass, 89, 93, 10, 10);
        return bmp;
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
