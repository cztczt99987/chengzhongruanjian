using CZRWeighSystem.Core;
using CZRWeighSystem.Report;

namespace CZRWeighSystem.Tests;

/// <summary>
/// 模板按打印文档路径渲染的非空白回归（2026-10-02 用户反馈公平计量磅单打印预览全白）。
/// 复现 BuildTemplateDocument 的 PrintPage 路径：TranslateTransform(边距起点) + DpiX 换算渲染。
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
            g.TranslateTransform(40, 40);   // BuildTemplateDocument：MarginBounds 起点（0.4 英寸）
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
    }
}
