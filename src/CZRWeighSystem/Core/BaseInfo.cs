namespace CZRWeighSystem.Core;

/// <summary>
/// 车辆档案（对应 PRD F9-01 / 第 8 章 t_vehicle）。
/// </summary>
public class Vehicle
{
    public long Id { get; set; }
    /// <summary>车号（唯一）</summary>
    public string VehicleNo { get; set; } = "";
    /// <summary>默认皮重（千克），称重时自动带出</summary>
    public double? DefaultTareKg { get; set; }
    public string? Owner { get; set; }
    public string? Phone { get; set; }
    public bool Enabled { get; set; } = true;
}

/// <summary>
/// 货物档案（对应 PRD F9-02）。
/// </summary>
public class GoodsInfo
{
    public long Id { get; set; }
    /// <summary>货物名称（唯一）</summary>
    public string Name { get; set; } = "";
    public string? Spec { get; set; }
    /// <summary>计量单位（吨/千克/件等）</summary>
    public string? Unit { get; set; }
    /// <summary>默认扣率（%，扣水扣杂用，0~100）</summary>
    public double? DeductRate { get; set; }
    public bool Enabled { get; set; } = true;
}

/// <summary>
/// 收/发货单位档案（对应 PRD F9-03）。
/// </summary>
public class CompanyInfo
{
    public long Id { get; set; }
    /// <summary>单位名称</summary>
    public string Name { get; set; } = "";
    /// <summary>类型：收货 / 发货</summary>
    public string Type { get; set; } = "收货";
    public string? Contact { get; set; }
    public string? Phone { get; set; }
    public bool Enabled { get; set; } = true;
}
