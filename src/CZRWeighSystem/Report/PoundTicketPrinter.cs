using CZRWeighSystem.Core;
using System.Drawing.Printing;

namespace CZRWeighSystem.Report;

/// <summary>
/// 磅单打印（对应 PRD 5.5 磅单打印 / F5-01~F5-04）。
/// GDI+ PrintDocument 绘制，支持打印预览与补打；
/// 纸张、字段显隐、联数由打印设置（AppConfig）驱动（对应 F9-04 / F5-02 / F5-03）。
/// </summary>
public static class PoundTicketPrinter
{
    // ===== 可选纸张目录（尺寸单位：0.01 英寸，对应 F5-02 纸张可选）=====
    // 命名按行业惯例：二等分 = 241×140mm（11 英寸均分两份），三等分 = 241×93mm（均分三份）
    public static readonly (string Key, string Name, int W, int H)[] Papers =
    [
        ("A4", "A4（210×297mm）", 827, 1169),
        ("241x140", "针打 241×140mm（二等分）", 949, 551),
        ("241x93", "针打 241×93mm（三等分）", 949, 366),
        ("120x140", "竖联带孔纸 120×140mm", 472, 551),
    ];

    /// <summary>纸张毫米尺寸（Papers 存储为 0.01 英寸，模板元素坐标为毫米；未知 key 回退第一项）。</summary>
    public static (float W, float H) PaperSizeMm(string key)
    {
        int idx = Array.FindIndex(Papers, p => p.Key == key);
        var paper = idx >= 0 ? Papers[idx] : Papers[0];
        return (paper.W / 100f * 25.4f, paper.H / 100f * 25.4f);
    }

    /// <summary>
    /// 解析打印纸张：优先使用打印机驱动中已安装的同尺寸纸张（±0.25mm 容差）。
    /// 针打连续纸的页长由驱动纸张决定，程序自定义纸张（PaperKind.Custom）常被驱动
    /// 忽略而按默认页长（近 A4）走纸，导致一张磅单占用多联连续纸；匹配驱动纸张可确保页长生效。
    /// 无匹配时回退程序自定义尺寸。
    /// </summary>
    private static PaperSize ResolvePaperSize(PrintDocument doc, (string Key, string Name, int W, int H) paper)
    {
        foreach (PaperSize ps in doc.PrinterSettings.PaperSizes)
        {
            if (Math.Abs(ps.Width - paper.W) <= 25 && Math.Abs(ps.Height - paper.H) <= 25)
                return ps;
        }
        return new PaperSize(paper.Name, paper.W, paper.H);
    }

    // ===== 磅单字段目录（key 用于打印设置显隐配置，对应 F5-02）=====

    /// <summary>基础信息行（值为 null 时显示"-"）</summary>
    private static readonly FieldDef[] BasicRows =
    [
        new("车号", "车号", (r, _) => r.VehicleNo),
        new("货物", "货物", (r, _) => r.Goods),
        new("发货单位", "发货单位", (r, _) => r.SenderCompany),
        new("收货单位", "收货单位", (r, _) => r.ReceiverCompany),
        new("规格", "规格", (r, _) => r.Spec),
        new("批次", "批次", (r, _) => r.BatchNo),
    ];

    /// <summary>时间与人员行（值为 null 时整行不打印，如未补录的备注）</summary>
    private static readonly FieldDef[] DetailRows =
    [
        new("一次磅时间", "一次磅时间", (r, _) => r.FirstTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "-"),
        new("二次磅时间", "二次磅时间", (r, _) => r.SecondTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "-"),
        new("司磅员", "司磅员", (r, _) => r.Operator),
        new("备注", "备注", (r, _) => r.IsManual ? "手工补录"
            : string.IsNullOrEmpty(r.Remark) ? null : r.Remark),
    ];

    /// <summary>重量区格子（毛皮净一行，扣重/实收/单位一行）</summary>
    private static readonly (string Key, string Label)[] WeightCells =
    [
        ("毛重", "毛重"), ("皮重", "皮重"), ("净重", "净重"),
        ("扣重", "扣重"), ("实收", "实收"), ("单位", "单位"),
    ];

    /// <summary>全部可配置字段（key, 显示名），打印设置界面用</summary>
    public static readonly (string Key, string Label)[] FieldCatalog =
    [
        ("公司名称", "公司名称"), ("流水号", "流水号"),
        ..BasicRows.Select(e => (e.Key, e.Label)),
        ..WeightCells,
        ..DetailRows.Select(e => (e.Key, e.Label)),
        ("签字栏", "签字栏"), ("页脚", "页脚"),
    ];

    /// <summary>字段定义：key / 显示名 / 取值（null = 不打印该行）</summary>
    private sealed record FieldDef(string Key, string Label, Func<WeighRecord, AppConfig, string?> Get);

    /// <summary>
    /// 弹出系统打印对话框打印磅单（返回 false=用户取消、超限或失败）。
    /// 综合设置开启"打印次数"累计时：达到上限拒绝打印，打印成功后次数 +1。
    /// </summary>
    public static bool Print(WeighRecord r)
    {
        var cfg = AppConfig.Current;
        if (ExceedsPrintLimit(r, cfg))
        {
            MessageBox.Show(
                $"该磅单已打印 {r.PrintCount} 次，达到允许上限 {cfg.MaxPrintCount} 次，不能再打印。",
                "打印次数超限", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
        try
        {
            using var doc = BuildRecordDocument(r, copies: 0);
            using var dialog = new PrintDialog
            {
                Document = doc,
                UseEXDialog = true,
            };
            if (dialog.ShowDialog() != DialogResult.OK) return false;
            doc.Print();
            CountPrint(r);
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
        using var doc = BuildRecordDocument(r, copies: 0);
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
    /// 按给定画布直接渲染磅单内容（供验证工具做渲染自检）。
    /// </summary>
    public static void RenderTo(Graphics g, Rectangle bounds, WeighRecord r)
        => DrawTicket(g, bounds, r);

    /// <summary>
    /// 加载当前启用的打印模板；未启用或文件缺失返回 null（走内置版式）。
    /// </summary>
    public static PrintTemplate? LoadActiveTemplate()
    {
        var file = AppConfig.Current.PrintTemplateFile;
        if (string.IsNullOrWhiteSpace(file)) return null;
        try
        {
            return PrintTemplate.Load(file);
        }
        catch (Exception ex)
        {
            Log.Error($"加载打印模板失败，改用内置版式：{file}", ex);
            return null;
        }
    }

    /// <summary>
    /// 为指定模板构建打印文档（模板设计器预览用，不读启用配置）。
    /// </summary>
    public static PrintDocument BuildTemplateDocument(PrintTemplate t, WeighRecord r)
    {
        var cfg = AppConfig.Current;
        int idx = Array.FindIndex(Papers, p => p.Key == t.PaperKey);
        var paper = idx >= 0 ? Papers[idx] : Papers[0];

        var doc = new PrintDocument { DocumentName = "磅单模板预览" };
        doc.DefaultPageSettings.PaperSize = ResolvePaperSize(doc, paper);
        doc.DefaultPageSettings.Margins = new Margins(40, 40, 40, 40);
        doc.PrintPage += (_, e) =>
        {
            e.Graphics!.TranslateTransform(e.MarginBounds.X, e.MarginBounds.Y);
            TemplateTicketRenderer.Render(t, e.Graphics, r, cfg, e.Graphics.DpiX / 25.4f);
        };
        return doc;
    }

    /// <summary>
    /// 构建单张磅单打印文档：启用模板时按模板纸张与版式渲染，否则走内置版式；
    /// 联数逐页输出（对应 F5-03 一式 N 联）。
    /// copies：联数；传 0 表示取全局打印设置的联数（批量导出 PDF 等场景可传 1）。
    /// </summary>
    public static PrintDocument BuildRecordDocument(WeighRecord r, int copies)
    {
        var cfg = AppConfig.Current;
        var tpl = LoadActiveTemplate();
        int idx = Array.FindIndex(Papers, p => p.Key == (tpl?.PaperKey ?? cfg.PrintPaper));
        var paper = idx >= 0 ? Papers[idx] : Papers[0];
        int pages = copies > 0 ? copies : Math.Max(1, cfg.PrintCopies);

        var doc = new PrintDocument { DocumentName = $"磅单_{r.SerialNo}" };
        doc.DefaultPageSettings.PaperSize = ResolvePaperSize(doc, paper);
        doc.DefaultPageSettings.Margins = new Margins(40, 40, 40, 40);

        int printed = 0;
        doc.PrintPage += (_, e) =>
        {
            RenderRecord(tpl, e.Graphics!, e.MarginBounds, r);
            printed++;
            e.HasMorePages = printed < pages;
        };
        return doc;
    }

    /// <summary>
    /// 构建批量打印文档：多张磅单合并为一个打印任务，每张按全局联数逐页输出。
    /// 所有记录共用启用模板（或内置版式）与解析出的纸张。
    /// </summary>
    public static PrintDocument BuildBatchDocument(IReadOnlyList<WeighRecord> records)
    {
        var cfg = AppConfig.Current;
        var tpl = LoadActiveTemplate();
        int idx = Array.FindIndex(Papers, p => p.Key == (tpl?.PaperKey ?? cfg.PrintPaper));
        var paper = idx >= 0 ? Papers[idx] : Papers[0];
        int copies = Math.Max(1, cfg.PrintCopies);

        var doc = new PrintDocument { DocumentName = $"磅单批量打印_{records.Count}张" };
        doc.DefaultPageSettings.PaperSize = ResolvePaperSize(doc, paper);
        doc.DefaultPageSettings.Margins = new Margins(40, 40, 40, 40);

        int recordIdx = 0, pageInRecord = 0;
        doc.PrintPage += (_, e) =>
        {
            RenderRecord(tpl, e.Graphics!, e.MarginBounds, records[recordIdx]);
            pageInRecord++;
            if (pageInRecord >= copies)
            {
                recordIdx++;
                pageInRecord = 0;
            }
            e.HasMorePages = recordIdx < records.Count;
        };
        return doc;
    }

    /// <summary>
    /// 批量打印：一次打印对话框，多张磅单合并为单个打印任务（返回 false=用户取消、全部超限或失败）。
    /// 综合设置开启"打印次数"累计时：达到上限的磅单自动跳过并提示。
    /// </summary>
    public static bool PrintBatch(IReadOnlyList<WeighRecord> records)
    {
        if (records.Count == 0) return false;
        var cfg = AppConfig.Current;
        var list = records.ToList();
        int skipped = 0;
        if (cfg.PrintCountEnabled)
        {
            skipped = list.Count(r => ExceedsPrintLimit(r, cfg));
            list = list.Where(r => !ExceedsPrintLimit(r, cfg)).ToList();
            if (list.Count == 0)
            {
                MessageBox.Show(
                    $"所选磅单均已打印 {cfg.MaxPrintCount} 次（上限），无法打印。",
                    "打印次数超限", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
        }
        try
        {
            using var doc = BuildBatchDocument(list);
            using var dialog = new PrintDialog { Document = doc, UseEXDialog = true };
            if (dialog.ShowDialog() != DialogResult.OK) return false;
            doc.Print();
            foreach (var r in list) CountPrint(r);
            if (skipped > 0)
                MessageBox.Show($"已跳过 {skipped} 张达到打印次数上限的磅单。",
                    "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            Log.Info($"批量打印磅单：{list.Count} 张");
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("批量打印磅单失败", ex);
            MessageBox.Show("批量打印失败：" + ex.Message, "错误",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
    }

    /// <summary>打印成功后累计打印次数（综合设置关闭"打印次数"累计时不计数）。</summary>
    private static void CountPrint(WeighRecord r)
    {
        var cfg = AppConfig.Current;
        if (!cfg.PrintCountEnabled || r.Id <= 0) return;
        r.PrintCount = new Database.WeighRecordRepository().IncrementPrintCount(r.Id);
    }

    /// <summary>
    /// 打印次数是否已达上限（综合设置开启"打印次数"累计时：已打印次数 ≥ 允许次数）。
    /// </summary>
    internal static bool ExceedsPrintLimit(WeighRecord r, AppConfig cfg)
        => cfg.PrintCountEnabled && r.PrintCount >= cfg.MaxPrintCount;

    /// <summary>按模板（或内置版式）渲染单张磅单页面。</summary>
    private static void RenderRecord(PrintTemplate? tpl, Graphics g, Rectangle bounds, WeighRecord r)
    {
        if (tpl != null)
        {
            // 模板模式：元素坐标为毫米，平移到边距起点后按设备分辨率渲染
            g.TranslateTransform(bounds.X, bounds.Y);
            TemplateTicketRenderer.Render(tpl, g, r, AppConfig.Current, g.DpiX / 25.4f);
        }
        else
        {
            DrawTicket(g, bounds, r);
        }
    }

    // ==================== 绘制 ====================

    /// <summary>
    /// 绘制磅单内容。
    /// 宽扁纸张（针打连续单，高&lt;宽）自动切换两列紧凑布局；
    /// 内容超出页面高度时整体等比缩小以完整放入一页。
    /// </summary>
    private static void DrawTicket(Graphics g, Rectangle bounds, WeighRecord r)
    {
        var cfg = AppConfig.Current;
        bool Vis(string key) => !cfg.PrintHiddenFields.Contains(key);
        bool compact = bounds.Height < bounds.Width;

        float scale = Math.Min(1f, bounds.Height / EstimateHeight(r, compact));
        if (scale < 1f) g.ScaleTransform(scale, scale);

        // 缩放后坐标系同步缩小，起点/宽度按 1/scale 反算以铺满可用区域
        float x = bounds.X / scale + 40;
        float y = bounds.Y / scale + 40;
        float w = bounds.Width / scale - 80;

        using var titleFont = new Font("Microsoft YaHei UI", compact ? 16F : 22F, FontStyle.Bold);
        using var font = new Font("Microsoft YaHei UI", 12F);
        using var boldFont = new Font("Microsoft YaHei UI", 12F, FontStyle.Bold);
        using var smallFont = new Font("Microsoft YaHei UI", 9F);
        using var pen = new Pen(Color.Black, 1.2f);

        // 标题 + 公司名/流水号行（标题取综合设置的磅单抬头，未填时回退"磅 单"）
        string header = string.IsNullOrWhiteSpace(cfg.SlipHeader) ? "磅  单" : cfg.SlipHeader;
        g.DrawString(header, titleFont, Brushes.Black, x, y);
        y += compact ? 34 : 52;
        if (Vis("公司名称") || Vis("流水号"))
        {
            if (Vis("公司名称")) g.DrawString(cfg.CompanyName, font, Brushes.Black, x, y);
            if (Vis("流水号")) g.DrawString($"No. {r.SerialNo}", font, Brushes.Black, x + w - 220, y);
            y += compact ? 24 : 34;
        }

        // 基础信息
        y = DrawRows(g, font, x, y, w, CollectRows(BasicRows, r, cfg, Vis), compact);

        // 重量区（边框突出；两行各自整体显隐，格内按配置跳过）
        bool row1 = Vis("毛重") || Vis("皮重") || Vis("净重");
        bool row2 = Vis("扣重") || Vis("实收") || Vis("单位");
        if (row1 || row2)
        {
            float boxH = compact ? 12 + (row1 ? 26 : 0) + (row2 ? 26 : 0) + 6 : 110;
            g.DrawRectangle(pen, x, y, w, boxH);
            float colW = w / 3;
            if (compact)
            {
                float ry = y + 6;
                if (row1)
                {
                    if (Vis("毛重")) g.DrawString($"毛重：{Fmt(r.GrossKg, cfg)}", boldFont, Brushes.Black, x + 12, ry);
                    if (Vis("皮重")) g.DrawString($"皮重：{Fmt(r.TareKg, cfg)}", boldFont, Brushes.Black, x + colW + 12, ry);
                    if (Vis("净重")) g.DrawString($"净重：{Fmt(r.NetKg, cfg)}", boldFont, Brushes.Black, x + colW * 2 + 12, ry);
                    ry += 26;
                }
                if (row2)
                {
                    if (Vis("扣重")) g.DrawString($"扣重：{Fmt(r.DeductKg, cfg)}", font, Brushes.Black, x + 12, ry);
                    if (Vis("实收")) g.DrawString($"实收：{Fmt(r.ReceivedKg, cfg)}", font, Brushes.Black, x + colW + 12, ry);
                    if (Vis("单位")) g.DrawString($"单位：{cfg.Unit}", font, Brushes.Black, x + colW * 2 + 12, ry);
                }
            }
            else
            {
                if (row1)
                {
                    if (Vis("毛重")) g.DrawString($"毛重：{Fmt(r.GrossKg, cfg)}", boldFont, Brushes.Black, x + 16, y + 18);
                    if (Vis("皮重")) g.DrawString($"皮重：{Fmt(r.TareKg, cfg)}", boldFont, Brushes.Black, x + colW + 16, y + 18);
                    if (Vis("净重")) g.DrawString($"净重：{Fmt(r.NetKg, cfg)}", boldFont, Brushes.Black, x + colW * 2 + 16, y + 18);
                }
                if (row2)
                {
                    if (Vis("扣重")) g.DrawString($"扣重：{Fmt(r.DeductKg, cfg)}", font, Brushes.Black, x + 16, y + 62);
                    if (Vis("实收")) g.DrawString($"实收：{Fmt(r.ReceivedKg, cfg)}", font, Brushes.Black, x + colW + 16, y + 62);
                    if (Vis("单位")) g.DrawString($"单位：{cfg.Unit}", font, Brushes.Black, x + colW * 2 + 16, y + 62);
                }
            }
            y += boxH + (compact ? 10 : 16);
        }

        // 时间与人员
        y = DrawRows(g, font, x, y, w, CollectRows(DetailRows, r, cfg, Vis), compact);

        // 签字栏
        if (Vis("签字栏"))
        {
            g.DrawString("司磅员签字：____________", font, Brushes.Black, x, y);
            g.DrawString("客户签字：____________", font, Brushes.Black, x + w / 2, y);
            y += compact ? 40 : 72;
        }

        // 页脚（取综合设置的磅单底部文字，未填时回退系统默认文案）
        if (Vis("页脚"))
        {
            string footer = string.IsNullOrWhiteSpace(cfg.SlipFooter)
                ? "本单据由 CZR 智能称重管理系统生成" : cfg.SlipFooter;
            g.DrawString(footer, smallFont, Brushes.Gray, x, y);
        }
    }

    /// <summary>
    /// 预估内容总高度（布局常量与 DrawTicket 保持一致），用于超高时等比缩放。
    /// </summary>
    private static float EstimateHeight(WeighRecord r, bool compact)
    {
        var cfg = AppConfig.Current;
        bool Vis(string key) => !cfg.PrintHiddenFields.Contains(key);

        float h = 40 + (compact ? 34 : 52);                        // 顶部留白 + 标题
        if (Vis("公司名称") || Vis("流水号")) h += compact ? 24 : 34;

        h += Lines(BasicRows.Count(e => Vis(e.Key)), compact) * 28;

        bool r1 = Vis("毛重") || Vis("皮重") || Vis("净重");
        bool r2 = Vis("扣重") || Vis("实收") || Vis("单位");
        if (r1 || r2)
            h += compact ? 12 + (r1 ? 26 : 0) + (r2 ? 26 : 0) + 6 + 10 : 110 + 16;

        h += Lines(DetailRows.Count(e => Vis(e.Key) && e.Get(r, cfg) != null), compact) * 28;

        if (Vis("签字栏")) h += compact ? 40 : 72;
        if (Vis("页脚")) h += 20;
        return h;
    }

    /// <summary>按显隐配置收集要打印的行（值为 null 的行不打印）。</summary>
    private static List<(string Label, string Value)> CollectRows(
        FieldDef[] defs, WeighRecord r, AppConfig cfg, Func<string, bool> vis)
        => defs.Where(e => vis(e.Key))
               .Select(e => (e.Label, e.Get(r, cfg)))
               .Where(t => t.Item2 != null)
               .Select(t => (t.Label, Value: t.Item2!))
               .ToList();

    /// <summary>绘制"标签：值"行（compact 时两列），返回下一行 y。</summary>
    private static float DrawRows(Graphics g, Font font, float x, float y, float w,
        List<(string Label, string Value)> rows, bool compact)
    {
        float colW = compact ? w / 2 : w;
        float valueX = compact ? 105f : 130f;
        for (int i = 0; i < rows.Count; i++)
        {
            // 两列布局时奇数序号排在右列、与前一元素同行
            float cx = x + (compact && i % 2 == 1 ? colW : 0);
            float cy = y + (i / (compact ? 2 : 1)) * 28;
            g.DrawString($"{rows[i].Label}：", font, Brushes.Black, cx, cy);
            g.DrawString(rows[i].Value, font, Brushes.Black, cx + valueX, cy);
        }
        return y + Lines(rows.Count, compact) * 28;
    }

    /// <summary>行数折算：两列布局时行数减半（向上取整）。</summary>
    private static int Lines(int count, bool compact) => compact ? (count + 1) / 2 : count;

    /// <summary>重量显示文本（千克 → 配置单位）。</summary>
    private static string Fmt(double? kg, AppConfig cfg)
    {
        if (!kg.HasValue) return "-";
        double v = cfg.Unit == "t" ? kg.Value / 1000.0 : kg.Value;
        return v.ToString("0.###");
    }
}
