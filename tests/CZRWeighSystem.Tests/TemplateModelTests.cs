using CZRWeighSystem.Core;
using CZRWeighSystem.Report;
using System.Text.Json;

namespace CZRWeighSystem.Tests;

/// <summary>模板占位符取值测试（含单位吨换算）。</summary>
public class TemplateModelTests
{
    private static WeighRecord SampleRecord() => new()
    {
        SerialNo = "20260903-01-0001",
        ScaleNo = "01",
        VehicleNo = "冀A12345",
        Goods = "煤炭",
        SenderCompany = "发货商A",
        ReceiverCompany = "收货商B",
        Spec = "一级",
        BatchNo = "B2026",
        GrossKg = 34600,
        TareKg = 11600,
        NetKg = 23000,
        DeductKg = 100,
        FirstTime = new DateTime(2026, 9, 3, 8, 30, 0),
        SecondTime = new DateTime(2026, 9, 3, 9, 0, 0),
        Operator = "张三",
        Status = "已完成",
    };

    [Fact]
    public void BuildPlaceholders_关键字段取值正确()
    {
        var ph = PrintTemplate.BuildPlaceholders(SampleRecord(), new AppConfig { Unit = "kg" });

        Assert.Equal("冀A12345", ph["车号"]);
        Assert.Equal("煤炭", ph["货物"]);
        Assert.Equal("发货商A", ph["发货单位"]);
        Assert.Equal("收货商B", ph["收货单位"]);
        Assert.Equal("已完成", ph["状态"]);
        Assert.Equal("23000", ph["净重"]);   // 指定 kg 单位不换算
    }

    [Fact]
    public void BuildPlaceholders_吨单位自动换算()
    {
        var cfg = new AppConfig { Unit = "t" };
        var ph = PrintTemplate.BuildPlaceholders(SampleRecord(), cfg);

        Assert.Equal("23", ph["净重"]);            // 23000kg = 23t
        Assert.Equal("34.6", ph["毛重"]);          // 34600kg = 34.6t
        Assert.Equal("22.9", ph["实收"]);          // 实收 = 净重 - 扣重 = 22.9t
    }

    [Fact]
    public void BuildPlaceholders_空值显示横杠()
    {
        var r = new WeighRecord { SerialNo = "S1", VehicleNo = "冀B00000" };
        var ph = PrintTemplate.BuildPlaceholders(r, new AppConfig());

        Assert.Equal("-", ph["货物"]);
        Assert.Equal("-", ph["发货单位"]);
        Assert.Equal("-", ph["一次磅时间"]);
    }

    [Fact]
    public void BuildPlaceholders_弹窗固定字段未填时为空串()
    {
        // 记录未含 ext_json 固定键（地主姓名/地主电话/司机电话/货主姓名/货主电话/单价）时取值应为空串，
        // 否则公平计量模板中的 {单价} 等会把"{单价}"字面打印到磅单（2026-10-04 补齐字段）
        var r = new WeighRecord { SerialNo = "S1", VehicleNo = "冀B00000" };
        var ph = PrintTemplate.BuildPlaceholders(r, new AppConfig());

        Assert.Equal("", ph["地主姓名"]);
        Assert.Equal("", ph["地主电话"]);
        Assert.Equal("", ph["司机电话"]);
        Assert.Equal("", ph["货主姓名"]);
        Assert.Equal("", ph["货主电话"]);
        Assert.Equal("", ph["单价"]);
        Assert.Equal("", PrintTemplate.Expand("{单价}", ph));   // 展开为空文本而非原样保留
    }

    [Fact]
    public void BuildPlaceholders_固定字段有值时以ext_json为准()
    {
        // 空串兜底不得覆盖记录实际录入的固定字段值（兜底在 ext 之后 TryAdd）
        var r = new WeighRecord
        {
            SerialNo = "S1",
            ExtJson = WeighRecord.ExtToJson(new Dictionary<string, string>
            {
                ["单价"] = "235.5", ["地主姓名"] = "张三", ["司机电话"] = "13911111111",
            }),
        };
        var ph = PrintTemplate.BuildPlaceholders(r, new AppConfig());

        Assert.Equal("235.5", ph["单价"]);
        Assert.Equal("张三", ph["地主姓名"]);
        Assert.Equal("13911111111", ph["司机电话"]);
    }

    [Fact]
    public void CreateDefault_纸张合法且含元素()
    {
        var t = PrintTemplate.CreateDefault();

        Assert.NotEmpty(t.Elements);
        Assert.Contains(t.PaperKey, PoundTicketPrinter.Papers.Select(p => p.Key));
    }

    [Fact]
    public void 模板JSON序列化往返_元素类型与多态标记保留()
    {
        var t = PrintTemplate.CreateDefault();
        t.Elements.Add(new CZRWeighSystem.Report.ImageElement { Path = "logo.png" });

        var json = JsonSerializer.Serialize(t);
        var back = JsonSerializer.Deserialize<PrintTemplate>(json)!;

        Assert.Equal(t.Elements.Count, back.Elements.Count);
        Assert.Equal(t.PaperKey, back.PaperKey);
        // 各元素类型按顺序保留（$type 多态标记有效）
        for (int i = 0; i < t.Elements.Count; i++)
            Assert.Equal(t.Elements[i].GetType(), back.Elements[i].GetType());
    }
}

/// <summary>占位符文本替换测试。</summary>
public class PlaceholderExpandTests
{
    private static Dictionary<string, string> Ph => new()
    {
        ["车号"] = "冀A12345",
        ["毛重"] = "34600",
        ["一次磅时间"] = "2026-09-03 08:30:00",
    };

    [Fact]
    public void Expand_替换已知占位符()
    {
        Assert.Equal("车号：冀A12345", PrintTemplate.Expand("车号：{车号}", Ph));
        Assert.Equal("冀A12345 34600", PrintTemplate.Expand("{车号} {毛重}", Ph));
    }

    [Fact]
    public void Expand_未知占位符原样保留()
    {
        Assert.Equal("{不存在的字段}", PrintTemplate.Expand("{不存在的字段}", Ph));
        Assert.Equal("x{}y", PrintTemplate.Expand("x{}y", Ph));
    }

    [Fact]
    public void Expand_多个占位符与普通文本混合()
    {
        var s = PrintTemplate.Expand("车号 {车号} 毛重 {毛重} 吨", Ph);
        Assert.Equal("车号 冀A12345 毛重 34600 吨", s);
    }
}
