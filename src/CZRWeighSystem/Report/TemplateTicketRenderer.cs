using CZRWeighSystem.Core;

namespace CZRWeighSystem.Report;

/// <summary>
/// 磅单模板渲染器：把模板元素绘制到 Graphics。
/// 打印（PrintDocument）与模板设计器画布共用同一渲染逻辑。
/// 坐标单位毫米，按 pxPerMm（毫米→像素比例）换算；页面左上角为 (0,0)，
/// 调用方自行 TranslateTransform 到页内起点。
/// </summary>
public static class TemplateTicketRenderer
{
    private static readonly Dictionary<string, Image> ImgCache = [];

    /// <summary>清空图片缓存（图片文件被替换后调用）</summary>
    public static void ClearImageCache() => ImgCache.Clear();

    /// <summary>渲染模板全部元素。pxPerMm：打印用 g.DpiX/25.4，设计器画布用缩放系数。</summary>
    public static void Render(PrintTemplate t, Graphics g, WeighRecord r, AppConfig cfg, float pxPerMm)
    {
        var ph = PrintTemplate.BuildPlaceholders(r, cfg);
        foreach (var e in t.Elements)
        {
            switch (e)
            {
                case TextElement te:
                    DrawText(te, g, ph, pxPerMm);
                    break;
                case LineElement le:
                    using (var p = new Pen(Color.Black, Math.Max(0.1f, le.Thick * pxPerMm)))
                        g.DrawLine(p, le.X * pxPerMm, le.Y * pxPerMm,
                            (le.X + le.W) * pxPerMm, (le.Y + le.H) * pxPerMm);
                    break;
                case RectElement re:
                    using (var p = new Pen(Color.Black, Math.Max(0.1f, re.Thick * pxPerMm)))
                        g.DrawRectangle(p, re.X * pxPerMm, re.Y * pxPerMm,
                            re.W * pxPerMm, re.H * pxPerMm);
                    break;
                case ImageElement ie:
                    DrawImage(ie, g, pxPerMm);
                    break;
            }
        }
    }

    /// <summary>绘制文本：替换占位符，按元素宽度内对齐（字号磅→像素换算：1磅=0.3528毫米）。</summary>
    private static void DrawText(TextElement te, Graphics g,
        Dictionary<string, string> ph, float pxPerMm)
    {
        string s = PrintTemplate.Expand(te.Text, ph);
        using var f = new Font("Microsoft YaHei UI",
            te.FontPt * 0.3528f * pxPerMm,
            te.Bold ? FontStyle.Bold : FontStyle.Regular,
            GraphicsUnit.Pixel);
        float x = te.X * pxPerMm;
        float w = te.W * pxPerMm;
        if (te.Align != TextAlign.Left)
        {
            var size = g.MeasureString(s, f);
            x = te.Align == TextAlign.Center ? x + (w - size.Width) / 2 : x + w - size.Width;
        }
        g.DrawString(s, f, Brushes.Black, x, te.Y * pxPerMm);
    }

    /// <summary>绘制图片（按路径缓存；文件缺失时画灰框占位，不抛异常）。</summary>
    private static void DrawImage(ImageElement ie, Graphics g, float pxPerMm)
    {
        var rect = new RectangleF(ie.X * pxPerMm, ie.Y * pxPerMm, ie.W * pxPerMm, ie.H * pxPerMm);
        try
        {
            if (string.IsNullOrWhiteSpace(ie.Path)) throw new FileNotFoundException("未设置图片路径");
            if (!ImgCache.TryGetValue(ie.Path, out var img) || img is null)
            {
                img = Image.FromFile(PrintTemplate.PathOf(ie.Path));
                ImgCache[ie.Path] = img;
            }
            g.DrawImage(img, rect);
        }
        catch
        {
            using var p = new Pen(Color.Gray, 1);
            g.DrawRectangle(p, rect.X, rect.Y, rect.Width, rect.Height);
            g.DrawString("图片缺失", SystemFonts.DefaultFont, Brushes.Gray,
                rect.X + 2, rect.Y + 2);
        }
    }
}
