namespace CZRWeighSystem.Tests;

/// <summary>重量单位配置测试（默认吨、界面显示汉字"吨"、表格单元格纯数字）。</summary>
public class AppConfigUnitTests
{
    [Fact]
    public void 默认显示单位为吨()
    {
        Assert.Equal("t", new AppConfig().Unit);
        Assert.Equal("吨", new AppConfig().UnitDisplay);
    }

    [Fact]
    public void 单位显示文本_吨显示汉字_千克显示kg()
    {
        Assert.Equal("吨", new AppConfig { Unit = "t" }.UnitDisplay);
        Assert.Equal("kg", new AppConfig { Unit = "kg" }.UnitDisplay);
    }

    [Fact]
    public void FormatWeight_吨单位带汉字单位()
    {
        var cfg = new AppConfig { Unit = "t" };
        Assert.Equal("34.6 吨", cfg.FormatWeight(34600));
        Assert.Equal("0 吨", cfg.FormatWeight(0));
    }

    [Fact]
    public void FormatWeight_千克单位带kg()
    {
        var cfg = new AppConfig { Unit = "kg" };
        Assert.Equal("34600 kg", cfg.FormatWeight(34600));
    }

    [Fact]
    public void FormatWeightNumber_只返回数字不带单位()
    {
        Assert.Equal("34.6", new AppConfig { Unit = "t" }.FormatWeightNumber(34600));
        Assert.Equal("34600", new AppConfig { Unit = "kg" }.FormatWeightNumber(34600));
    }
}
