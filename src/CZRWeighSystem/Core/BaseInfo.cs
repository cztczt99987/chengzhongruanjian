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
