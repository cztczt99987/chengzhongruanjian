namespace CZRWeighSystem.Core;

/// <summary>
/// 称重记录留痕模型（对应 PRD F3-03/F3-04：修改/作废需留痕）。
/// 每次修改按字段记录前后值；作废记录原因。
/// </summary>
public class RecordAudit
{
    public long Id { get; set; }
    /// <summary>关联的称重记录 Id</summary>
    public long RecordId { get; set; }
    /// <summary>动作：修改 / 作废</summary>
    public string Action { get; set; } = "";
    /// <summary>字段名（作废时为空）</summary>
    public string? Field { get; set; }
    /// <summary>修改前的值（作废时为空）</summary>
    public string? OldValue { get; set; }
    /// <summary>修改后的值（作废时存作废原因）</summary>
    public string? NewValue { get; set; }
    /// <summary>操作人</summary>
    public string? Operator { get; set; }
    /// <summary>操作时间</summary>
    public DateTime Time { get; set; }
}
