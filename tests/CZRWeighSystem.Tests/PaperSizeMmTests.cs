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

/// <summary>
/// 纸样命名回归（2026-10-02 用户反馈：二等分打出占好几联）——
/// 行业惯例二等分 = 241×140、三等分 = 241×93，此前内置模板命名恰好相反，
/// 用户按名字选"二等分"实际按 93mm 页高打印，驱动页长不符时一张磅单占用多联纸。
/// </summary>
public class PaperNamingTests
{
    [Fact]
    public void 内置模板_三等分为241x93()
    {
        // 2026-10 精简后内置模板仅保留"公平计量磅单-三等分"（241×93 三等分连单）
        var byName = PrintTemplate.CreateBuiltins().ToDictionary(t => t.Name);
        Assert.Equal("241x93", byName["公平计量磅单-三等分"].PaperKey);
    }

    [Fact]
    public void 纸张目录_显示名与尺寸对应正确()
    {
        Assert.Contains(PoundTicketPrinter.Papers,
            p => p.Key == "241x140" && p.Name.Contains("二等分"));
        Assert.Contains(PoundTicketPrinter.Papers,
            p => p.Key == "241x93" && p.Name.Contains("三等分"));
    }

    [Fact]
    public void 默认打印模板_为公平计量磅单三等分()
    {
        // 2026-10-04 用户指定：新装机/未单独设置时默认打印"公平计量磅单-三等分"（241×93 三等分连单）；
        // 模板文件由 EnsureBuiltinsOnDisk 在程序启动时生成（Program.Main 已调用）
        string expected = PrintTemplate.BuiltinFileName(
            PrintTemplate.CreateBuiltins().Single(t => t.Name == "公平计量磅单-三等分"));

        Assert.Equal(expected, new AppConfig().PrintTemplateFile);
    }

    [Fact]
    public void EnsureBuiltinsOnDisk_版式版本升级_重建为最新版式并保持启用()
    {
        // 2026-10-04 公平计量模板版式升级：已装机用户的旧文件必须被自动删除重建为新版式
        // （含白联/红联/蓝联标注、站点名称、承诺占位符、防右侧裁切的内容收窄），启用配置保持指向同名文件
        var builtin = PrintTemplate.CreateBuiltins().Single(t => t.Name == "公平计量磅单-三等分");
        string file = PrintTemplate.BuiltinFileName(builtin);

        Directory.CreateDirectory(PrintTemplate.TemplatesDir);
        new PrintTemplate { Name = builtin.Name, Category = "通用", PaperKey = "241x93", LayoutVersion = "3" }
            .Save(file);
        var cfg = AppConfig.Current;
        cfg.PrintTemplateFile = file;

        try
        {
            PrintTemplate.EnsureBuiltinsOnDisk();

            var rebuilt = PrintTemplate.Load(file);
            Assert.Equal("7", rebuilt.LayoutVersion);
            Assert.Contains(rebuilt.Elements.OfType<TextElement>(), e => e.Text == "白联:存根");
            Assert.Contains(rebuilt.Elements.OfType<TextElement>(), e => e.Text == "{站点名称}");
            Assert.Contains(rebuilt.Elements.OfType<TextElement>(), e => e.Text == "{承诺}");
            Assert.Contains(rebuilt.Elements.OfType<TextElement>(), e => e.Text == "{货主电话}");   // 版式 7：货主电话行
            Assert.Equal(file, cfg.PrintTemplateFile);   // 同名重建，启用配置不变
        }
        finally
        {
            cfg.PrintTemplateFile = "";
            cfg.Save();
        }
    }
}
