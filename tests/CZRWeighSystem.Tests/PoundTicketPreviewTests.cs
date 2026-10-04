using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Printing;
using System.Runtime.InteropServices;
using CZRWeighSystem.Core;
using CZRWeighSystem.Report;

namespace CZRWeighSystem.Tests;

// 真实打印驱动渲染（PreviewPrintController）在部分驱动上不稳定：PCL6 激光机在并发测试宿主中
// 会弹窗阻塞、连续生成 EMF 后原生崩溃。因此默认跳过，仅在显式设置环境变量
// CZR_RUN_PRINTER_TESTS=1 时运行（开发机手动验证：`$env:CZR_RUN_PRINTER_TESTS=1; dotnet test --filter ...`）。
// 正确性的常驻保障由不依赖驱动的单测承担：TemplatePrintRenderTests（直接渲染非空白）、
// PrintPaperFitTests（缩放系数）、PrintTemplateGeometryTests（全部内置模板边界）。
[Collection("数据库顺序")]
public class PoundTicketPreviewTests
{
    /// <summary>有打印机且显式开启时才跑真实驱动渲染。</summary>
    private static bool DriverTestsEnabled =>
        PrinterSettings.InstalledPrinters.Count > 0
        && Environment.GetEnvironmentVariable("CZR_RUN_PRINTER_TESTS") == "1";

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

    /// <summary>公平计量磅单-三等分（2026-10-04 两次反馈：空白页 + 顶部裁切均出在它身上）。</summary>
    private static PrintTemplate Fairness =>
        PrintTemplate.CreateBuiltins().First(t => t.Name.Contains("公平计量"));

    [Fact]
    public void 打印文档_原点必须锚定物理纸边_由渲染层处理硬边距()
    {
        // 构建文档也会枚举驱动纸张，与其他测试并发时个别驱动（PCL6）会死锁，一并门控
        if (!DriverTestsEnabled) return;

        using var doc = PoundTicketPrinter.BuildTemplateDocument(Fairness, Sample);
        Assert.False(doc.OriginAtMargins);   // true+0边距会让顶部内容落入不可打印区被裁
    }

    [Fact]
    public void 三等分模板_打印预览页_有内容且顶部不裁切_内容落在纸面内()
    {
        if (!DriverTestsEnabled) return;

        using var doc = PoundTicketPrinter.BuildTemplateDocument(Fairness, Sample);

        float hardYmm = Math.Max(0, doc.DefaultPageSettings.HardMarginY) / 100f * 25.4f;
        float pageWmm = doc.DefaultPageSettings.PaperSize.Width / 100f * 25.4f;
        float pageHmm = doc.DefaultPageSettings.PaperSize.Height / 100f * 25.4f;

        var pc = new PreviewPrintController();
        doc.PrintController = pc;
        doc.Print();

        var (dark, firstYmm, lastYmm, firstXmm, lastXmm) = InkBounds(pc.GetPreviewPageInfo()[0].Image);

        // 修复空白页 bug：整页必须有足量墨迹
        Assert.True(dark > 500, $"三等分磅单预览页深色墨迹仅 {dark}，打印渲染整体移出纸面");
        // 修复顶部裁切 bug：模板 Y≈2mm 的标题，物理位置不得早于打印机硬边距
        Assert.True(firstYmm >= hardYmm - 0.5f,
            $"首行墨迹 {firstYmm:F1}mm 早于硬边距 {hardYmm:F1}mm，顶部会被裁");
        // 内容不超出物理纸面
        Assert.True(lastXmm <= pageWmm + 1f && lastYmm <= pageHmm + 1f,
            $"内容超出纸面：({lastXmm:F1},{lastYmm:F1}) > {pageWmm:F0}×{pageHmm:F0}");
        // 左缘不应异常缩进/超出（模板左边距 10mm + 硬边距）
        Assert.True(firstXmm >= hardYmm - 0.5f && firstXmm < 60f,
            $"左缘墨迹 {firstXmm:F1}mm 位置异常");
    }

    /// <summary>
    /// 把预览元文件降采样（1/4）铺白底栅格化，按物理口径统计深色墨迹包围盒（毫米）。
    /// 深色阈值放宽到 180：降采样后细文字/线条被抗锯齿成中灰。
    /// </summary>
    private static (long dark, float firstYmm, float lastYmm, float firstXmm, float lastXmm)
        InkBounds(Image mf)
    {
        const int div = 4;
        using var bmp = new Bitmap(Math.Max(1, mf.Width / div), Math.Max(1, mf.Height / div));
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.White);                 // EMF 背景透明，先铺白底
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.DrawImage(mf, new Rectangle(0, 0, bmp.Width, bmp.Height));
        }

        float dpi = (mf.HorizontalResolution > 0 ? mf.HorizontalResolution : 600) / div;

        var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
        var data = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
        int stride = data.Stride;
        byte[] rgb = new byte[stride * bmp.Height];
        Marshal.Copy(data.Scan0, rgb, 0, rgb.Length);
        bmp.UnlockBits(data);

        long dark = 0;
        int firstY = -1, lastY = -1, firstX = -1, lastX = -1;
        for (int y = 0; y < bmp.Height; y += 3)
            for (int x = 0; x < bmp.Width; x += 3)
            {
                int i = y * stride + x * 3;
                if (rgb[i + 2] < 180 && rgb[i + 1] < 180 && rgb[i] < 180)
                {
                    dark++;
                    if (firstY < 0) firstY = y;
                    lastY = y;
                    if (firstX < 0 || x < firstX) firstX = x;
                    if (x > lastX) lastX = x;
                }
            }

        float Mm(float px) => px / dpi * 25.4f;
        return (dark,
            firstY < 0 ? -1 : Mm(firstY), lastY < 0 ? -1 : Mm(lastY),
            firstX < 0 ? -1 : Mm(firstX), lastX < 0 ? -1 : Mm(lastX));
    }
}

/// <summary>
/// 内置模板几何边界单测（不依赖打印机/驱动，稳定快速）：
/// 保证每个内置模板的所有元素都落在其设计纸张范围内，且包含内容——
/// 从结构上排查"其他模板是否同样错位/越界"。
/// </summary>
public class PrintTemplateGeometryTests
{
    public static IEnumerable<object[]> AllBuiltins() =>
        PrintTemplate.CreateBuiltins().Select(t => new object[] { t.Name, t });

    [Theory]
    [MemberData(nameof(AllBuiltins))]
    public void 内置模板_元素不超出设计纸张(string name, PrintTemplate t)
    {
        var (paperW, paperH) = PoundTicketPrinter.PaperSizeMm(t.PaperKey);
        float maxRight = t.Elements.Max(e => e.X + e.W);
        float maxBottom = t.Elements.Max(e => e.Y + e.H);

        Assert.True(maxRight <= paperW + 0.1f,
            $"模板「{name}」右缘 {maxRight:F1}mm 超出纸张宽 {paperW:F0}mm");
        Assert.True(maxBottom <= paperH + 0.1f,
            $"模板「{name}」底边 {maxBottom:F1}mm 超出纸张高 {paperH:F0}mm");
    }

    [Theory]
    [MemberData(nameof(AllBuiltins))]
    public void 内置模板_元素起点不在纸面外且包含实质内容(string name, PrintTemplate t)
    {
        Assert.True(t.Elements.Count >= 5, $"模板「{name}」元素过少（{t.Elements.Count}），疑似空模板");
        Assert.All(t.Elements, e =>
        {
            Assert.True(e.X >= -0.1f && e.Y >= -0.1f,
                $"模板「{name}」存在负坐标元素（{e.Desc} @ {e.X},{e.Y}）");
        });
    }
}