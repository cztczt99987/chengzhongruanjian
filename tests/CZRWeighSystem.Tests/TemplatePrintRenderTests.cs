using CZRWeighSystem.Core;
using CZRWeighSystem.Report;

namespace CZRWeighSystem.Tests;

/// <summary>
/// 模板按打印文档路径渲染的非空白回归（2026-10-02 用户反馈公平计量磅单打印预览全白）。
/// 复现 BuildTemplateDocument 的 PrintPage 路径：OriginAtMargins=true（原点=可打印区起点，
/// 无需平移）+ DpiX 换算渲染。
/// </summary>
public class TemplatePrintRenderTests
{
    [Theory]
    [InlineData("公平计量磅单-三等分")]
    [InlineData("标准磅单-三等分")]
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
        // WinForms 只设 DefaultPageSettings 时 PrinterSettings.DefaultPageSettings 仍为
        // 驱动默认页长，预览按其渲染会空白（2026-10-02 用户反馈）——两处必须一致
        var t = PrintTemplate.CreateBuiltins().Single(x => x.Name == "公平计量磅单-三等分");
        using var doc = PoundTicketPrinter.BuildTemplateDocument(t, new WeighRecord());

        var dps = doc.DefaultPageSettings.PaperSize;
        var pps = doc.PrinterSettings.DefaultPageSettings.PaperSize;
        Assert.Equal((dps.Width, dps.Height), (pps.Width, pps.Height));
        Assert.InRange(dps.Width, 949 - 25, 949 + 25);   // 241×93mm → 0.01 英寸
        Assert.InRange(dps.Height, 366 - 25, 366 + 25);
        Assert.True(doc.OriginAtMargins);                // 模板坐标即所见即所得（内容不偏移）
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
        // 模板内容约 230×66mm；实际纸张 120×140（竖联纸）→ 按宽度 120/230 缩放
        float scale = PoundTicketPrinter.ComputeFitScale(Fairness, 120, 140);
        Assert.InRange(scale, 120f / 230f - 0.01f, 120f / 230f + 0.01f);
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
}
