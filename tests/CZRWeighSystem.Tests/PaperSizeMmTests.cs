using CZRWeighSystem.Report;

namespace CZRWeighSystem.Tests;

/// <summary>纸张毫米尺寸换算测试（回归：模板预览/设计器画布曾误把 0.01 英寸当毫米导致预览空白）。</summary>
public class PaperSizeMmTests
{
    [Theory]
    [InlineData("A4", 210.0, 297.0)]
    [InlineData("241x140", 241.0, 140.0)]
    [InlineData("241x93", 241.0, 93.0)]
    public void PaperSizeMm_尺寸换算正确(string key, double wMm, double hMm)
    {
        var (w, h) = PoundTicketPrinter.PaperSizeMm(key);

        // Papers 以 0.01 英寸近似存储，允许 ±0.5mm 误差
        Assert.InRange(w, wMm - 0.5, wMm + 0.5);
        Assert.InRange(h, hMm - 0.5, hMm + 0.5);
    }

    [Fact]
    public void PaperSizeMm_未知key回退第一项()
    {
        var (w, h) = PoundTicketPrinter.PaperSizeMm("不存在");
        var a4 = PoundTicketPrinter.PaperSizeMm("A4");

        Assert.Equal(a4, (w, h));
    }

    [Fact]
    public void 内置模板_元素均在纸张毫米范围内()
    {
        foreach (var t in PrintTemplate.CreateBuiltins())
        {
            var (w, h) = PoundTicketPrinter.PaperSizeMm(t.PaperKey);
            Assert.True(w > 0 && h > 0, $"{t.Name}: 纸张尺寸异常");

            foreach (var e in t.Elements)
                Assert.True(e.X >= 0 && e.Y >= 0 && e.X + e.W <= w && e.Y + e.H <= h,
                    $"{t.Name}: 元素超出纸张范围（{e.X},{e.Y},{e.W},{e.H}，纸张 {w}×{h}mm）");
        }
    }
}
