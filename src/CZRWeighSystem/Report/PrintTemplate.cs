using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using CZRWeighSystem.Core;

namespace CZRWeighSystem.Report;

/// <summary>
/// 磅单打印模板（二期）：纸张 + 元素列表，元素坐标单位为毫米（页面左上角为原点）。
/// JSON 序列化保存于 templates/ 目录；在打印设置中选择启用，
/// 启用后纸张与版式由模板决定，联数/自动打印仍取全局打印设置。
/// </summary>
public class PrintTemplate
{
    /// <summary>纸张 key（见 PoundTicketPrinter.Papers 目录）</summary>
    public string PaperKey { get; set; } = "A4";

    /// <summary>模板名称（展示用）</summary>
    public string Name { get; set; } = "自定义模板";

    /// <summary>元素列表；绘制顺序即列表顺序，末尾元素在最上层</summary>
    public List<TemplateElement> Elements { get; set; } = [];

    // ---------- 文件存取 ----------

    /// <summary>模板目录（exe 下 templates/）</summary>
    public static string TemplatesDir => Path.Combine(AppContext.BaseDirectory, "templates");

    /// <summary>解析模板路径：相对名按 templates/ 目录，绝对路径原样返回</summary>
    public static string PathOf(string fileName) =>
        Path.IsPathRooted(fileName) ? fileName : Path.Combine(TemplatesDir, fileName);

    /// <summary>列出模板目录下全部模板文件名</summary>
    public static List<string> ListNames()
        => Directory.Exists(TemplatesDir)
            ? [.. Directory.GetFiles(TemplatesDir, "*.json").Select(Path.GetFileName)!]
            : [];

    /// <summary>从 templates/ 目录（或绝对路径）加载模板</summary>
    public static PrintTemplate Load(string fileName)
    {
        var json = File.ReadAllText(PathOf(fileName));
        return JsonSerializer.Deserialize<PrintTemplate>(json)
            ?? throw new InvalidOperationException("模板文件内容为空");
    }

    /// <summary>保存模板到 templates/ 目录（或绝对路径）</summary>
    public void Save(string fileName)
    {
        Directory.CreateDirectory(TemplatesDir);
        var options = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(PathOf(fileName), JsonSerializer.Serialize(this, options));
    }

    // ---------- 占位符 ----------

    /// <summary>可用占位符名（模板设计器"插入字段"用）</summary>
    public static readonly string[] PlaceholderNames =
    [
        "公司名称", "流水号", "磅号", "车号", "货物", "收发货单位", "规格", "批次",
        "毛重", "皮重", "净重", "扣重", "实收", "单位",
        "一次磅时间", "二次磅时间", "打印时间", "司磅员", "备注", "状态",
    ];

    /// <summary>按称重记录与配置生成占位符取值表</summary>
    public static Dictionary<string, string> BuildPlaceholders(WeighRecord r, AppConfig cfg) => new()
    {
        ["公司名称"] = cfg.CompanyName,
        ["流水号"] = r.SerialNo,
        ["磅号"] = r.ScaleNo,
        ["车号"] = r.VehicleNo,
        ["货物"] = r.Goods ?? "-",
        ["收发货单位"] = r.Company ?? "-",
        ["规格"] = r.Spec ?? "-",
        ["批次"] = r.BatchNo ?? "-",
        ["毛重"] = FmtKg(r.GrossKg, cfg),
        ["皮重"] = FmtKg(r.TareKg, cfg),
        ["净重"] = FmtKg(r.NetKg, cfg),
        ["扣重"] = FmtKg(0, cfg),
        ["实收"] = FmtKg(r.NetKg, cfg),
        ["单位"] = cfg.Unit,
        ["一次磅时间"] = r.FirstTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "-",
        ["二次磅时间"] = r.SecondTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "-",
        ["打印时间"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
        ["司磅员"] = r.Operator ?? "-",
        ["备注"] = r.IsManual ? "手工补录" : (r.Remark ?? ""),
        ["状态"] = r.Status,
    };

    /// <summary>替换文本中的 {占位符}；未识别的占位符原样保留</summary>
    public static string Expand(string text, Dictionary<string, string> ph)
        => System.Text.RegularExpressions.Regex.Replace(text, @"\{([^{}]+)\}",
            m => ph.TryGetValue(m.Groups[1].Value, out var v) ? v : m.Value);

    private static string FmtKg(double? kg, AppConfig cfg)
    {
        if (!kg.HasValue) return "-";
        double v = cfg.Unit == "t" ? kg.Value / 1000.0 : kg.Value;
        return v.ToString("0.###");
    }

    /// <summary>内置默认模板（A4 单据版式，可另存后修改）</summary>
    public static PrintTemplate CreateDefault(string paperKey = "A4")
    {
        var t = new PrintTemplate { PaperKey = paperKey, Name = "内置默认" };
        t.Elements.Add(new TextElement { Text = "磅  单", X = 75, Y = 12, W = 60, H = 12, FontPt = 20, Bold = true, Align = TextAlign.Center });
        t.Elements.Add(new TextElement { Text = "{公司名称}", X = 15, Y = 28, W = 90, H = 8 });
        t.Elements.Add(new TextElement { Text = "No. {流水号}", X = 130, Y = 28, W = 65, H = 8, Align = TextAlign.Right });
        t.Elements.Add(new TextElement { Text = "车号：{车号}", X = 15, Y = 44, W = 85, H = 8 });
        t.Elements.Add(new TextElement { Text = "货物：{货物}", X = 105, Y = 44, W = 90, H = 8 });
        t.Elements.Add(new TextElement { Text = "收发货单位：{收发货单位}", X = 15, Y = 54, W = 95, H = 8 });
        t.Elements.Add(new TextElement { Text = "规格：{规格}", X = 110, Y = 54, W = 40, H = 8 });
        t.Elements.Add(new TextElement { Text = "批次：{批次}", X = 152, Y = 54, W = 43, H = 8 });
        t.Elements.Add(new RectElement { X = 15, Y = 66, W = 180, H = 18 });
        t.Elements.Add(new TextElement { Text = "毛重：{毛重} {单位}", X = 20, Y = 70, W = 55, H = 8, FontPt = 13, Bold = true });
        t.Elements.Add(new TextElement { Text = "皮重：{皮重} {单位}", X = 80, Y = 70, W = 55, H = 8, FontPt = 13, Bold = true });
        t.Elements.Add(new TextElement { Text = "净重：{净重} {单位}", X = 140, Y = 70, W = 50, H = 8, FontPt = 13, Bold = true });
        t.Elements.Add(new TextElement { Text = "扣重：{扣重} {单位}　实收：{实收} {单位}", X = 20, Y = 77.5f, W = 120, H = 5, FontPt = 10 });
        t.Elements.Add(new TextElement { Text = "一次磅时间：{一次磅时间}", X = 15, Y = 90, W = 95, H = 8 });
        t.Elements.Add(new TextElement { Text = "备注：{备注}", X = 112, Y = 90, W = 83, H = 8 });
        t.Elements.Add(new TextElement { Text = "二次磅时间：{二次磅时间}", X = 15, Y = 99, W = 95, H = 8 });
        t.Elements.Add(new TextElement { Text = "司磅员：{司磅员}", X = 112, Y = 99, W = 83, H = 8 });
        t.Elements.Add(new LineElement { X = 15, Y = 112, W = 180, H = 0 });
        t.Elements.Add(new TextElement { Text = "司磅员签字：____________　　客户签字：____________", X = 15, Y = 116, W = 180, H = 8 });
        return t;
    }
}

// ==================== 元素模型 ====================

/// <summary>模板元素基类：位置尺寸单位毫米</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(TextElement), "text")]
[JsonDerivedType(typeof(LineElement), "line")]
[JsonDerivedType(typeof(RectElement), "rect")]
[JsonDerivedType(typeof(ImageElement), "image")]
public abstract class TemplateElement
{
    [Category("位置"), DisplayName("X（毫米）")]
    public float X { get; set; }

    [Category("位置"), DisplayName("Y（毫米）")]
    public float Y { get; set; }

    [Category("尺寸"), DisplayName("宽（毫米）")]
    public float W { get; set; } = 40;

    [Category("尺寸"), DisplayName("高（毫米）")]
    public float H { get; set; } = 8;

    /// <summary>元素列表展示文本</summary>
    [Browsable(false)]
    public abstract string Desc { get; }
}

/// <summary>文本元素：内容支持 {占位符}</summary>
public class TextElement : TemplateElement
{
    [Category("内容"), DisplayName("文本（支持 {占位符}）")]
    public string Text { get; set; } = "文本";

    [Category("内容"), DisplayName("字号（磅）")]
    public float FontPt { get; set; } = 12;

    [Category("内容"), DisplayName("加粗")]
    public bool Bold { get; set; }

    [Category("内容"), DisplayName("对齐")]
    public TextAlign Align { get; set; } = TextAlign.Left;

    [Browsable(false)]
    public override string Desc => $"文本：{Text}";
}

/// <summary>线条元素：从 (X,Y) 到 (X+W, Y+H) 的线段；H=0 即水平线</summary>
public class LineElement : TemplateElement
{
    [Category("外观"), DisplayName("线宽（毫米）")]
    public float Thick { get; set; } = 0.3f;

    [Browsable(false)]
    public override string Desc => $"线条（长 {W:0.#}mm）";
}

/// <summary>矩形框元素</summary>
public class RectElement : TemplateElement
{
    [Category("外观"), DisplayName("线宽（毫米）")]
    public float Thick { get; set; } = 0.3f;

    [Browsable(false)]
    public override string Desc => $"矩形框（{W:0.#}×{H:0.#}mm）";
}

/// <summary>图片元素（如公司 LOGO，支持 png/jpg/bmp）</summary>
public class ImageElement : TemplateElement
{
    [Category("内容"), DisplayName("图片路径")]
    public string Path { get; set; } = "";

    [Browsable(false)]
    public override string Desc => $"图片：{System.IO.Path.GetFileName(Path)}";
}

/// <summary>文本对齐方式（在元素宽度内）</summary>
public enum TextAlign
{
    Left,
    Center,
    Right,
}
