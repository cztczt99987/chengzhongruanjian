using CZRWeighSystem.Core;
using CZRWeighSystem.Report;
using System.Text.Json;

namespace CZRWeighSystem.Tests;

/// <summary>占位符格式后缀（{字段:格式}）测试。</summary>
public class ExpandFormatTests
{
    private static Dictionary<string, string> Ph => new()
    {
        ["毛重"] = "34.6",
        ["一次磅时间"] = "2026-09-03 08:30:00",
        ["车号"] = "冀A12345",
    };

    private static Dictionary<string, object?> Raw => new()
    {
        ["毛重"] = 34.6,
        ["一次磅时间"] = new DateTime(2026, 9, 3, 8, 30, 0),
    };

    [Fact]
    public void 数字字段按格式输出()
    {
        Assert.Equal("34.6", PrintTemplate.Expand("{毛重:F1}", Ph, Raw));
        Assert.Equal("35", PrintTemplate.Expand("{毛重:F0}", Ph, Raw));
    }

    [Fact]
    public void 日期字段按格式输出()
    {
        Assert.Equal("2026-09-03", PrintTemplate.Expand("{一次磅时间:yyyy-MM-dd}", Ph, Raw));
        Assert.Equal("08:30", PrintTemplate.Expand("{一次磅时间:HH:mm}", Ph, Raw));
    }

    [Fact]
    public void 非法格式回退默认字符串值()
    {
        Assert.Equal("34.6", PrintTemplate.Expand("{毛重:不是合法格式}", Ph, Raw));
    }

    [Fact]
    public void 原值缺失时退化为默认字符串值()
    {
        Assert.Equal("34.6", PrintTemplate.Expand("{毛重:F1}", Ph, null));
        var rawNull = new Dictionary<string, object?> { ["毛重"] = null };
        Assert.Equal("34.6", PrintTemplate.Expand("{毛重:F1}", Ph, rawNull));
    }

    [Fact]
    public void 未知占位符带格式原样保留()
    {
        Assert.Equal("{不存在:F1}", PrintTemplate.Expand("{不存在:F1}", Ph, Raw));
    }

    [Fact]
    public void 无后缀占位符行为不变()
    {
        Assert.Equal("冀A12345", PrintTemplate.Expand("{车号}", Ph, Raw));
    }
}

/// <summary>Code39 编码测试。</summary>
public class Code39Tests
{
    [Theory]
    [InlineData("12345")]
    [InlineData("ABC-123")]
    [InlineData("20260903-01-0001")]
    [InlineData("A B")]
    public void 合法字符可通过校验(string text) => Assert.True(Code39.IsValid(text));

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("中文字符")]
    [InlineData("abc#123")]
    public void 非法字符不可通过校验(string? text) => Assert.False(Code39.IsValid(text));

    [Fact]
    public void TotalUnits与Encode序列一致()
    {
        foreach (var text in new[] { "123", "PREVIEW-0001", "A1B2C3" })
            Assert.Equal(Code39.Encode(text).Sum(u => u.Units), Code39.TotalUnits(text));
    }

    [Fact]
    public void 每字符编码恰为9单元且3宽6窄()
    {
        foreach (var c in Code39.Charset)
        {
            // 单字符编码 = 起止符 + 字符 + 起止符 + 2 个字符间隔
            var seq = Code39.Encode(c.ToString());
            // 直接校验内部结构：取每个字符的 9 元素段（含起止符与间隔共 29 段）
            Assert.Equal(29, seq.Count);   // 9*3 + 2 = 29
            int wide = seq.Count(u => u.Units == 2);
            int narrow = seq.Count(u => u.Units == 1);
            // 3 个字符各 3 宽 6 窄 = 9 宽 18 窄 + 2 个窄间隔 = 9 宽 20 窄
            Assert.Equal(9, wide);
            Assert.Equal(20, narrow);
        }
    }

    [Fact]
    public void 条空交替且以条开始结束()
    {
        var seq = Code39.Encode("123");
        Assert.True(seq[0].Bar);
        Assert.True(seq[^1].Bar);
        for (int i = 1; i < seq.Count; i++)
        {
            // 同字符内条空交替；字符间隔（1 窄空）后接新字符的条
            if (seq[i - 1].Bar) Assert.False(seq[i].Bar);
        }
    }

    [Fact]
    public void 大小写归一化()
    {
        Assert.Equal(Code39.TotalUnits("ABC123"), Code39.TotalUnits("abc123"));
    }
}

/// <summary>内置模板库测试。</summary>
public class BuiltinTemplatesTests
{
    [Fact]
    public void 内置模板共2种_名称唯一且纸样均登记()
    {
        var builtins = PrintTemplate.CreateBuiltins();
        Assert.Equal(2, builtins.Count);
        Assert.Equal(builtins.Count, builtins.Select(t => t.Name).Distinct().Count());
        // 2026-10 精简：公平计量磅单（三等分 241×93）+ 2026-10-14 新增热敏小票-58mm（58×135）
        Assert.Equal(2, builtins.Count(t => t.Category == "通用"));
        Assert.Equal(1, builtins.Count(t => t.PaperKey == "241x93"));
        Assert.Equal(1, builtins.Count(t => t.PaperKey == "58x135"));
        Assert.All(builtins, t =>
        {
            Assert.False(string.IsNullOrWhiteSpace(t.Name));
            Assert.False(string.IsNullOrWhiteSpace(t.Category));
            Assert.Contains(t.PaperKey, PoundTicketPrinter.Papers.Select(p => p.Key));
            Assert.NotEmpty(t.Elements);
        });
    }

    [Fact]
    public void 公平计量磅单_三等分_版式要素齐全()
    {
        var t = PrintTemplate.CreateBuiltins().Single(x => x.Name == "公平计量磅单-三等分");
        Assert.Equal("241x93", t.PaperKey);

        string text = string.Join("\n", t.Elements.OfType<TextElement>().Select(e => e.Text));
        Assert.Contains("第{打印次数}次打印", text);
        Assert.Contains("编号：{流水号}", text);
        Assert.Contains("地主姓名", text);
        Assert.Contains("司机电话", text);
        Assert.Contains("毛重司磅", text);
        Assert.Contains("皮重司磅", text);
        Assert.Contains("承诺", text);
        // 弹窗固定字段补齐（2026-10-04）：品种（记录 Goods 列）与单价（ext_json 固定键）
        Assert.Contains("品种", text);
        Assert.Contains("{货物}", text);
        Assert.Contains("单价", text);
        Assert.Contains("{单价}", text);
        // 货主电话（ext_json 固定键）同步进默认模板（2026-10-05）
        Assert.Contains("货主电话", text);
        Assert.Contains("{货主电话}", text);
    }

    [Fact]
    public void 公平计量磅单_联次标注位于网格右外侧且三行均匀分布()
    {
        // 2026-10-04 用户要求仿实物：表格右侧竖排"白联:存根/红联:客户/蓝联:司机"
        var t = PrintTemplate.CreateBuiltins().Single(x => x.Name == "公平计量磅单-三等分");
        var marks = t.Elements.OfType<TextElement>()
            .Where(e => e.Text is "白联:存根" or "红联:客户" or "蓝联:司机").ToList();

        Assert.Equal(3, marks.Count);
        // 网格右缘 = 8 + 176 = 184：三行标注须位于网格右侧空白区且不超内容右缘 206（防 215 纸右侧裁切）
        Assert.All(marks, e => Assert.True(e.X >= 184f && e.X + e.W <= 206f,
            $"联次标注「{e.Text}」不在网格右侧空白区（X={e.X}，右缘={e.X + e.W}）"));
        // 竖直方向按网格高度（6 行 × 6.5mm = 39mm）三等分：行距相等（13mm）
        var ys = marks.OrderBy(e => e.Y).Select(e => e.Y).ToList();
        Assert.InRange(ys[1] - ys[0], 12.8f, 13.2f);
        Assert.InRange(ys[2] - ys[1], 12.8f, 13.2f);
    }

    [Fact]
    public void 打印次数占位符_取已打印次数加一()
    {
        var r = new WeighRecord { PrintCount = 2 };
        var ph = PrintTemplate.BuildPlaceholders(r, AppConfig.Current);
        Assert.Equal("3", ph["打印次数"]);
        Assert.Equal("1", PrintTemplate.BuildPlaceholders(
            new WeighRecord(), AppConfig.Current)["打印次数"]);
    }

    [Fact]
    public void 内置模板JSON往返保留全部元素类型()
    {
        // 2026-10 精简后内置模板仅公平计量磅单（Text/Line/Rect 元素）；其他元素类型
        // 仍可由模板设计器/用户模板承载，往返序列化逐一验证
        foreach (var t in PrintTemplate.CreateBuiltins())
        {
            var json = JsonSerializer.Serialize(t);
            var back = JsonSerializer.Deserialize<PrintTemplate>(json)!;
            Assert.Equal(t.Elements.Count, back.Elements.Count);
            for (int i = 0; i < t.Elements.Count; i++)
                Assert.Equal(t.Elements[i].GetType(), back.Elements[i].GetType());
            Assert.Equal(t.Category, back.Category);
        }
    }

    [Fact]
    public void 内置文件名以内置前缀命名()
    {
        var t = PrintTemplate.CreateBuiltins()[0];
        Assert.Equal($"内置_{t.Name}.json", PrintTemplate.BuiltinFileName(t));
        Assert.True(PrintTemplate.IsBuiltinFileName(PrintTemplate.BuiltinFileName(t)));
        Assert.False(PrintTemplate.IsBuiltinFileName("我的模板.json"));
    }
}

/// <summary>
/// 58mm 热敏小票模板回归（2026-10-14 用户提供实物小票照片：
/// 要求小票版式，且字段内容与现有唯一模板「公平计量磅单-三等分」完全一致）。
/// </summary>
public class ThermalReceiptTemplateTests
{
    /// <summary>58mm 热敏纸打印头有效宽（384 点 / 203dpi ≈ 48mm）。</summary>
    private const float PrintHeadWidthMm = 48f;

    private static PrintTemplate Thermal =>
        PrintTemplate.CreateBuiltins().Single(t => t.Name == "热敏小票-58mm");

    private static string AllText(PrintTemplate t) =>
        string.Join("\n", t.Elements.OfType<TextElement>().Select(e => e.Text));

    [Fact]
    public void 热敏小票_纸样为58x135_内容不超出打印头有效宽度()
    {
        var t = Thermal;
        Assert.Equal("58x135", t.PaperKey);

        var (paperW, paperH) = PoundTicketPrinter.PaperSizeMm(t.PaperKey);
        Assert.InRange(paperW, 57.5f, 58.5f);
        Assert.InRange(paperH, 134.5f, 135.5f);

        // 58mm 纸两侧硬边距吃掉约 10mm，内容右缘超出打印头有效宽即被裁
        float maxRight = t.Elements.Max(e => e.X + e.W);
        Assert.True(maxRight <= PrintHeadWidthMm,
            $"内容右缘 {maxRight:F1}mm 超过 58mm 纸打印头有效宽 {PrintHeadWidthMm}mm");

        float maxBottom = t.Elements.Max(e => e.Y + e.H);
        Assert.True(maxBottom <= paperH, $"内容底边 {maxBottom:F1}mm 超出纸张高 {paperH:F0}mm");
    }

    [Fact]
    public void 热敏小票_字段与占位符与公平计量磅单完全一致()
    {
        // 用户要求：现有唯一模板里的内容，小票模板必须都有
        var fairness = PrintTemplate.CreateBuiltins().Single(t => t.Name == "公平计量磅单-三等分");
        var placeholders = System.Text.RegularExpressions.Regex
            .Matches(AllText(fairness), @"\{[^}]+\}").Select(m => m.Value).Distinct().ToList();
        Assert.NotEmpty(placeholders);

        string thermalText = AllText(Thermal);
        foreach (var ph in placeholders)
            Assert.True(thermalText.Contains(ph), $"小票模板缺少占位符 {ph}");
    }

    [Fact]
    public void 热敏小票_标签与联次标注齐全()
    {
        string text = AllText(Thermal);
        // 单列字段标签（含品种取 {货物}、单价取 ext 固定键）
        foreach (var label in new[]
        {
            "地主姓名", "地主电话", "车号", "司机电话", "品种", "货主电话", "货主姓名", "单价",
            "毛重", "皮重", "净重", "毛重司磅", "皮重司磅",
        })
            Assert.Contains(label, text);
        // 联次标注与三等分模板同义
        foreach (var mark in new[] { "白联:存根", "红联:客户", "蓝联:司机" })
            Assert.Contains(mark, text);
    }

    [Fact]
    public void 热敏小票_单列标签列不与值列重叠()
    {
        // 每行标签元素右缘 ≤ 同行值元素左缘（窄纸两列不能压字）
        var texts = Thermal.Elements.OfType<TextElement>().ToList();
        var labels = texts.Where(e => e.X < 13f && e.W <= 12f).ToList();
        Assert.True(labels.Count >= 11, $"标签元素数量不足：{labels.Count}");

        foreach (var label in labels)
        {
            var value = texts.FirstOrDefault(v => Math.Abs(v.Y - label.Y) < 0.01f && v.X > label.X);
            Assert.NotNull(value);
            Assert.True(label.X + label.W <= value!.X,
                $"标签「{label.Text}」右缘 {label.X + label.W:F1}mm 压到值列左缘 {value.X:F1}mm");
        }
    }
}

/// <summary>颜色解析测试。</summary>
public class ParseColorTests
{
    [Fact]
    public void 十六进制颜色解析()
    {
        Assert.Equal(Color.FromArgb(204, 0, 0), TemplateTicketRenderer.ParseColor("#CC0000", Color.Black));
    }

    [Fact]
    public void 颜色名解析()
    {
        Assert.Equal(Color.Red, TemplateTicketRenderer.ParseColor("Red", Color.Black));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("#不是颜色")]
    [InlineData("不存在颜色名")]
    public void 非法颜色回退(string? s)
        => Assert.Equal(Color.Black, TemplateTicketRenderer.ParseColor(s, Color.Black));
}
