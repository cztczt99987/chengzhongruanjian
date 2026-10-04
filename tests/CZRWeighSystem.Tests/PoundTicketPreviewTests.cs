using System.Drawing;
using System.Drawing.Printing;
using CZRWeighSystem.Core;
using CZRWeighSystem.Report;

namespace CZRWeighSystem.Tests;

/// <summary>
/// 磅单打印预览渲染回归测试：
/// 2026-10-04 用户反馈"公平计量磅单-三等分"打印预览整页空白。
/// 根因：打印 Graphics 为 PageUnit.Display（1 逻辑单位=1/100"，与打印机物理 DPI 无关），
/// 渲染器却按 g.DpiX/25.4 换算毫米，600dpi 打印机上内容整体放大 6 倍移出纸面。
/// 此测试走与 PrintPreviewDialog 相同的 PreviewPrintController 管线，
/// 断言预览页栅格化后确实有足量黑色墨迹（不是空白页）。
/// </summary>
public class PoundTicketPreviewTests
{
    /// <summary>无打印机的环境（如无打印服务的 CI）跳过：预览控制器依赖默认打印机。</summary>
    private static bool HasPrinter => PrinterSettings.InstalledPrinters.Count > 0;

    private static WeighRecord Sample => new()
    {
        SerialNo = "PREVIEW-0001", VehicleNo = "黑A12345", Goods = "煤炭",
        GrossKg = 34600, TareKg = 11600, NetKg = 23000,
        FirstTime = DateTime.Now.AddHours(-1), SecondTime = DateTime.Now,
        Operator = "管理员", Status = "已完成",
        ExtJson = WeighRecord.ExtToJson(new Dictionary<string, string>
        {
            ["地主姓名"] = "张三", ["地主电话"] = "13800000000",
            ["司机电话"] = "13911111111", ["货主姓名"] = "李四",
        }),
    };

    [Fact]
    public void 三等分模板_打印预览页_必须渲染出内容而非空白()
    {
        if (!HasPrinter) return;   // 无打印机环境无法构造预览打印控制器

        var tpl = PrintTemplate.CreateBuiltins().First(t => t.Name.Contains("公平计量"));
        using var doc = PoundTicketPrinter.BuildTemplateDocument(tpl, Sample);

        var pc = new PreviewPrintController();
        doc.PrintController = pc;
        doc.Print();

        using var mf = pc.GetPreviewPageInfo()[0].Image;
        using var bmp = new Bitmap(mf.Width, mf.Height);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.White);                 // EMF 背景透明，先铺白底
            g.DrawImage(mf, new Rectangle(0, 0, bmp.Width, bmp.Height));
        }

        // 抽样统计深色墨迹（文字/表格线）；空白页为 0，正常页以万计
        long dark = 0;
        for (int y = 0; y < bmp.Height; y += 4)
            for (int x = 0; x < bmp.Width; x += 4)
            {
                var c = bmp.GetPixel(x, y);
                if (c.R < 100 && c.G < 100 && c.B < 100) dark++;
            }
        Assert.True(dark > 1000,
            $"三等分磅单预览页深色墨迹仅 {dark}（应为数千以上），打印渲染可能整体移出纸面");
    }

    [Theory]
    [InlineData("A4")]
    [InlineData("241x140")]
    [InlineData("241x93")]
    public void 各纸张模板_打印预览页_均不得为空白(string paperKey)
    {
        if (!HasPrinter) return;

        var tpl = PrintTemplate.CreateBuiltins().First(t => t.PaperKey == paperKey);
        using var doc = PoundTicketPrinter.BuildTemplateDocument(tpl, Sample);
        var pc = new PreviewPrintController();
        doc.PrintController = pc;
        doc.Print();

        using var mf = pc.GetPreviewPageInfo()[0].Image;
        using var bmp = new Bitmap(mf.Width, mf.Height);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.White);
            g.DrawImage(mf, new Rectangle(0, 0, bmp.Width, bmp.Height));
        }

        long dark = 0;
        for (int y = 0; y < bmp.Height; y += 4)
            for (int x = 0; x < bmp.Width; x += 4)
            {
                var c = bmp.GetPixel(x, y);
                if (c.R < 100 && c.G < 100 && c.B < 100) dark++;
            }
        Assert.True(dark > 1000, $"纸张 {paperKey} 预览页空白（深色墨迹 {dark}）");
    }
}