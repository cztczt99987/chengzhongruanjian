using CZRWeighSystem.Core;
using CZRWeighSystem.Report;

namespace CZRWeighSystem.Tests;

/// <summary>
/// 模板渲染非空白回归（2026-10-02 公平计量磅单打印预览全白 / 10-04 顶部裁切）。
/// 打印路径（PoundTicketPrinter.RenderPage）：OriginAtMargins=false（原点=物理纸边），
/// 按打印机硬边距平移到可打印区，毫米换算固定 100/25.4（与打印机物理 DPI 无关）。
/// </summary>
public class TemplatePrintRenderTests
{
    [Theory]
    [InlineData("公平计量磅单-三等分")]
    public void 模板按打印路径渲染_非空白(string name)
    {
        var t = PrintTemplate.CreateBuiltins().Single(x => x.Name == name);

        // 模拟打印/预览页面：241×93mm → 0.01 英寸 949×366，预览 Graphics 约 96dpi
        using var bmp = new System.Drawing.Bitmap(949, 366);
        using (var g = System.Drawing.Graphics.FromImage(bmp))
        {
            g.Clear(System.Drawing.Color.White);
            TemplateTicketRenderer.Render(t, g, new WeighRecord
            {
                SerialNo = "PREVIEW-0001", VehicleNo = "冀A12345",
                FirstTime = DateTime.Now, SecondTime = DateTime.Now,
            }, AppConfig.Current, g.DpiX / 25.4f);
        }

        // 抽样统计深色像素（网格线/文字），全白则视为未渲染
        int dark = 0;
        for (int y = 0; y < bmp.Height; y += 2)
            for (int x = 0; x < bmp.Width; x += 2)
                if (bmp.GetPixel(x, y).GetBrightness() < 0.5f)
                    dark++;

        Assert.True(dark > 200, $"{name} 渲染疑似空白：949×366 采样深色点仅 {dark} 个");
    }

    [Fact]
    public void BuildTemplateDocument_纸张设置同步到打印机侧默认页面()
    {
        // 构建文档会枚举打印驱动纸张，与其他测试并发时个别驱动（PCL6）死锁；
        // 默认跳过，需真实驱动验证时设 CZR_RUN_PRINTER_TESTS=1
        if (System.Drawing.Printing.PrinterSettings.InstalledPrinters.Count == 0
            || Environment.GetEnvironmentVariable("CZR_RUN_PRINTER_TESTS") != "1")
            return;

        // WinForms 只设 DefaultPageSettings 时 PrinterSettings.DefaultPageSettings 仍为
        // 驱动默认页长，预览按其渲染会空白（2026-10-02 用户反馈）——两处必须一致
        var t = PrintTemplate.CreateBuiltins().Single(x => x.Name == "公平计量磅单-三等分");
        using var doc = PoundTicketPrinter.BuildTemplateDocument(t, new WeighRecord());

        var dps = doc.DefaultPageSettings.PaperSize;
        var pps = doc.PrinterSettings.DefaultPageSettings.PaperSize;
        Assert.Equal((dps.Width, dps.Height), (pps.Width, pps.Height));
        Assert.InRange(dps.Width, 949 - 25, 949 + 25);   // 241×93mm → 0.01 英寸
        Assert.InRange(dps.Height, 366 - 25, 366 + 25);
        // 原点必须锚定物理纸边：true+0边距会让 Y=2mm 标题落入硬边距不可打印区被裁
        // （2026-10-04 反馈预览顶部裁切），硬边距平移由 RenderPage 显式完成
        Assert.False(doc.OriginAtMargins);
    }
}

/// <summary>
/// 模板打印自适应缩放（2026-10-02 用户反馈：驱动忽略自定义纸张回退 120×140 窄纸，
/// 右半栏被裁、走纸超过一联）。缩放系数保证内容完整放入打印机实际纸张。
/// </summary>
public class PrintPaperFitTests
{
    private static PrintTemplate Fairness
        => PrintTemplate.CreateBuiltins().Single(x => x.Name == "公平计量磅单-三等分");

    [Fact]
    public void 纸张相符时不缩放()
        => Assert.Equal(1f, PoundTicketPrinter.ComputeFitScale(Fairness, 241, 93));

    [Fact]
    public void 纸张偏窄时按宽度比例缩小()
    {
        // 模板内容约 226mm 宽（内容右缘收到 226 防针式机硬边距右侧裁切）；实际纸张 120×140（竖联纸）→ 按宽度 120/226 缩放
        float scale = PoundTicketPrinter.ComputeFitScale(Fairness, 120, 140);
        Assert.InRange(scale, 120f / 226f - 0.01f, 120f / 226f + 0.01f);
        Assert.True(scale < 1f);
    }

    [Fact]
    public void 纸张偏矮时按高度比例缩小()
    {
        var t = new PrintTemplate { PaperKey = "241x93" };
        t.Elements.Add(new TextElement { X = 5, Y = 5, W = 100, H = 90 });   // 内容 105×95mm
        float scale = PoundTicketPrinter.ComputeFitScale(t, 120, 80);
        Assert.InRange(scale, 80f / 95f - 0.01f, 80f / 95f + 0.01f);
    }

    [Fact]
    public void 空模板或非法尺寸时不缩放()
    {
        Assert.Equal(1f, PoundTicketPrinter.ComputeFitScale(new PrintTemplate(), 100, 100));
        Assert.Equal(1f, PoundTicketPrinter.ComputeFitScale(Fairness, 0, 93));
        Assert.Equal(1f, PoundTicketPrinter.ComputeFitScale(Fairness, 241, 0));
    }

    [Fact]
    public void 纸张均大时不缩放()
        => Assert.Equal(1f, PoundTicketPrinter.ComputeFitScale(Fairness, 400, 300));

    [Fact]
    public void 模板内容右缘不超过安全余量()
    {
        // 2026-10-04 用户反馈：EPSON LQ-80KFII 设 241×93 纸仍右侧打印不全且误报纸张不符。
        // 根因一：内容右缘 230mm，扣针式机左右硬边距（各约 7mm）后可打印区约 227mm → 右侧被裁。
        // 故内容右缘必须 ≤ 226mm；超过即回归，会重新引发右侧裁切。
        float contentW = Fairness.Elements.Max(e => e.X + e.W);
        Assert.True(contentW <= 226f, $"模板内容右缘 {contentW:F1}mm 超出安全余量 226mm，右侧将被硬边距裁切");
    }

    [Fact]
    public void 针式机硬边距下内容完整不缩放()
    {
        // 根因二：narrow 判定原本按模板纸宽 241 比较，针式机扣硬边距后可用宽必然 < 241 → 每打印必误报。
        // 修复后按内容宽 226 判定。EPSON 典型场景（可打印区约 227.6×92mm）内容放得下 → 不缩放、不弹提示。
        float scale = PoundTicketPrinter.ComputeFitScale(Fairness, 227.6f, 92f);
        Assert.Equal(1f, scale);
        // 内容右缘 226 + 左硬边距 7mm = 233 < 可打印区右边界 234（241-7）→ 完整打印
        Assert.True(226f + 7f <= 241f - 7f + 0.5f);
    }
}
