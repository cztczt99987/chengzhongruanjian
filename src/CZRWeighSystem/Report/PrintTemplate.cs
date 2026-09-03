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

    /// <summary>模板分类（内置模板按行业分类：通用/物流/仓储/矿山；模板库按此筛选）</summary>
    public string Category { get; set; } = "通用";

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
        "公司名称", "流水号", "磅号", "车号", "货物", "发货单位", "收货单位", "规格", "批次",
        "毛重", "皮重", "净重", "扣重", "实收", "单位",
        "一次磅时间", "二次磅时间", "打印时间", "司磅员", "备注", "状态",
    ];

    /// <summary>按称重记录与配置生成占位符取值表（含自定义字段）。</summary>
    public static Dictionary<string, string> BuildPlaceholders(WeighRecord r, AppConfig cfg)
    {
        var ph = new Dictionary<string, string>
        {
            ["公司名称"] = cfg.CompanyName,
            ["流水号"] = r.SerialNo,
            ["磅号"] = r.ScaleNo,
            ["车号"] = r.VehicleNo,
            ["货物"] = r.Goods ?? "-",
            ["发货单位"] = r.SenderCompany ?? "-",
            ["收货单位"] = r.ReceiverCompany ?? "-",
            ["规格"] = r.Spec ?? "-",
            ["批次"] = r.BatchNo ?? "-",
            ["毛重"] = FmtKg(r.GrossKg, cfg),
            ["皮重"] = FmtKg(r.TareKg, cfg),
            ["净重"] = FmtKg(r.NetKg, cfg),
            ["扣重"] = FmtKg(r.DeductKg, cfg),
            ["实收"] = FmtKg(r.ReceivedKg, cfg),
            ["单位"] = cfg.Unit,
            ["一次磅时间"] = r.FirstTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "-",
            ["二次磅时间"] = r.SecondTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "-",
            ["打印时间"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            ["司磅员"] = r.Operator ?? "-",
            ["备注"] = r.IsManual ? "手工补录" : (r.Remark ?? ""),
            ["状态"] = r.Status,
        };
        // 自定义字段名即占位符（如模板中写 {合同号}）
        foreach (var (name, val) in r.ParseExt())
            ph.TryAdd(name, val);
        return ph;
    }

    /// <summary>
    /// 生成类型化原值表（占位符名 → 原始值）。
    /// 重量字段按配置单位换算后的 double?；时间字段为 DateTime?；供 {字段:格式} 格式化使用。
    /// </summary>
    public static Dictionary<string, object?> BuildRawValues(WeighRecord r, AppConfig cfg)
    {
        double? Kg(double? kg) => kg.HasValue
            ? (cfg.Unit == "t" ? kg.Value / 1000.0 : kg.Value) : null;
        return new Dictionary<string, object?>
        {
            ["毛重"] = Kg(r.GrossKg),
            ["皮重"] = Kg(r.TareKg),
            ["净重"] = Kg(r.NetKg),
            ["扣重"] = Kg(r.DeductKg),
            ["实收"] = Kg(r.ReceivedKg),
            ["一次磅时间"] = r.FirstTime,
            ["二次磅时间"] = r.SecondTime,
            ["打印时间"] = DateTime.Now,
        };
    }

    /// <summary>
    /// 替换文本中的 {占位符}；未识别的占位符原样保留。
    /// 支持格式后缀：{毛重:F1}、{一次磅时间:yyyy-MM-dd}——带格式时从 <paramref name="raw"/>
    /// 取类型化原值按格式输出；原值缺失或格式非法时回退为默认字符串值。
    /// </summary>
    public static string Expand(string text, Dictionary<string, string> ph,
        Dictionary<string, object?>? raw = null)
        => System.Text.RegularExpressions.Regex.Replace(text, @"\{([^{}:]+)(?::([^{}]+))?\}", m =>
        {
            string name = m.Groups[1].Value;
            string fmt = m.Groups[2].Success ? m.Groups[2].Value : "";
            if (fmt.Length == 0)
                return ph.TryGetValue(name, out var v) ? v : m.Value;
            if (raw is null || !raw.TryGetValue(name, out var rv) || rv is null)
                return ph.TryGetValue(name, out var v2) ? v2 : m.Value;
            try
            {
                return rv switch
                {
                    // 数字格式仅允许数字格式字符，含其他字符（如中文说明）视为非法回退
                    double d => IsNumericFormat(fmt) ? d.ToString(fmt) : Fallback(ph, name, m.Value),
                    DateTime dt => dt.ToString(fmt),
                    _ => rv.ToString() ?? "",
                };
            }
            catch (FormatException)
            {
                return ph.TryGetValue(name, out var v3) ? v3 : m.Value;
            }
        });

    /// <summary>数字格式合法性：允许标准格式符（C/D/E/F/G/N/P/R 大小写）与自定义格式字符（0 # . , % e E + -）。</summary>
    private static bool IsNumericFormat(string fmt)
        => fmt.All(c => "0123456789#.,%eE+-CFGENPRcfgenpr".Contains(c));

    /// <summary>格式化回退：取默认字符串值，无则保留原文本。</summary>
    private static string Fallback(Dictionary<string, string> ph, string name, string original)
        => ph.TryGetValue(name, out var v) ? v : original;

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
        t.Elements.Add(new TextElement { Text = "发货单位：{发货单位}", X = 15, Y = 54, W = 45, H = 8 });
        t.Elements.Add(new TextElement { Text = "收货单位：{收货单位}", X = 65, Y = 54, W = 45, H = 8 });
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

    // ---------- 内置模板库 ----------

    /// <summary>内置模板文件名（内置模板固定以"内置_"前缀命名，便于模板库识别）</summary>
    public static string BuiltinFileName(PrintTemplate t) => $"内置_{t.Name}.json";

    /// <summary>是否为内置模板文件</summary>
    public static bool IsBuiltinFileName(string fileName) => fileName.StartsWith("内置_");

    /// <summary>创建 5 种内置行业模板（每次调用返回新实例）。</summary>
    public static List<PrintTemplate> CreateBuiltins()
    {
        var list = new List<PrintTemplate> { Builtin通用A4(), Builtin物流运输(), Builtin仓储出入库(), Builtin矿山销售(), Builtin针打连单() };
        return list;
    }

    /// <summary>把缺失的内置模板写入 templates/ 目录（已存在的不覆盖，保留用户改动）。</summary>
    public static void EnsureBuiltinsOnDisk()
    {
        foreach (var t in CreateBuiltins())
        {
            string file = BuiltinFileName(t);
            if (!File.Exists(PathOf(file)))
                t.Save(file);
        }
    }

    /// <summary>内置模板 1：通用A4磅单（通用/A4）。</summary>
    private static PrintTemplate Builtin通用A4()
    {
        var t = new PrintTemplate { Name = "通用A4磅单", Category = "通用", PaperKey = "A4" };
        t.Elements.Add(new TextElement { Text = "磅  单", X = 75, Y = 10, W = 60, H = 12, FontPt = 20, Bold = true, Align = TextAlign.Center });
        t.Elements.Add(new QRCodeElement { Content = "{流水号}", X = 168, Y = 8, W = 24, H = 24 });
        t.Elements.Add(new TextElement { Text = "{公司名称}", X = 15, Y = 26, W = 90, H = 8 });
        t.Elements.Add(new TextElement { Text = "No. {流水号}", X = 110, Y = 26, W = 55, H = 8, Align = TextAlign.Right });
        t.Elements.Add(new TextElement { Text = "车号：{车号}", X = 15, Y = 42, W = 85, H = 8 });
        t.Elements.Add(new TextElement { Text = "货物：{货物}", X = 105, Y = 42, W = 90, H = 8 });
        t.Elements.Add(new TextElement { Text = "发货单位：{发货单位}", X = 15, Y = 52, W = 45, H = 8 });
        t.Elements.Add(new TextElement { Text = "收货单位：{收货单位}", X = 65, Y = 52, W = 45, H = 8 });
        t.Elements.Add(new TextElement { Text = "规格：{规格}", X = 110, Y = 52, W = 40, H = 8 });
        t.Elements.Add(new TextElement { Text = "批次：{批次}", X = 152, Y = 52, W = 43, H = 8 });
        t.Elements.Add(new RectElement { X = 15, Y = 64, W = 180, H = 18 });
        t.Elements.Add(new TextElement { Text = "毛重：{毛重} {单位}", X = 20, Y = 68, W = 55, H = 8, FontPt = 13, Bold = true });
        t.Elements.Add(new TextElement { Text = "皮重：{皮重} {单位}", X = 80, Y = 68, W = 55, H = 8, FontPt = 13, Bold = true });
        t.Elements.Add(new TextElement { Text = "净重：{净重} {单位}", X = 140, Y = 68, W = 50, H = 8, FontPt = 13, Bold = true });
        t.Elements.Add(new TextElement { Text = "扣重：{扣重} {单位}　实收：{实收} {单位}", X = 20, Y = 75.5f, W = 120, H = 5, FontPt = 10 });
        t.Elements.Add(new TextElement { Text = "一次磅时间：{一次磅时间}", X = 15, Y = 88, W = 95, H = 8 });
        t.Elements.Add(new TextElement { Text = "备注：{备注}", X = 112, Y = 88, W = 83, H = 8 });
        t.Elements.Add(new TextElement { Text = "二次磅时间：{二次磅时间}", X = 15, Y = 97, W = 95, H = 8 });
        t.Elements.Add(new TextElement { Text = "司磅员：{司磅员}", X = 112, Y = 97, W = 83, H = 8 });
        t.Elements.Add(new LineElement { X = 15, Y = 110, W = 180, H = 0 });
        t.Elements.Add(new SignatureElement { Labels = ["司磅员签字", "客户签字"], X = 15, Y = 114, W = 180, H = 10 });
        t.Elements.Add(new TextElement { Text = "本单据由 CZR 智能称重管理系统生成", X = 15, Y = 128, W = 120, H = 5, FontPt = 8, ForeColor = "#999999" });
        return t;
    }

    /// <summary>内置模板 2：物流运输磅单（物流/A4）。</summary>
    private static PrintTemplate Builtin物流运输()
    {
        var t = new PrintTemplate { Name = "物流运输磅单", Category = "物流", PaperKey = "A4" };
        t.Elements.Add(new TextElement { Text = "物流运输磅单", X = 45, Y = 10, W = 120, H = 12, FontPt = 20, Bold = true, Align = TextAlign.Center });
        t.Elements.Add(new TextElement { Text = "{公司名称}", X = 15, Y = 26, W = 90, H = 8 });
        t.Elements.Add(new TextElement { Text = "No. {流水号}", X = 120, Y = 26, W = 75, H = 8, Align = TextAlign.Right });
        t.Elements.Add(new TableElement
        {
            X = 15, Y = 38, W = 180, RowH = 8,
            Columns =
            [
                new() { Header = "车号", Text = "{车号}", Width = 50 },
                new() { Header = "货物", Text = "{货物}", Width = 50 },
                new() { Header = "规格", Text = "{规格}", Width = 40 },
                new() { Header = "批次", Text = "{批次}", Width = 40 },
            ],
        });
        t.Elements.Add(new QRCodeElement { Content = "{流水号}", X = 172, Y = 60, W = 23, H = 23 });
        t.Elements.Add(new TextElement { Text = "发货单位：{发货单位}", X = 15, Y = 58, W = 90, H = 8 });
        t.Elements.Add(new TextElement { Text = "收货单位：{收货单位}", X = 15, Y = 66, W = 90, H = 8 });
        t.Elements.Add(new RectElement { X = 15, Y = 78, W = 180, H = 18 });
        t.Elements.Add(new TextElement { Text = "毛重：{毛重:F1} {单位}", X = 20, Y = 82, W = 55, H = 8, FontPt = 13, Bold = true });
        t.Elements.Add(new TextElement { Text = "皮重：{皮重:F1} {单位}", X = 80, Y = 82, W = 55, H = 8, FontPt = 13, Bold = true });
        t.Elements.Add(new TextElement { Text = "净重：{净重:F1} {单位}", X = 140, Y = 82, W = 50, H = 8, FontPt = 13, Bold = true });
        t.Elements.Add(new TextElement { Text = "扣重：{扣重} {单位}　实收：{实收:F1} {单位}", X = 20, Y = 89.5f, W = 130, H = 5, FontPt = 10 });
        t.Elements.Add(new TextElement { Text = "一次磅时间：{一次磅时间}", X = 15, Y = 102, W = 95, H = 8 });
        t.Elements.Add(new TextElement { Text = "二次磅时间：{二次磅时间}", X = 15, Y = 111, W = 95, H = 8 });
        t.Elements.Add(new TextElement { Text = "司磅员：{司磅员}", X = 112, Y = 102, W = 55, H = 8 });
        t.Elements.Add(new BarcodeElement { Content = "{流水号}", X = 15, Y = 124, W = 80, H = 14 });
        t.Elements.Add(new SignatureElement { Labels = ["司磅员", "司机", "客户签字"], X = 15, Y = 144, W = 180, H = 10 });
        t.Elements.Add(new TextElement { Text = "本单据由 CZR 智能称重管理系统生成", X = 15, Y = 158, W = 120, H = 5, FontPt = 8, ForeColor = "#999999" });
        return t;
    }

    /// <summary>内置模板 3：仓储出入库磅单（仓储/A4）。</summary>
    private static PrintTemplate Builtin仓储出入库()
    {
        var t = new PrintTemplate { Name = "仓储出入库磅单", Category = "仓储", PaperKey = "A4" };
        t.Elements.Add(new TextElement { Text = "仓储出入库磅单", X = 45, Y = 10, W = 120, H = 12, FontPt = 20, Bold = true, Align = TextAlign.Center });
        t.Elements.Add(new TextElement { Text = "{公司名称}", X = 15, Y = 26, W = 90, H = 8 });
        t.Elements.Add(new TextElement { Text = "打印时间：{打印时间:yyyy-MM-dd HH:mm}", X = 15, Y = 34, W = 100, H = 6 });
        t.Elements.Add(new TextElement { Text = "No. {流水号}", X = 120, Y = 26, W = 75, H = 8, Align = TextAlign.Right });
        t.Elements.Add(new TableElement
        {
            X = 15, Y = 44, W = 180, RowH = 8,
            Columns =
            [
                new() { Header = "车号", Text = "{车号}", Width = 35 },
                new() { Header = "货物", Text = "{货物}", Width = 30 },
                new() { Header = "规格", Text = "{规格}", Width = 25 },
                new() { Header = "毛重（{单位}）", Text = "{毛重:F1}", Width = 30 },
                new() { Header = "皮重（{单位}）", Text = "{皮重:F1}", Width = 30 },
                new() { Header = "净重（{单位}）", Text = "{净重:F1}", Width = 30 },
            ],
        });
        t.Elements.Add(new TextElement { Text = "扣重：{扣重} {单位}　实收：{实收:F1} {单位}", X = 15, Y = 64, W = 120, H = 8, FontPt = 11 });
        t.Elements.Add(new RectElement { X = 15, Y = 76, W = 180, H = 16 });
        t.Elements.Add(new TextElement { Text = "备注：{备注}", X = 20, Y = 82, W = 170, H = 6 });
        t.Elements.Add(new BarcodeElement { Content = "{流水号}", X = 15, Y = 98, W = 80, H = 14 });
        t.Elements.Add(new SignatureElement { Labels = ["仓管员", "司机", "库管"], X = 15, Y = 118, W = 180, H = 10 });
        t.Elements.Add(new TextElement { Text = "本单据由 CZR 智能称重管理系统生成", X = 15, Y = 132, W = 120, H = 5, FontPt = 8, ForeColor = "#999999" });
        return t;
    }

    /// <summary>内置模板 4：矿山销售磅单（矿山/A4）。</summary>
    private static PrintTemplate Builtin矿山销售()
    {
        var t = new PrintTemplate { Name = "矿山销售磅单", Category = "矿山", PaperKey = "A4" };
        t.Elements.Add(new TextElement { Text = "矿山销售磅单", X = 45, Y = 10, W = 120, H = 12, FontPt = 20, Bold = true, Align = TextAlign.Center });
        t.Elements.Add(new TextElement { Text = "矿点：{磅号}", X = 15, Y = 26, W = 60, H = 8 });
        t.Elements.Add(new TextElement { Text = "No. {流水号}", X = 120, Y = 26, W = 75, H = 8, Align = TextAlign.Right });
        t.Elements.Add(new TextElement { Text = "车号：{车号}", X = 15, Y = 38, W = 60, H = 8 });
        t.Elements.Add(new TextElement { Text = "货物：{货物}", X = 80, Y = 38, W = 60, H = 8 });
        t.Elements.Add(new TextElement { Text = "规格：{规格}", X = 145, Y = 38, W = 50, H = 8 });
        t.Elements.Add(new TableElement
        {
            X = 15, Y = 50, W = 180, RowH = 8,
            Columns =
            [
                new() { Header = "发货单位", Text = "{发货单位}", Width = 60 },
                new() { Header = "收货单位", Text = "{收货单位}", Width = 60 },
                new() { Header = "批次", Text = "{批次}", Width = 60 },
            ],
        });
        t.Elements.Add(new TextElement { Text = "净重：{净重:F1} {单位}", X = 15, Y = 72, W = 180, H = 14, FontPt = 22, Bold = true, Align = TextAlign.Center, ForeColor = "#CC0000" });
        t.Elements.Add(new TextElement { Text = "毛重：{毛重:F1} {单位}　　皮重：{皮重:F1} {单位}", X = 15, Y = 90, W = 180, H = 8, FontPt = 11, Align = TextAlign.Center });
        t.Elements.Add(new TextElement { Text = "扣重：{扣重} {单位}　　实收：{实收:F1} {单位}", X = 15, Y = 99, W = 180, H = 8, FontPt = 11, Align = TextAlign.Center });
        t.Elements.Add(new TextElement { Text = "一次磅时间：{一次磅时间}", X = 15, Y = 112, W = 95, H = 8 });
        t.Elements.Add(new TextElement { Text = "二次磅时间：{二次磅时间}", X = 15, Y = 121, W = 95, H = 8 });
        t.Elements.Add(new BarcodeElement { Content = "{流水号}", X = 118, Y = 112, W = 77, H = 14 });
        t.Elements.Add(new SignatureElement { Labels = ["司磅员", "购货方签字"], X = 15, Y = 134, W = 180, H = 10 });
        t.Elements.Add(new TextElement { Text = "备注：{备注}", X = 15, Y = 148, W = 170, H = 8 });
        return t;
    }

    /// <summary>内置模板 5：针打连单（物流/241x140，紧凑两列版式）。</summary>
    private static PrintTemplate Builtin针打连单()
    {
        var t = new PrintTemplate { Name = "针打连单", Category = "物流", PaperKey = "241x140" };
        t.Elements.Add(new BarcodeElement { Content = "{流水号}", X = 10, Y = 8, W = 60, H = 12 });
        t.Elements.Add(new TextElement { Text = "磅  单", X = 90, Y = 6, W = 60, H = 10, FontPt = 16, Bold = true, Align = TextAlign.Center });
        t.Elements.Add(new TextElement { Text = "No. {流水号}", X = 160, Y = 8, W = 71, H = 8, Align = TextAlign.Right });
        t.Elements.Add(new TextElement { Text = "车号：{车号}", X = 10, Y = 26, W = 72, H = 7 });
        t.Elements.Add(new TextElement { Text = "货物：{货物}", X = 85, Y = 26, W = 72, H = 7 });
        t.Elements.Add(new TextElement { Text = "规格：{规格}", X = 160, Y = 26, W = 71, H = 7 });
        t.Elements.Add(new TextElement { Text = "毛重：{毛重} {单位}", X = 10, Y = 36, W = 72, H = 8, FontPt = 12, Bold = true });
        t.Elements.Add(new TextElement { Text = "皮重：{皮重} {单位}", X = 85, Y = 36, W = 72, H = 8, FontPt = 12, Bold = true });
        t.Elements.Add(new TextElement { Text = "净重：{净重} {单位}", X = 160, Y = 36, W = 71, H = 8, FontPt = 12, Bold = true });
        t.Elements.Add(new TextElement { Text = "扣重：{扣重} {单位}", X = 10, Y = 47, W = 72, H = 7 });
        t.Elements.Add(new TextElement { Text = "实收：{实收} {单位}", X = 85, Y = 47, W = 72, H = 7 });
        t.Elements.Add(new TableElement
        {
            X = 10, Y = 57, W = 221, RowH = 7,
            Columns =
            [
                new() { Header = "发货单位", Text = "{发货单位}", Width = 110 },
                new() { Header = "收货单位", Text = "{收货单位}", Width = 111 },
            ],
        });
        t.Elements.Add(new TextElement { Text = "一次磅时间：{一次磅时间}", X = 10, Y = 74, W = 110, H = 7 });
        t.Elements.Add(new TextElement { Text = "二次磅时间：{二次磅时间}", X = 10, Y = 82, W = 110, H = 7 });
        t.Elements.Add(new SignatureElement { Labels = ["司磅员", "客户签字"], X = 10, Y = 92, W = 221, H = 8 });
        t.Elements.Add(new TextElement { Text = "打印时间：{打印时间}", X = 10, Y = 104, W = 110, H = 7 });
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
[JsonDerivedType(typeof(TableElement), "table")]
[JsonDerivedType(typeof(SignatureElement), "signature")]
[JsonDerivedType(typeof(QRCodeElement), "qrcode")]
[JsonDerivedType(typeof(BarcodeElement), "barcode")]
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

    [Category("内容"), DisplayName("文字颜色（#RRGGBB 或颜色名）")]
    public string ForeColor { get; set; } = "#000000";

    [Browsable(false)]
    public override string Desc => $"文本：{Text}";
}

/// <summary>线条元素：从 (X,Y) 到 (X+W, Y+H) 的线段；H=0 即水平线</summary>
public class LineElement : TemplateElement
{
    [Category("外观"), DisplayName("线宽（毫米）")]
    public float Thick { get; set; } = 0.3f;

    [Category("外观"), DisplayName("颜色（#RRGGBB 或颜色名）")]
    public string Color { get; set; } = "#000000";

    [Browsable(false)]
    public override string Desc => $"线条（长 {W:0.#}mm）";
}

/// <summary>矩形框元素</summary>
public class RectElement : TemplateElement
{
    [Category("外观"), DisplayName("线宽（毫米）")]
    public float Thick { get; set; } = 0.3f;

    [Category("外观"), DisplayName("边框颜色（#RRGGBB 或颜色名）")]
    public string Color { get; set; } = "#000000";

    /// <summary>填充色（null = 不填充，仅边框）</summary>
    [Category("外观"), DisplayName("填充色（留空不填充）")]
    public string? FillColor { get; set; }

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

/// <summary>表格列定义（表头文字 + 数据行内容，内容支持 {占位符}）</summary>
public class TableColumn
{
    [Category("内容"), DisplayName("列头")]
    public string Header { get; set; } = "列";

    [Category("内容"), DisplayName("内容（支持 {占位符}）")]
    public string Text { get; set; } = "";

    [Category("外观"), DisplayName("列宽（毫米）")]
    public float Width { get; set; } = 30;

    [Category("外观"), DisplayName("对齐")]
    public TextAlign Align { get; set; } = TextAlign.Center;
}

/// <summary>
/// 表格元素：表头行 + 1 个数据行（列内容支持 {占位符}，行高 RowH）。
/// 各列按列宽比例分摊元素总宽 W。
/// </summary>
public class TableElement : TemplateElement
{
    [Category("外观"), DisplayName("行高（毫米）")]
    public float RowH { get; set; } = 8;

    [Category("内容"), DisplayName("列集合")]
    public List<TableColumn> Columns { get; set; } = [];

    [Browsable(false)]
    public override string Desc => $"表格（{Columns.Count} 列）";
}

/// <summary>签名区元素：各标签均分宽度，绘制"标签：______"下划线。</summary>
public class SignatureElement : TemplateElement
{
    [Category("内容"), DisplayName("签名标签（分号分隔）")]
    public List<string> Labels { get; set; } = ["司磅员", "客户签字"];

    [Category("内容"), DisplayName("字号（磅）")]
    public float FontPt { get; set; } = 10;

    [Browsable(false)]
    public override string Desc => $"签名区：{string.Join("/", Labels)}";
}

/// <summary>二维码元素：内容支持 {占位符}（QRCoder 生成）。</summary>
public class QRCodeElement : TemplateElement
{
    [Category("内容"), DisplayName("内容（支持 {占位符}）")]
    public string Content { get; set; } = "{流水号}";

    [Browsable(false)]
    public override string Desc => $"二维码：{Content}";
}

/// <summary>条形码元素（Code39）：内容支持 {占位符}。</summary>
public class BarcodeElement : TemplateElement
{
    [Category("内容"), DisplayName("内容（支持 {占位符}）")]
    public string Content { get; set; } = "{流水号}";

    [Category("外观"), DisplayName("显示内容文本")]
    public bool ShowText { get; set; } = true;

    [Browsable(false)]
    public override string Desc => $"条码：{Content}";
}

/// <summary>文本对齐方式（在元素宽度内）</summary>
public enum TextAlign
{
    Left,
    Center,
    Right,
}
