namespace CZRWeighSystem.Core;

/// <summary>
/// 自定义字段定义（对应 PRD 自定义表单能力，存储于 t_custom_field）。
/// 称重记录的扩展字段值以 JSON 存于 t_weigh_record.ext_json（键=字段名）。
/// </summary>
public class CustomField
{
    public long Id { get; set; }

    /// <summary>字段名（录入区标签、查询列名、打印占位符名）</summary>
    public string Name { get; set; } = "";

    /// <summary>类型：文本 / 数字 / 日期 / 下拉</summary>
    public string FieldType { get; set; } = "文本";

    /// <summary>下拉类型的候选值（中文逗号或英文逗号分隔）</summary>
    public string? Options { get; set; }

    /// <summary>保存时是否必填</summary>
    public bool Required { get; set; }

    /// <summary>显示顺序（小在前）</summary>
    public int SortOrder { get; set; }

    public bool Enabled { get; set; } = true;

    /// <summary>下拉候选值列表。</summary>
    public string[] OptionList => (Options ?? "")
        .Split(['，', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
