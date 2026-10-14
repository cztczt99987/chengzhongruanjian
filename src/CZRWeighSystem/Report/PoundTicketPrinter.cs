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
        // 58mm 热敏收银纸：纸宽 58mm、打印头有效宽约 48mm（384 点 / 203dpi）；
        // 页长 145mm 以容下 6 行承诺文案（窄纸折行后约 18mm）不留裁切
        ("58x145", "热敏小票 58×145mm（收银纸）", 228, 571),
    ];

    /// <summary>纸张毫米尺寸（Papers 存储为 0.01 英寸，模板元素坐标为毫米；未知 key 回退第一项）。</summary>
    public static (float W, float H) PaperSizeMm(string key)
    {
        int idx = Array.FindIndex(Papers, p => p.Key == key);
        var paper = idx >= 0 ? Papers[idx] : Papers[0];
        return (paper.W / 100f * 25.4f, paper.H / 100f * 25.4f);
    }

    /// <summary>
    /// 解析并应用打印纸张：优先使用打印机驱动中已安装的同尺寸纸张（±0.25mm 容差）。
    /// 针打连续纸的页长由驱动纸张决定，程序自定义纸张（PaperKind.Custom）常被驱动
    /// 忽略而按默认页长（近 A4）走纸，导致一张磅单占用多联连续纸；匹配驱动纸张可确保页长生效。
    /// 无匹配时回退程序自定义尺寸。
    /// </summary>
    private static void ApplyPaperSize(PrintDocument doc, (string Key, string Name, int W, int H) paper)
    {
        PaperSize Resolve()
        {
            foreach (PaperSize ps in doc.PrinterSettings.PaperSizes)
            {
                if (Math.Abs(ps.Width - paper.W) <= 25 && Math.Abs(ps.Height - paper.H) <= 25)
                    return ps;
            }
            return new PaperSize(paper.Name, paper.W, paper.H);
        }

        doc.DefaultPageSettings.PaperSize = Resolve();
        // WinForms 已知问题：只设 DefaultPageSettings 时，PrinterSettings.DefaultPageSettings
        // 仍是驱动默认页长（近 A4），预览/部分驱动会按其渲染，导致预览空白或占多联——两处必须同步
        doc.PrinterSettings.DefaultPageSettings.PaperSize = doc.DefaultPageSettings.PaperSize;
        doc.PrinterSettings.DefaultPageSettings.Margins = doc.DefaultPageSettings.Margins;
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
    /// 磅单实际输出动作：默认直接打印到系统默认打印机，不弹打印机选择框
    /// （换打印机请在 Windows 中修改默认打印机）。internal 静态可替换，测试用于拦截真实出纸。
    /// </summary>
    internal static Action<PrintDocument> OutputAction = doc => doc.Print();

    /// <summary>
    /// 直接打印磅单到系统默认打印机（点"打印"即出单，不再弹打印机选择框；返回 false=超限或失败）。
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
            OutputAction(doc);
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
    /// 打印预览（不占用纸张，便于调试磅单版式）。预览不弹纸张不符指引（仅真实打印提示）。
    /// </summary>
    /// <param name="allowPrint">
    /// true 时预览窗口底部新增「打 印」按钮，点击即按当前版式直接出单
    /// （首页记录右键「打印预览」、记录查询页「磅单预览」）；
    /// false 仅预览不出单（打印设置里的样张预览、模板库预览）。
    /// </param>
    public static void PrintPreview(WeighRecord r, bool allowPrint = false)
    {
        using var preview = BuildPreview(r, allowPrint);
        preview.ShowDialog();
    }

    /// <summary>
    /// 构建磅单打印预览窗口：allowPrint 时底部操作栏提供「打 印」（直接出单）与「关 闭」。
    /// 单独暴露（internal）便于测试在不弹窗的前提下校验控件结构与打印路径。
    /// </summary>
    internal static PrintPreviewDialog BuildPreview(WeighRecord r, bool allowPrint)
    {
        var preview = new PrintPreviewDialog
        {
            Document = BuildRecordDocument(r, copies: 0, showPaperWarning: false),
            Width = 800,
            Height = 600,
            StartPosition = FormStartPosition.CenterParent,
            Text = $"磅单预览 - {r.VehicleNo}",
        };
        if (!allowPrint) return preview;

        // 系统工具栏自带的打印图标与本按钮重复，且不经打印次数校验/日志/提示，隐藏之（名称未命中则忽略）
        if (preview.Controls.OfType<ToolStrip>().FirstOrDefault() is { } tool
            && tool.Items["printToolStripButton"] is { } builtinPrint)
            builtinPrint.Visible = false;

        var bar = new FlowLayoutPanel
        {
            Name = "previewActionBar",
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,   // 先加入者靠最右
            Height = 52,
            Padding = new Padding(0, 9, 14, 0),
        };
        var btnClose = new Button
        {
            Name = "btnPreviewClose",
            Text = "关 闭",
            Size = new Size(96, 32),
            FlatStyle = FlatStyle.Flat,
        };
        var btnPrint = new Button
        {
            Name = "btnPreviewPrint",
            Text = "打 印",
            Size = new Size(108, 32),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(0, 122, 204),
            ForeColor = Color.White,
            Margin = new Padding(8, 0, 0, 0),
        };
        btnPrint.Click += (_, _) => PrintFromPreview(r, preview);
        btnClose.Click += (_, _) => preview.Close();
        bar.Controls.Add(btnClose);
        bar.Controls.Add(btnPrint);
        preview.Controls.Add(bar);
        return preview;
    }

    /// <summary>
    /// 预览窗口「打 印」按钮动作：按当前版式直接打印到系统默认打印机
    /// （复用 <see cref="Print"/>：含打印次数上限校验、日志与"正在打印"提示）。
    /// 可否打印由 <see cref="CanPrint"/> 判定，与记录查询页「补打磅单」一致。
    /// </summary>
    internal static void PrintFromPreview(WeighRecord r, Form owner)
    {
        if (!CanPrint(r))
        {
            MessageBox.Show(owner, "已作废的记录不能打印磅单", "提示",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        using var indicator = new UI.PrintingIndicator();
        indicator.Show(owner);
        Print(r);
    }

    /// <summary>
    /// 是否允许打印磅单：除已作废记录外均可打印——已完成结算的记录，以及只称过一次
    /// （如空车去皮只称皮重、重车过磅只称毛重）的未完成记录都允许出单，
    /// 未结算字段按 "-" 打印。
    /// </summary>
    internal static bool CanPrint(WeighRecord r) => r.Status != "已作废";

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
        int idx = Array.FindIndex(Papers, p => p.Key == t.PaperKey);
        var paper = idx >= 0 ? Papers[idx] : Papers[0];

        var doc = new PrintDocument { DocumentName = "磅单模板预览" };
        // 原点锚定物理纸边（false），RenderPage 再按打印机硬边距平移到可打印区——
        // 不能用 OriginAtMargins=true+0边距：那会让原点=物理纸边且裁剪区从硬边距开始，
        // 模板顶部 Y=2mm 的标题落在不可打印区被裁（2026-10-04 反馈预览顶部裁切）
        doc.OriginAtMargins = false;
        doc.DefaultPageSettings.Margins = new Margins(0, 0, 0, 0);
        ApplyPaperSize(doc, paper);
        // 模板库/设计器的打印预览：只渲染不打印，纸张不符不弹指引（避免预览与自动化测试被模态框阻塞）
        doc.PrintPage += (_, e) => RenderPage(e, t, r, showPaperWarning: false);
        return doc;
    }

    /// <summary>
    /// 构建单张磅单打印文档：启用模板时按模板纸张与版式渲染，否则走内置版式；
    /// 联数逐页输出（对应 F5-03 一式 N 联）。
    /// copies：联数；传 0 表示取全局打印设置的联数（批量导出 PDF 等场景可传 1）。
    /// showPaperWarning：纸张与模板不符时是否弹指引（实际打印=true，打印预览=false）。
    /// </summary>
    public static PrintDocument BuildRecordDocument(WeighRecord r, int copies, bool showPaperWarning = true)
    {
        var cfg = AppConfig.Current;
        var tpl = LoadActiveTemplate();
        int idx = Array.FindIndex(Papers, p => p.Key == (tpl?.PaperKey ?? cfg.PrintPaper));
        var paper = idx >= 0 ? Papers[idx] : Papers[0];
        int pages = copies > 0 ? copies : Math.Max(1, cfg.PrintCopies);

        var doc = new PrintDocument { DocumentName = $"磅单_{r.SerialNo}" };
        doc.OriginAtMargins = false;              // 原点=物理纸边；RenderPage 平移到可打印区（防顶部裁切）
        doc.DefaultPageSettings.Margins = new Margins(0, 0, 0, 0);
        ApplyPaperSize(doc, paper);

        int printed = 0;
        doc.PrintPage += (_, e) =>
        {
            RenderPage(e, tpl, r, showPaperWarning);
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
        doc.OriginAtMargins = false;              // 原点=物理纸边；RenderPage 平移到可打印区（防顶部裁切）
        doc.DefaultPageSettings.Margins = new Margins(0, 0, 0, 0);
        ApplyPaperSize(doc, paper);

        int recordIdx = 0, pageInRecord = 0;
        doc.PrintPage += (_, e) =>
        {
            RenderPage(e, tpl, records[recordIdx]);   // 实际批量打印：保留纸张不符指引
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
    /// 批量打印：多张磅单合并为单个打印任务直接打印到系统默认打印机
    /// （不再弹打印机选择框；返回 false=全部超限或失败）。
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
            OutputAction(doc);
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

    /// <summary>按模板（或内置版式）渲染单张磅单页面。调用前 RenderPage 已完成硬边距平移与缩放。</summary>
    private static void RenderRecord(PrintTemplate? tpl, Graphics g, Rectangle bounds, WeighRecord r)
    {
        if (tpl != null)
        {
            // 打印 Graphics 固定 PageUnit=Display（1 逻辑单位 = 1/100 英寸），与打印机物理 DPI
            // 无关 → 毫米换算系数固定 100/25.4。若用 g.DpiX/25.4：600dpi 打印机会按设备像素
            // 解释（23.6 vs 3.94），内容整体放大 6 倍移出纸面，预览/打印整页空白
            // （2026-10-04 用户反馈三等分模板打印预览空白）。
            TemplateTicketRenderer.Render(tpl, g, r, AppConfig.Current, 100f / 25.4f);
        }
        else
        {
            DrawTicket(g, bounds, r);
        }
    }

    /// <summary>
    /// PrintPage 共用入口：
    /// ① 原点锚定物理纸边，再平移到打印机硬边距（可打印区起点）——模板 Y=2mm 的标题不再
    ///    落入打印机不可打印区被裁，且各电脑/打印机行为一致（硬边距仅造成整体物理位移，
    ///    布局与缩放不变）；
    /// ② 模板模式按"物理纸张减去硬边距后的可打印区"自适应缩放：驱动忽略自定义纸张时
    ///    整体等比缩小放入，不裁切（2026-10-02 走纸/右半栏被裁问题）。
    /// </summary>
    private static void RenderPage(PrintPageEventArgs e, PrintTemplate? tpl, WeighRecord r,
        bool showPaperWarning = true)
    {
        var g = e.Graphics!;
        // 打印机硬边距（物理纸边到可打印区，单位 1/100 英寸）；虚拟打印机通常为 0
        float hardX = Math.Max(0, e.PageSettings.HardMarginX);
        float hardY = Math.Max(0, e.PageSettings.HardMarginY);
        g.ResetTransform();

        if (tpl != null)
        {
            var (designW, designH) = PaperSizeMm(tpl.PaperKey);
            // 可打印区 = 物理纸张减去上下左右硬边距
            float availW = Math.Max(50, e.PageBounds.Width - 2 * hardX) * 25.4f / 100f;
            float availH = Math.Max(50, e.PageBounds.Height - 2 * hardY) * 25.4f / 100f;
            float scale = ComputeFitScale(tpl, availW, availH);
            // 判定依据：模板内容实际所需宽度（元素最大右缘），而非模板纸宽——针式机 241 纸扣掉
            // 左右硬边距后可用宽必然 < 241，若按纸宽判定会每一次都误报"纸张不符"。
            float contentW = tpl.Elements.Count == 0 ? designW : tpl.Elements.Max(e => e.X + e.W);
            bool narrow = availW < contentW - 1f;    // 内容超出可打印区 → 需缩放防裁切
            bool tooTall = availH > designH + 2f;    // 实际纸张比模板高 → 走纸超过一联
            if (narrow || tooTall)
            {
                Log.Warn($"打印纸张与模板「{tpl.Name}」不符：模板纸 {designW:F0}×{designH:F0}mm，"
                    + $"打印机实际 {availW:F0}×{availH:F0}mm"
                    + (narrow ? $"（内容已按 {scale:P0} 缩放完整打印）" : "（走纸可能超过一联）"));
                // 指引弹窗仅实际打印时弹；打印预览/自动化渲染不弹（模态框会阻塞）
                if (showPaperWarning) WarnPaperMismatchOnce(tpl, designW, designH, availW, availH, narrow);
            }
            // 变换顺序：物理位置 = 硬边距 + 缩放×模板坐标（GDI+ 先缩放后平移）
            g.ScaleTransform(scale, scale);
            g.TranslateTransform(hardX, hardY);
            RenderRecord(tpl, g, Rectangle.Empty, r);
        }
        else
        {
            g.TranslateTransform(hardX, hardY);
            var printable = new Rectangle(0, 0,
                (int)Math.Max(50, e.PageBounds.Width - 2 * hardX),
                (int)Math.Max(50, e.PageBounds.Height - 2 * hardY));
            RenderRecord(null, g, printable, r);
        }
    }

    /// <summary>
    /// 模板打印自适应缩放系数（≤1）：打印机实际纸张小于模板内容尺寸时等比缩小，
    /// 保证内容完整不被裁切；纸张相符、内容为空或坐标为 0 时为 1。internal 便于单测。
    /// </summary>
    internal static float ComputeFitScale(PrintTemplate t, float availWmm, float availHmm)
    {
        if (t.Elements.Count == 0) return 1f;
        float w = t.Elements.Max(e => e.X + e.W);
        float h = t.Elements.Max(e => e.Y + e.H);
        if (w <= 0 || h <= 0 || availWmm <= 0 || availHmm <= 0) return 1f;
        return Math.Min(1f, Math.Min(availWmm / w, availHmm / h));
    }

    /// <summary>
    /// 打印机实际可打印区域（毫米）：PageBounds 与 MarginBounds 取较小者（1/100 英寸 → 毫米）。
    /// 驱动忽略自定义纸张回退默认纸张（更窄）时，按实际纸张渲染才不会把右半栏裁掉。
    /// designW/designH：模板设计纸张（毫米），用于兜底判断。
    /// </summary>
    private static (float W, float H) ActualPageMm(PrintPageEventArgs e, float designW, float designH)
    {
        float w = Math.Min(e.PageBounds.Width, e.MarginBounds.Width) * 25.4f / 100f;
        float h = Math.Min(e.PageBounds.Height, e.MarginBounds.Height) * 25.4f / 100f;
        // 防御：个别驱动返回异常放大的边界（单位误判等）时退回设计纸张，不误缩放/误报
        if (w > designW * 2f && h > designH * 2f) return (designW, designH);
        return (w, h);
    }

    private static bool _paperWarned;

    /// <summary>纸张不符指引（每进程只弹一次，避免批量打印反复打断）。</summary>
    private static void WarnPaperMismatchOnce(PrintTemplate tpl, float designW, float designH,
        float availW, float availH, bool narrow)
    {
        if (_paperWarned) return;
        _paperWarned = true;
        if (!AppConfig.Current.ShowPaperSizeWarn) return;   // 综合设置关闭后仅自动缩放、不弹窗
        MessageBox.Show(
            $"打印机当前纸张与模板「{tpl.Name}」不符：\n"
            + $"　模板纸张：{designW:F0}×{designH:F0} mm\n"
            + $"　打印机实际：{availW:F0}×{availH:F0} mm\n\n"
            + (narrow ? "内容已自动缩放以完整打印。\n" : "走纸长度可能超过一联纸。\n")
            + $"如需标准效果，请在打印机属性 → 首选项/自定义表单中新建并选用 "
            + $"{designW:F0}×{designH:F0}mm 纸张（程序自动匹配驱动中同尺寸纸张）。",
            "打印纸张提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
