using CZRWeighSystem.Core;
using System.Drawing.Printing;

namespace CZRWeighSystem.Report;

/// <summary>
/// 磅单打印（对应 PRD 5.5 磅单打印 / F5-01~F5-04）。
/// GDI+ PrintDocument 绘制，支持打印预览与补打；
/// 磅单字段布局与纸张后续按 F5-02 做成模板可配置。
/// </summary>
public static class PoundTicketPrinter
{
    /// <summary>
    /// 弹出系统打印对话框打印磅单（返回 false=用户取消或失败）。
    /// </summary>
    public static bool Print(WeighRecord r)
    {
        try
        {
            using var doc = BuildDocument(r);
            using var dialog = new PrintDialog
            {
                Document = doc,
                UseEXDialog = true,
            };
            if (dialog.ShowDialog() != DialogResult.OK) return false;
            doc.Print();
            Log.Info($"磅单已打印：{r.SerialNo}（车号 {r.VehicleNo}）");
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("磅单打印失败", ex);
            MessageBox.Show("打印失败：" + ex.Message, "错误",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
    }

    /// <summary>
    /// 打印预览（不占用纸张，便于调试磅单版式）。
    /// </summary>
    public static void PrintPreview(WeighRecord r)
    {
        using var doc = BuildDocument(r);
        using var preview = new PrintPreviewDialog
        {
            Document = doc,
            Width = 800,
            Height = 600,
            StartPosition = FormStartPosition.CenterParent,
        };
        preview.ShowDialog();
    }

    /// <summary>
    /// 构建打印文档：A4 纵向，边距内绘制磅单内容。
    /// </summary>
    private static PrintDocument BuildDocument(WeighRecord r)
    {
        var doc = new PrintDocument
        {
            DocumentName = $"磅单_{r.SerialNo}",
        };
        doc.DefaultPageSettings.PaperSize = new PaperSize("A4", 827, 1169);
        doc.PrintPage += (_, e) => DrawTicket(e.Graphics, e.MarginBounds, r);
        return doc;
    }

    /// <summary>
    /// 绘制磅单内容（对应 PRD F5-03 磅单字段）。
    /// </summary>
    private static void DrawTicket(Graphics g, Rectangle bounds, WeighRecord r)
    {
        var cfg = AppConfig.Current;
        float x = bounds.X + 40, w = bounds.Width - 80;
        float y = bounds.Y + 40;

        using var titleFont = new Font("Microsoft YaHei UI", 22F, FontStyle.Bold);
        using var font = new Font("Microsoft YaHei UI", 12F);
        using var boldFont = new Font("Microsoft YaHei UI", 12F, FontStyle.Bold);
        using var smallFont = new Font("Microsoft YaHei UI", 9F);
        using var black = Brushes.Black;
        using var pen = new Pen(Color.Black, 1.2f);

        // 标题（公司名 + 单据名）
        g.DrawString("磅  单", titleFont, black, x, y);
        y += 52;
        g.DrawString(cfg.CompanyName, font, black, x, y);
        g.DrawString($"No. {r.SerialNo}", font, black, x + w - 220, y);
        y += 34;

        // 基础信息两列
        y = DrawField(g, font, x, y, "车号", r.VehicleNo, w);
        y = DrawField(g, font, x, y, "货物", r.Goods ?? "-", w);
        y = DrawField(g, font, x, y, "收发货单位", r.Company ?? "-", w);
        y = DrawField(g, font, x, y, "规格", r.Spec ?? "-", w);
        y = DrawField(g, font, x, y, "批次", r.BatchNo ?? "-", w);
        y += 8;

        // 重量区（带边框突出显示）
        float boxH = 110;
        g.DrawRectangle(pen, x, y, w, boxH);
        float colW = w / 3;
        string unit = cfg.Unit;
        g.DrawString($"毛重：{Fmt(r.GrossKg, cfg)}", boldFont, black, x + 16, y + 18);
        g.DrawString($"皮重：{Fmt(r.TareKg, cfg)}", boldFont, black, x + colW + 16, y + 18);
        g.DrawString($"净重：{Fmt(r.NetKg, cfg)}", boldFont, black, x + colW * 2 + 16, y + 18);
        g.DrawString($"扣重：{Fmt(0, cfg)}", font, black, x + 16, y + 62);
        g.DrawString($"实收：{Fmt(r.NetKg, cfg)}", font, black, x + colW + 16, y + 62);
        g.DrawString($"单位：{unit}", font, black, x + colW * 2 + 16, y + 62);
        y += boxH + 16;

        // 时间与人员
        y = DrawField(g, font, x, y, "一次磅时间", r.FirstTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "-", w);
        y = DrawField(g, font, x, y, "二次磅时间", r.SecondTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "-", w);
        y = DrawField(g, font, x, y, "司磅员", r.Operator ?? "-", w);
        if (r.IsManual)
            y = DrawField(g, font, x, y, "备注", "手工补录", w);
        y += 24;

        // 签字栏
        g.DrawString("司磅员签字：____________", font, black, x, y);
        g.DrawString("客户签字：____________", font, black, x + w / 2, y);
        y += 48;

        // 页脚
        g.DrawString("本单据由 CZR 智能称重管理系统生成", smallFont,
            Brushes.Gray, x, y);
    }

    /// <summary>绘制"标签：值"一行，返回下一行 y。</summary>
    private static float DrawField(Graphics g, Font font, float x, float y,
        string label, string value, float totalW)
    {
        g.DrawString($"{label}：", font, Brushes.Black, x, y);
        g.DrawString(value, font, Brushes.Black, x + 130, y);
        return y + 28;
    }

    /// <summary>重量显示文本（千克 → 配置单位）。</summary>
    private static string Fmt(double? kg, AppConfig cfg)
    {
        if (!kg.HasValue) return "-";
        double v = cfg.Unit == "t" ? kg.Value / 1000.0 : kg.Value;
        return v.ToString("0.###");
    }
}
