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
        "公司名称", "磅单抬头", "流水号", "磅号", "车号", "货物", "发货单位", "收货单位", "规格", "批次",
        "毛重", "皮重", "净重", "扣重", "实收", "金额", "单位",
        "一次磅时间", "二次磅时间", "打印时间", "司磅员", "备注", "状态",
    ];

    /// <summary>按称重记录与配置生成占位符取值表（含自定义字段）。</summary>
    public static Dictionary<string, string> BuildPlaceholders(WeighRecord r, AppConfig cfg)
    {
        var ph = new Dictionary<string, string>
        {
            ["公司名称"] = cfg.CompanyName,
            // 磅单类型行（综合设置的磅单抬头，如"过磅单"；老软件的"皮重过磅单/毛重过磅单"即此处配置）
            ["磅单抬头"] = string.IsNullOrWhiteSpace(cfg.SlipHeader) ? "过磅单" : cfg.SlipHeader,
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
            // 应收金额（元）：按收费分段自动计算；未启用收费/未结算为 "-"
            ["金额"] = r.AmountYuan.HasValue ? r.AmountYuan.Value.ToString("0.##") : "-",
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

    /// <summary>创建内置模板：标准磅单 / 金额磅单 两种样式 × A4、二等分（241×140）、
    /// 三等分（241×93）、竖联（120×140）四种纸样，版式仿老软件过磅单（公司名抬头 + 类型 +
    /// 字段网格：车牌/品种/地主/司机/货主 + 毛皮净），金额磅单额外含应收金额行。
    /// 纸样命名按行业惯例：二等分 = 241 宽 × 11 英寸高均分两份（每联 140mm），
    /// 三等分 = 均分三份（每联 93mm）。</summary>
    public static List<PrintTemplate> CreateBuiltins()
    {
        var list = new List<PrintTemplate>();
        foreach (var (paperKey, suffix, vertical, dense) in new[]
        {
            ("A4", "-A4", false, false),
            ("241x140", "-二等分", false, false),
            ("241x93", "-三等分", false, true),
            ("120x140", "-竖联", true, false),
        })
        {
            list.Add(BuildTicketTemplate($"标准磅单{suffix}", "标准", paperKey, vertical, dense, withAmount: false));
            list.Add(BuildTicketTemplate($"金额磅单{suffix}", "金额", paperKey, vertical, dense, withAmount: true));
        }
        return list;
    }

    /// <summary>把缺失的内置模板写入 templates/ 目录（已存在的不覆盖，保留用户改动）；
    /// 同时清理旧版内置模板；纸样命名修正（2026-10-02，二等分/三等分此前反了）后，
    /// 同名但 PaperKey 为旧映射的内置文件先删除重建，启用配置自动迁移到同尺寸新命名模板。</summary>
    public static void EnsureBuiltinsOnDisk()
    {
        var builtins = CreateBuiltins();

        // 旧纸样命名的内置文件（如"内置_标准磅单-二等分.json"内容为 241x93）：
        // 与新映射的 PaperKey 不符 → 删除（随后按新映射重建）；
        // 该文件正被启用时记住迁移目标 = "同纸样 + 同分类"的新命名模板
        string? migrateTo = null;
        foreach (var t in builtins)
        {
            string file = BuiltinFileName(t);
            string path = PathOf(file);
            if (!File.Exists(path)) continue;
            try
            {
                var existing = Load(file);
                if (existing.PaperKey == t.PaperKey) continue;   // 已是新映射，保留（含用户改动）
                File.Delete(path);
                if (AppConfig.Current.PrintTemplateFile == file)
                    migrateTo = BuiltinFileName(builtins.First(b =>
                        b.PaperKey == existing.PaperKey && b.Category == existing.Category));
            }
            catch (Exception ex)
            {
                Log.Error($"读取内置模板失败，按缺失重建：{file}", ex);
            }
        }

        foreach (var t in builtins)
        {
            string file = BuiltinFileName(t);
            if (!File.Exists(PathOf(file)))
                t.Save(file);
        }

        if (migrateTo != null)
        {
            AppConfig.Current.PrintTemplateFile = migrateTo;
            AppConfig.Current.Save();
            Log.Info($"纸样命名已修正（二等分=241×140、三等分=241×93），启用模板自动切换为：{migrateTo}");
        }

        // 旧版内置模板（2026-10 按老软件版式重做前）：文件删除，启用中则迁移
        string[] legacy =
        [
            "内置_通用A4磅单.json", "内置_物流运输磅单.json", "内置_仓储出入库磅单.json",
            "内置_矿山销售磅单.json", "内置_针打连单.json",
        ];
        bool currentRemoved = false;
        foreach (var legacyFile in legacy)
        {
            string path = PathOf(legacyFile);
            if (!File.Exists(path)) continue;
            currentRemoved |= AppConfig.Current.PrintTemplateFile == legacyFile;
            try { File.Delete(path); }
            catch (Exception ex) { Log.Error($"清理旧内置模板失败：{legacyFile}", ex); }
        }
        if (currentRemoved)
        {
            AppConfig.Current.PrintTemplateFile = BuiltinFileName(CreateBuiltins()[1]);   // 标准磅单-二等分
            AppConfig.Current.Save();
            Log.Info($"原启用模板已废弃，自动切换为：{AppConfig.Current.PrintTemplateFile}");
        }
    }

    /// <summary>
    /// 生成过磅单版式模板（仿老软件）：
    /// 公司名大字抬头 + 磅单类型行 + 日期/流水号 + 字段网格（车牌、品种、地主姓名/电话、
    /// 司机电话、货主姓名、毛皮净、金额）+ 磅重时间 + 签字栏。
    /// withAmount：金额磅单（含金额行，替代标准版的部分字段位）；vertical：竖联竖版布局；
    /// dense：三等分 241×93 紧凑布局。
    /// </summary>
    private static PrintTemplate BuildTicketTemplate(string name, string category, string paperKey,
        bool vertical, bool dense, bool withAmount)
    {
        var t = new PrintTemplate { Name = name, Category = category, PaperKey = paperKey };
        var (paperW, paperH) = PoundTicketPrinter.PaperSizeMm(paperKey);

        if (vertical)
        {
            // ---- 竖联（120×140 竖版带孔纸）：字段单列逐行排布 ----
            const float x0 = 4f, w = 112f;
            t.Elements.Add(new TextElement { Text = "{公司名称}", X = x0, Y = 5, W = w, H = 6, FontPt = 12, Bold = true, Align = TextAlign.Center });
            t.Elements.Add(new TextElement { Text = "{磅单抬头}", X = x0, Y = 12.5f, W = w, H = 5, FontPt = 10, Align = TextAlign.Center });
            t.Elements.Add(new TextElement { Text = "No. {流水号}", X = x0, Y = 18.5f, W = w, H = 5, FontPt = 9, Align = TextAlign.Center });
            t.Elements.Add(new TextElement { Text = "{打印时间:yyyy 年 M 月 d 日}", X = x0, Y = 24, W = w, H = 5, FontPt = 8, Align = TextAlign.Center });

            // 字段表：10 行（标签列 28mm + 值列 84mm）
            var rows = new List<(string Label, string Text)>
            {
                ("车牌号码", "{车号}"), ("品种", "{货物}"),
                ("地主姓名", "{地主姓名}"), ("地主电话", "{地主电话}"),
                ("司机电话", "{司机电话}"), ("货主姓名", "{货主姓名}"),
                ("毛重", "{毛重} {单位}"), ("皮重", "{皮重} {单位}"), ("净重", "{净重} {单位}"),
                (withAmount ? "金额(元)" : "状态", withAmount ? "{金额}" : "{状态}"),
            };
            const float gy0 = 30f, rowH0 = 8f, labelW0 = 30f;
            t.Elements.Add(new RectElement { X = x0, Y = gy0, W = w, H = rowH0 * rows.Count });
            for (int i = 1; i < rows.Count; i++)
                t.Elements.Add(new LineElement { X = x0, Y = gy0 + i * rowH0, W = w, H = 0 });
            t.Elements.Add(new LineElement { X = x0 + labelW0, Y = gy0, W = 0, H = rowH0 * rows.Count });
            for (int i = 0; i < rows.Count; i++)
            {
                float ry = gy0 + i * rowH0;
                t.Elements.Add(new TextElement { Text = rows[i].Label, X = x0 + 2, Y = ry + 2, W = labelW0 - 4, H = 5, FontPt = 9, Bold = true });
                t.Elements.Add(new TextElement { Text = rows[i].Text, X = x0 + labelW0 + 2, Y = ry + 2, W = w - labelW0 - 4, H = 5, FontPt = 9 });
            }

            t.Elements.Add(new TextElement { Text = "司磅员：{司磅员}", X = x0, Y = 112.5f, W = w, H = 5, FontPt = 9 });
            t.Elements.Add(new SignatureElement { Labels = ["司机签字", "客户签字"], X = x0, Y = 119, W = w, H = 9 });
            t.Elements.Add(new TextElement { Text = "*竖联磅单请使用 120×140mm 带孔磅单纸", X = x0, Y = 130.5f, W = w, H = 4, FontPt = 6.5f, ForeColor = "#888888" });
            _ = paperH;   // 纸高仅用于布局校验（元素坐标已控制在纸张内）
            return t;
        }

        // ---- 横版（241×140 二等分 / 241×93 三等分 / A4 整版）----
        float x = 8f, contentW = paperW - 16f;
        t.Elements.Add(new TextElement { Text = "{公司名称}", X = x, Y = dense ? 3 : 5, W = contentW, H = dense ? 7f : 8, FontPt = dense ? 14 : 16, Bold = true, Align = TextAlign.Center });
        t.Elements.Add(new TextElement { Text = "{磅单抬头}", X = x, Y = dense ? 10.5f : 15, W = contentW, H = dense ? 6f : 7, FontPt = dense ? 11 : 14, Align = TextAlign.Center });
        t.Elements.Add(new TextElement { Text = "{打印时间:yyyy 年 M 月 d 日}", X = x, Y = dense ? 17.5f : 24.5f, W = contentW / 2, H = 5, FontPt = 9 });
        t.Elements.Add(new TextElement { Text = "No. {流水号}", X = x + contentW / 2, Y = dense ? 17.5f : 24.5f, W = contentW / 2, H = 5, FontPt = 9, Align = TextAlign.Right });

        // 字段网格：6 行 × [标签|值|标签|值]；金额磅单把品种/司磅员/备注位让给金额与状态
        var left = withAmount
            ? new[] { ("车牌号码", "{车号}"), ("地主姓名", "{地主姓名}"), ("地主电话", "{地主电话}"),
                      ("司机电话", "{司机电话}"), ("货主姓名", "{货主姓名}"), ("司磅员", "{司磅员}") }
            : new[] { ("车牌号码", "{车号}"), ("地主姓名", "{地主姓名}"), ("地主电话", "{地主电话}"),
                      ("司机电话", "{司机电话}"), ("货主姓名", "{货主姓名}"), ("备注", "{备注}") };
        var right = withAmount
            ? new[] { ("毛重", "{毛重} {单位}"), ("皮重", "{皮重} {单位}"), ("净重", "{净重} {单位}"),
                      ("金额(元)", "{金额}"), ("品种", "{货物}"), ("状态", "{状态}") }
            : new[] { ("毛重", "{毛重} {单位}"), ("皮重", "{皮重} {单位}"), ("净重", "{净重} {单位}"),
                      ("品种", "{货物}"), ("司磅员", "{司磅员}"), ("", "") };

        float gy = dense ? 24f : 32f;
        float rowH = dense ? 8.5f : 11f;
        float labelW = contentW * 0.19f, valueW = contentW * 0.31f;
        float x1 = x + labelW, x2 = x1 + valueW, x3 = x2 + labelW;
        t.Elements.Add(new RectElement { X = x, Y = gy, W = contentW, H = rowH * 6 });
        for (int i = 1; i < 6; i++)
            t.Elements.Add(new LineElement { X = x, Y = gy + i * rowH, W = contentW, H = 0 });
        foreach (var lx in new[] { x1, x2, x3 })
            t.Elements.Add(new LineElement { X = lx, Y = gy, W = 0, H = rowH * 6 });
        float valueOffset = dense ? 1.2f : 2.2f;
        for (int i = 0; i < 6; i++)
        {
            float ry = gy + i * rowH + valueOffset;
            t.Elements.Add(new TextElement { Text = left[i].Item1, X = x + 2, Y = ry, W = labelW - 4, H = 5, FontPt = 9, Bold = true });
            t.Elements.Add(new TextElement { Text = left[i].Item2, X = x1 + 2, Y = ry, W = valueW - 4, H = 5, FontPt = 10 });
            if (right[i].Item1.Length == 0) continue;
            t.Elements.Add(new TextElement { Text = right[i].Item1, X = x2 + 2, Y = ry, W = labelW - 4, H = 5, FontPt = 9, Bold = true });
            t.Elements.Add(new TextElement { Text = right[i].Item2, X = x3 + 2, Y = ry, W = valueW - 4, H = 5, FontPt = 10 });
        }

        // 网格下方：磅重时间行（三等分高度有限，二次磅时间与司磅员同行）
        float below = gy + rowH * 6 + 1.5f;
        if (dense)
        {
            t.Elements.Add(new TextElement { Text = "一次磅：{一次磅时间:HH:mm:ss}　二次磅：{二次磅时间:HH:mm:ss}", X = x, Y = below, W = contentW, H = 5, FontPt = 8 });
            t.Elements.Add(new TextElement { Text = "司磅员：{司磅员}", X = x, Y = below + 6.5f, W = contentW, H = 5, FontPt = 8 });
        }
        else
        {
            t.Elements.Add(new TextElement { Text = "一次磅时间：{一次磅时间}", X = x, Y = below, W = contentW / 2, H = 5, FontPt = 9 });
            t.Elements.Add(new TextElement { Text = "二次磅时间：{二次磅时间}", X = x + contentW / 2, Y = below, W = contentW / 2, H = 5, FontPt = 9, Align = TextAlign.Right });
            t.Elements.Add(new SignatureElement { Labels = ["司机签字", "客户签字"], X = x, Y = below + 7, W = contentW, H = 10 });
            t.Elements.Add(new TextElement { Text = "*金额由收费分段自动计算（金额磅单）；竖联磅单请使用 120×140mm 带孔磅单纸",
                X = x, Y = below + 19, W = contentW, H = 4, FontPt = 6.5f, ForeColor = "#888888" });

            // A4 整版补充：二维码、状态汇总表与流水号条码（其余纸样空间有限不放）
            if (paperKey == "A4")
            {
                t.Elements.Add(new QRCodeElement { Content = "{流水号}", X = x + contentW - 22, Y = 4, W = 22, H = 22 });
                t.Elements.Add(new TableElement
                {
                    X = x, Y = below + 30, W = contentW, RowH = 7,
                    Columns =
                    [
                        new() { Header = "状态", Text = "{状态}", Width = 60 },
                        new() { Header = "打印时间", Text = "{打印时间:yyyy-MM-dd HH:mm}", Width = 80 },
                    ],
                });
                t.Elements.Add(new BarcodeElement { Content = "{流水号}", X = x, Y = below + 50, W = 90, H = 14 });
            }
        }
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
