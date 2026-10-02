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
    public void 内置模板共9种_标准与金额各四种纸样且名称唯一()
    {
        var builtins = PrintTemplate.CreateBuiltins();
        Assert.Equal(9, builtins.Count);
        Assert.Equal(builtins.Count, builtins.Select(t => t.Name).Distinct().Count());
        // 标准磅单/金额磅单 × 四种纸样（A4/二等分/三等分/竖联）+ 公平计量磅单（三等分）
        Assert.Equal(4, builtins.Count(t => t.Category == "标准"));
        Assert.Equal(4, builtins.Count(t => t.Category == "金额"));
        Assert.Equal(1, builtins.Count(t => t.Category == "通用"));
        Assert.Equal(2, builtins.Count(t => t.PaperKey == "A4"));
        Assert.Equal(2, builtins.Count(t => t.PaperKey == "241x140"));
        Assert.Equal(3, builtins.Count(t => t.PaperKey == "241x93"));
        Assert.Equal(2, builtins.Count(t => t.PaperKey == "120x140"));
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
    public void 内置模板包含新元素类型()
    {
        var builtins = PrintTemplate.CreateBuiltins();
        Assert.Contains(builtins, t => t.Elements.Any(e => e is TableElement));
        Assert.Contains(builtins, t => t.Elements.Any(e => e is SignatureElement));
        Assert.Contains(builtins, t => t.Elements.Any(e => e is QRCodeElement));
        Assert.Contains(builtins, t => t.Elements.Any(e => e is BarcodeElement));
    }

    [Fact]
    public void 内置模板JSON往返保留全部元素类型()
    {
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
