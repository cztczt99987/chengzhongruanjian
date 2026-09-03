using CZRWeighSystem.Core;
using CZRWeighSystem.Report;

namespace CZRWeighSystem.Tests;

/// <summary>渲染冒烟测试：真实 Graphics 下渲染全部内置模板（覆盖表格/签名/二维码/条码绘制路径）。</summary>
public class RendererSmokeTests
{
    [Fact]
    public void 全部内置模板渲染不抛异常()
    {
        var cfg = new AppConfig();
        var r = new WeighRecord
        {
            SerialNo = "20260903-01-0001",
            VehicleNo = "冀A12345",
            Goods = "煤炭",
            GrossKg = 34600,
            TareKg = 11600,
            NetKg = 23000,
            DeductKg = 100,
            FirstTime = new DateTime(2026, 9, 3, 8, 30, 0),
            SecondTime = new DateTime(2026, 9, 3, 9, 0, 0),
            Operator = "张三",
            Status = "已完成",
        };
        foreach (var tpl in PrintTemplate.CreateBuiltins())
        {
            using var bmp = new Bitmap(1000, 1400);
            using var g = Graphics.FromImage(bmp);
            TemplateTicketRenderer.Render(tpl, g, r, cfg, g.DpiX / 25.4f);
        }
    }

    [Fact]
    public void 二维码与条码内容异常时占位不抛异常()
    {
        var cfg = new AppConfig();
        var r = new WeighRecord { SerialNo = "PREVIEW-0001", VehicleNo = "冀A12345" };
        var t = new PrintTemplate
        {
            Elements =
            [
                new QRCodeElement { Content = "", X = 5, Y = 5, W = 20, H = 20 },           // 空 → 占位框
                new QRCodeElement { Content = "{不存在的字段}", X = 30, Y = 5, W = 20, H = 20 }, // 展开后仍是 {…} → 占位框
                new BarcodeElement { Content = "中文内容", X = 5, Y = 30, W = 60, H = 12 },     // Code39 非法 → 占位框
            ],
        };
        using var bmp = new Bitmap(400, 200);
        using var g = Graphics.FromImage(bmp);
        TemplateTicketRenderer.Render(t, g, r, cfg, g.DpiX / 25.4f);
    }
}
