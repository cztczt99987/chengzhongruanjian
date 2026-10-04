using CZRWeighSystem.Core;
using QRCoder;
using System.Drawing.Drawing2D;

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
    private static readonly Dictionary<string, Bitmap> QrCache = [];

    /// <summary>清空图片/二维码缓存（图片文件被替换后调用）</summary>
    public static void ClearImageCache()
    {
        ImgCache.Clear();
        QrCache.Clear();
    }

    /// <summary>渲染模板全部元素。pxPerMm：打印用 g.DpiX/25.4，设计器画布用缩放系数。</summary>
    public static void Render(PrintTemplate t, Graphics g, WeighRecord r, AppConfig cfg, float pxPerMm)
    {
        var ph = PrintTemplate.BuildPlaceholders(r, cfg);
        var raw = PrintTemplate.BuildRawValues(r, cfg);
        foreach (var e in t.Elements)
        {
            switch (e)
            {
                case TextElement te:
                    DrawText(te, g, ph, raw, pxPerMm);
                    break;
                case LineElement le:
                    using (var p = new Pen(ParseColor(le.Color, Color.Black), Math.Max(0.1f, le.Thick * pxPerMm)))
                        g.DrawLine(p, le.X * pxPerMm, le.Y * pxPerMm,
                            (le.X + le.W) * pxPerMm, (le.Y + le.H) * pxPerMm);
                    break;
                case RectElement re:
                    DrawRect(re, g, pxPerMm);
                    break;
                case ImageElement ie:
                    DrawImage(ie, g, pxPerMm);
                    break;
                case TableElement tbe:
                    DrawTable(tbe, g, ph, raw, pxPerMm);
                    break;
                case SignatureElement se:
                    DrawSignature(se, g, pxPerMm);
                    break;
                case QRCodeElement qe:
                    DrawQRCode(qe, g, ph, pxPerMm);
                    break;
                case BarcodeElement be:
                    DrawBarcode(be, g, ph, pxPerMm);
                    break;
            }
        }
    }

    /// <summary>解析颜色字符串（#RRGGBB 或颜色名）；非法/为空时返回 fallback。</summary>
    internal static Color ParseColor(string? s, Color fallback)
    {
        if (string.IsNullOrWhiteSpace(s)) return fallback;
        try
        {
            s = s.Trim();
            if (s.StartsWith('#'))
                return ColorTranslator.FromHtml(s);
            var c = Color.FromName(s);
            // FromName 对未知名称也返回对象（IsKnownColor=false），需排除
            return c.IsKnownColor ? c : fallback;
        }
        catch
        {
            return fallback;
        }
    }

    /// <summary>绘制文本：替换占位符，按元素宽度内对齐（字号磅→像素换算：1磅=0.3528毫米）。</summary>
    private static void DrawText(TextElement te, Graphics g,
        Dictionary<string, string> ph, Dictionary<string, object?> raw, float pxPerMm)
    {
        string s = PrintTemplate.Expand(te.Text, ph, raw);
        using var f = new Font("Microsoft YaHei UI",
            te.FontPt * 0.3528f * pxPerMm,
            te.Bold ? FontStyle.Bold : FontStyle.Regular,
            GraphicsUnit.Pixel);
        using var brush = new SolidBrush(ParseColor(te.ForeColor, Color.Black));
        float x = te.X * pxPerMm;
        float w = te.W * pxPerMm;

        // 自动换行（Wrap=true）：按元素宽度折行，行数受元素高度限制
        if (te.Wrap && te.H > 0)
        {
            float lineH = Math.Max(1f, f.GetHeight(g));
            float maxLines = Math.Max(1, (int)(te.H * pxPerMm / lineH));
            using var sf = new StringFormat(StringFormat.GenericTypographic)
            {
                FormatFlags = StringFormatFlags.LineLimit,
                Alignment = te.Align == TextAlign.Center ? StringAlignment.Center
                    : te.Align == TextAlign.Right ? StringAlignment.Far : StringAlignment.Near,
            };
            g.DrawString(s, f, brush, new RectangleF(x, te.Y * pxPerMm, w, lineH * maxLines), sf);
            return;
        }

        // 单行（默认）：左/中/右对齐，Y 起点绘制
        if (te.Align != TextAlign.Left)
        {
            var size = g.MeasureString(s, f);
            x = te.Align == TextAlign.Center ? x + (w - size.Width) / 2 : x + w - size.Width;
        }
        g.DrawString(s, f, brush, x, te.Y * pxPerMm);
    }

    /// <summary>绘制矩形：可先填充（FillColor 非空）再画边框。</summary>
    private static void DrawRect(RectElement re, Graphics g, float pxPerMm)
    {
        var rect = new RectangleF(re.X * pxPerMm, re.Y * pxPerMm, re.W * pxPerMm, re.H * pxPerMm);
        if (!string.IsNullOrWhiteSpace(re.FillColor))
            using (var b = new SolidBrush(ParseColor(re.FillColor, Color.LightGray)))
                g.FillRectangle(b, rect);
        using var p = new Pen(ParseColor(re.Color, Color.Black), Math.Max(0.1f, re.Thick * pxPerMm));
        g.DrawRectangle(p, rect.X, rect.Y, rect.Width, rect.Height);
    }

    /// <summary>绘制表格：表头行 + 数据行（各列按列宽比例分摊总宽）。</summary>
    private static void DrawTable(TableElement tbe, Graphics g,
        Dictionary<string, string> ph, Dictionary<string, object?> raw, float pxPerMm)
    {
        if (tbe.Columns.Count == 0) return;
        float totalW = tbe.Columns.Sum(c => Math.Max(1, c.Width));
        float scale = tbe.W * pxPerMm / totalW;
        float rowH = tbe.RowH * pxPerMm;
        float x = tbe.X * pxPerMm, y = tbe.Y * pxPerMm;

        using var pen = new Pen(Color.Black, 1);
        using var headerFont = new Font("Microsoft YaHei UI", 10 * 0.3528f * pxPerMm, FontStyle.Bold, GraphicsUnit.Pixel);
        using var dataFont = new Font("Microsoft YaHei UI", 10 * 0.3528f * pxPerMm, FontStyle.Regular, GraphicsUnit.Pixel);

        // 外框与表头分隔线、数据行底线
        g.DrawRectangle(pen, x, y, tbe.W * pxPerMm, rowH * 2);
        g.DrawLine(pen, x, y + rowH, x + tbe.W * pxPerMm, y + rowH);

        float cx = x;
        foreach (var col in tbe.Columns)
        {
            float cw = Math.Max(1, col.Width) * scale;
            // 列分隔线（首列左侧已有外框）
            if (cx > x) g.DrawLine(pen, cx, y, cx, y + rowH * 2);
            DrawCellText(col.Header, headerFont, cx, y, cw, rowH, col.Align, g);
            DrawCellText(PrintTemplate.Expand(col.Text, ph, raw), dataFont,
                cx, y + rowH, cw, rowH, col.Align, g);
            cx += cw;
        }
    }

    /// <summary>表格单元格文本：按对齐方式绘制在单元格中上部。</summary>
    private static void DrawCellText(string s, Font f, float x, float y, float w, float h,
        TextAlign align, Graphics g)
    {
        var size = g.MeasureString(s, f);
        float tx = align switch
        {
            TextAlign.Center => x + (w - size.Width) / 2,
            TextAlign.Right => x + w - size.Width - 2,
            _ => x + 2,
        };
        g.DrawString(s, f, Brushes.Black, tx, y + (h - size.Height) / 2);
    }

    /// <summary>绘制签名区：各标签均分宽度，"标签：______"。</summary>
    private static void DrawSignature(SignatureElement se, Graphics g, float pxPerMm)
    {
        if (se.Labels.Count == 0) return;
        float slotW = se.W / se.Labels.Count * pxPerMm;
        float y = se.Y * pxPerMm;
        using var f = new Font("Microsoft YaHei UI", se.FontPt * 0.3528f * pxPerMm, FontStyle.Regular, GraphicsUnit.Pixel);
        using var pen = new Pen(Color.Black, 1);
        float x = se.X * pxPerMm;
        foreach (var label in se.Labels)
        {
            string text = $"{label}：";
            g.DrawString(text, f, Brushes.Black, x, y);
            var size = g.MeasureString(text, f);
            // 下划线：文字之后到本槽位末尾
            float lineY = y + size.Height * 0.92f;
            g.DrawLine(pen, x + size.Width, lineY, x + slotW - 4, lineY);
            x += slotW;
        }
    }

    /// <summary>绘制二维码（按展开后内容缓存位图；内容为空时画灰框占位）。</summary>
    private static void DrawQRCode(QRCodeElement qe, Graphics g,
        Dictionary<string, string> ph, float pxPerMm)
    {
        string content = PrintTemplate.Expand(qe.Content, ph);
        var rect = new RectangleF(qe.X * pxPerMm, qe.Y * pxPerMm, qe.W * pxPerMm, qe.H * pxPerMm);
        if (string.IsNullOrWhiteSpace(content) || content.StartsWith('{'))
        {
            DrawPlaceholder(g, rect, "二维码内容为空");
            return;
        }
        try
        {
            if (!QrCache.TryGetValue(content, out var bmp) || bmp is null)
            {
                using var gen = new QRCodeGenerator();
                using var data = gen.CreateQrCode(content, QRCodeGenerator.ECCLevel.M);
                using var qr = new QRCode(data);
                bmp = qr.GetGraphic(4);          // 每模块 4 像素，含静区
                QrCache[content] = bmp;
            }
            var mode = g.InterpolationMode;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;   // 保持码点锐利
            g.DrawImage(bmp, rect);
            g.InterpolationMode = mode;
        }
        catch
        {
            DrawPlaceholder(g, rect, "二维码生成失败");
        }
    }

    /// <summary>绘制 Code39 条码（内容非法时画灰框占位，不抛异常）。</summary>
    private static void DrawBarcode(BarcodeElement be, Graphics g,
        Dictionary<string, string> ph, float pxPerMm)
    {
        string content = PrintTemplate.Expand(be.Content, ph);
        float x = be.X * pxPerMm, y = be.Y * pxPerMm;
        float w = be.W * pxPerMm, h = be.H * pxPerMm;

        if (!Code39.IsValid(content))
        {
            DrawPlaceholder(g, new RectangleF(x, y, w, h), $"条码字符非法（Code39）");
            return;
        }

        // 文本占条码下部 25%，条占 75%；ShowText=false 时条占满
        float barH = be.ShowText ? h * 0.75f : h;
        float unitPx = w / Code39.TotalUnits(content);
        using var brush = new SolidBrush(Color.Black);
        float cx = x;
        foreach (var (bar, units) in Code39.Encode(content))
        {
            float seg = units * unitPx;
            if (bar) g.FillRectangle(brush, cx, y, seg, barH);
            cx += seg;
        }
        if (be.ShowText)
        {
            using var f = new Font("Microsoft YaHei UI", Math.Max(5f, h * 0.22f), FontStyle.Regular, GraphicsUnit.Pixel);
            var size = g.MeasureString(content, f);
            g.DrawString(content, f, Brushes.Black, x + (w - size.Width) / 2, y + barH);
        }
    }

    /// <summary>画灰色占位框 + 提示文字（图片缺失/码内容异常等场景）。</summary>
    private static void DrawPlaceholder(Graphics g, RectangleF rect, string text)
    {
        using var p = new Pen(Color.Gray, 1);
        g.DrawRectangle(p, rect.X, rect.Y, rect.Width, rect.Height);
        g.DrawString(text, SystemFonts.DefaultFont, Brushes.Gray, rect.X + 2, rect.Y + 2);
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
            DrawPlaceholder(g, rect, "图片缺失");
        }
    }
}
