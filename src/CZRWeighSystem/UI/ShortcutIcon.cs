using System.Drawing.Drawing2D;

namespace CZRWeighSystem.UI;

/// <summary>
/// 界面图标（纯代码绘制的彩色扁平风、透明背景）：
/// 顶部工具栏快捷方式——记录查询=文档放大镜、报表统计=柱状图、视频监控=显示器、基础资料=数据库、
/// 计算器=计算器、退出=电源键；
/// 记录列表右键菜单——当日记录=列表、打印预览=打印机、过磅视图=摄像机。
/// 统一 64×64 画布与圆角语言，按目标尺寸等比缩放。
/// </summary>
internal static class ShortcutIcon
{
    // 统一配色（与白色工具栏搭配的商务彩色）
    private static readonly Color Blue = Color.FromArgb(45, 125, 210);
    private static readonly Color BlueDark = Color.FromArgb(30, 96, 168);
    private static readonly Color Green = Color.FromArgb(63, 173, 102);
    private static readonly Color Orange = Color.FromArgb(240, 150, 30);
    private static readonly Color Paper = Color.FromArgb(255, 255, 255);
    private static readonly Color Ink = Color.FromArgb(74, 84, 98);
    private static readonly Color Red = Color.FromArgb(214, 69, 65);

    public static Bitmap Draw(string name, int size = 40)
    {
        var bmp = new Bitmap(size, size);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.ScaleTransform(size / 64f, size / 64f);

        switch (name)
        {
            case "query": DrawQuery(g); break;
            case "report": DrawReport(g); break;
            case "video": DrawVideo(g); break;
            case "base": DrawBase(g); break;
            case "calc": DrawCalc(g); break;
            case "exit": DrawExit(g); break;
            case "daily": DrawDaily(g); break;
            case "printer": DrawPrinter(g); break;
            case "camera": DrawCamera(g); break;
            case "expand": DrawExpand(g, outward: true); break;    // 全屏放大
            case "shrink": DrawExpand(g, outward: false); break;   // 取消半屏 / 退出全屏
            default: throw new ArgumentException($"未知快捷图标：{name}", nameof(name));
        }
        return bmp;
    }

    /// <summary>记录查询：橙色文档 + 蓝色放大镜。</summary>
    private static void DrawQuery(Graphics g)
    {
        using var doc = RoundedRect(10, 6, 34, 44, 5);
        using var b = new LinearGradientBrush(new Rectangle(10, 6, 34, 44),
            Color.FromArgb(255, 196, 82), Color.FromArgb(238, 144, 20), 90f);
        g.FillPath(b, doc);
        // 文档上的文字线
        using var line = new SolidBrush(Color.FromArgb(150, 110, 30));
        g.FillRectangle(line, 17, 16, 20, 3);
        g.FillRectangle(line, 17, 23, 20, 3);
        g.FillRectangle(line, 17, 30, 13, 3);
        // 放大镜（右下）
        using var ring = new Pen(BlueDark, 5) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawEllipse(ring, 24, 28, 24, 24);
        g.DrawLine(ring, 42, 46, 52, 56);
        using var glass = new SolidBrush(Color.FromArgb(60, 45, 125, 210));
        g.FillEllipse(glass, 27, 31, 18, 18);
    }

    /// <summary>报表统计：文档 + 三根彩色柱状图。</summary>
    private static void DrawReport(Graphics g)
    {
        using var doc = RoundedRect(10, 6, 44, 52, 5);
        using var b = new SolidBrush(Paper);
        g.FillPath(b, doc);
        using var border = new Pen(Color.FromArgb(206, 214, 226), 2);
        g.DrawPath(border, doc);
        // 标题线
        using var title = new SolidBrush(Color.FromArgb(150, 160, 172));
        g.FillRectangle(title, 17, 13, 18, 3);
        // 柱状图（蓝/绿/橙）
        using (var bar = new SolidBrush(Blue)) g.FillRectangle(bar, 17, 38, 8, 13);
        using (var bar = new SolidBrush(Green)) g.FillRectangle(bar, 29, 30, 8, 21);
        using (var bar = new SolidBrush(Orange)) g.FillRectangle(bar, 41, 24, 8, 27);
    }

    /// <summary>视频监控：蓝色显示器 + 播放三角。</summary>
    private static void DrawVideo(Graphics g)
    {
        // 底座
        using var dark = new SolidBrush(BlueDark);
        g.FillRectangle(dark, 26, 50, 12, 5);
        g.FillRectangle(dark, 18, 54, 28, 4);
        // 屏幕
        using var screen = RoundedRect(6, 8, 52, 42, 6);
        using var frame = new LinearGradientBrush(new Rectangle(6, 8, 52, 42),
            Color.FromArgb(74, 148, 224), BlueDark, 90f);
        g.FillPath(frame, screen);
        using var inner = RoundedRect(10, 12, 44, 34, 3);
        using var ib = new SolidBrush(Color.FromArgb(20, 44, 78));
        g.FillPath(ib, inner);
        // 播放三角
        using var play = new SolidBrush(Color.White);
        g.FillPolygon(play, new[] { new Point(27, 20), new Point(27, 38), new Point(42, 29) });
    }

    /// <summary>基础资料：三层数据库圆柱。</summary>
    private static void DrawBase(Graphics g)
    {
        Rectangle body = new(12, 14, 40, 38);
        using var bodyBrush = new SolidBrush(Blue);
        g.FillRectangle(bodyBrush, body);
        using var side = new SolidBrush(BlueDark);
        // 两层分隔环
        g.FillEllipse(side, 12, 26, 40, 12);
        g.FillEllipse(side, 12, 38, 40, 12);
        // 顶/中/底三个椭圆面
        using var topFace = new SolidBrush(Color.FromArgb(123, 186, 240));
        g.FillEllipse(topFace, 12, 8, 40, 12);
        g.FillEllipse(topFace, 12, 20, 40, 12);
        g.FillEllipse(topFace, 12, 32, 40, 12);
        using var rim = new Pen(BlueDark, 2);
        g.DrawEllipse(rim, 12, 8, 40, 12);
        g.DrawEllipse(rim, 12, 20, 40, 12);
        g.DrawEllipse(rim, 12, 32, 40, 12);
        g.DrawEllipse(rim, 12, 44, 40, 12);
    }

    /// <summary>计算器：灰身 + 深色屏 + 按键点阵。</summary>
    private static void DrawCalc(Graphics g)
    {
        using var shell = RoundedRect(14, 4, 36, 56, 7);
        using var b = new LinearGradientBrush(new Rectangle(14, 4, 36, 56),
            Color.FromArgb(238, 241, 245), Color.FromArgb(198, 206, 216), 90f);
        g.FillPath(b, shell);
        using var border = new Pen(Ink, 2);
        g.DrawPath(border, shell);
        // 屏幕
        using var scr = RoundedRect(19, 10, 26, 11, 2);
        using var sb = new SolidBrush(Color.FromArgb(58, 68, 82));
        g.FillPath(sb, scr);
        // 按键 3×4
        using var key = new SolidBrush(Color.FromArgb(120, 130, 144));
        for (int r = 0; r < 4; r++)
            for (int c = 0; c < 3; c++)
                g.FillEllipse(key, 20 + c * 8, 27 + r * 7, 5, 5);
    }

    /// <summary>退出：红色电源键（圆环缺口 + 竖线）。</summary>
    private static void DrawExit(Graphics g)
    {
        using var pen = new Pen(Red, 6) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        // 圆环缺口朝上（GDI+ 角度 0=右、顺时针：340° 起扫 220°，缺口 200°→340° 经过顶部 270°）
        g.DrawArc(pen, 12, 12, 40, 40, 340, 220);
        g.DrawLine(pen, 32, 8, 32, 30);
    }

    /// <summary>当日记录：蓝框列表（表头 + 三行数据）。</summary>
    private static void DrawDaily(Graphics g)
    {
        using var card = RoundedRect(8, 6, 48, 52, 5);
        using var paper = new SolidBrush(Paper);
        g.FillPath(paper, card);
        using var border = new Pen(Blue, 3);
        g.DrawPath(border, card);
        using var head = new SolidBrush(Blue);
        g.FillRectangle(head, 14, 13, 36, 8);
        using var line = new SolidBrush(Color.FromArgb(126, 136, 150));
        for (int i = 0; i < 3; i++)
            g.FillRectangle(line, 14, 27 + i * 9, 36, 3);
    }

    /// <summary>打印预览：灰色打印机 + 进出纸。</summary>
    private static void DrawPrinter(Graphics g)
    {
        // 进纸（上方白纸）
        using var top = new SolidBrush(Paper);
        g.FillRectangle(top, 18, 4, 28, 14);
        using var topLine = new SolidBrush(Color.FromArgb(150, 160, 172));
        g.FillRectangle(topLine, 22, 8, 20, 2);
        g.FillRectangle(topLine, 22, 12, 14, 2);
        // 机身
        using var body = RoundedRect(6, 18, 52, 26, 4);
        using var bb = new SolidBrush(Color.FromArgb(74, 84, 98));
        g.FillPath(bb, body);
        // 出纸（下方白纸）
        using var outc = new SolidBrush(Paper);
        g.FillRectangle(outc, 16, 40, 32, 18);
        using var outLine = new SolidBrush(Color.FromArgb(126, 136, 150));
        g.FillRectangle(outLine, 20, 46, 24, 2);
        g.FillRectangle(outLine, 20, 51, 18, 2);
        // 状态灯
        using var led = new SolidBrush(Green);
        g.FillEllipse(led, 46, 23, 6, 6);
    }

    /// <summary>过磅视图：蓝色摄像机（机身 + 镜头 + 支座）。</summary>
    private static void DrawCamera(Graphics g)
    {
        using var body = RoundedRect(10, 18, 34, 24, 4);
        using var bb = new LinearGradientBrush(new Rectangle(10, 18, 34, 24),
            Color.FromArgb(74, 148, 224), BlueDark, 90f);
        g.FillPath(bb, body);
        // 镜头
        using var lens = new SolidBrush(Color.FromArgb(30, 52, 84));
        g.FillRectangle(lens, 44, 22, 10, 16);
        using var ring = new Pen(Color.FromArgb(200, 214, 230), 2);
        g.DrawRectangle(ring, 44, 22, 10, 16);
        // 支座
        using var mount = new SolidBrush(BlueDark);
        g.FillRectangle(mount, 22, 42, 8, 8);
        g.FillRectangle(mount, 12, 50, 28, 5);
    }

    /// <summary>
    /// 全屏放大/取消半屏：左上与右下两条对角箭头。outward=true 箭头指向屏幕两角（放大），
    /// outward=false 箭头指向画面中心（收缩）。蓝线圆头，与其他彩色扁平图标同语言。
    /// </summary>
    private static void DrawExpand(Graphics g, bool outward)
    {
        using var pen = new Pen(Blue, 5) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        // 主对角线（左上 ↔ 中心附近 / 右下 ↔ 中心附近）
        if (outward)
        {
            g.DrawLine(pen, 30, 30, 13, 13);
            g.DrawLine(pen, 34, 34, 51, 51);
            // 左上角箭头（端在 13,13）
            g.DrawLine(pen, 13, 25, 13, 13);
            g.DrawLine(pen, 13, 13, 25, 13);
            // 右下角箭头（端在 51,51）
            g.DrawLine(pen, 51, 39, 51, 51);
            g.DrawLine(pen, 51, 51, 39, 51);
        }
        else
        {
            g.DrawLine(pen, 13, 13, 30, 30);
            g.DrawLine(pen, 51, 51, 34, 34);
            // 指向中心的箭头（端在 30,30）
            g.DrawLine(pen, 18, 30, 30, 30);
            g.DrawLine(pen, 30, 30, 30, 18);
            // 指向中心的箭头（端在 34,34）
            g.DrawLine(pen, 46, 34, 34, 34);
            g.DrawLine(pen, 34, 34, 34, 46);
        }
    }

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
}