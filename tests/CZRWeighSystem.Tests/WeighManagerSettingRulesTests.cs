using CZRWeighSystem.Core;
using CZRWeighSystem.Database;

namespace CZRWeighSystem.Tests;

/// <summary>
/// 综合设置-基本设置的称重拦截规则测试：
/// 1) 仪表不归零不允许保存（防止重复称重）；
/// 2) 严格禁止 X 分钟内二次称重。
/// 使用测试输出目录下的共享 SQLite 库（与数据库相关测试同集合串行执行）。
/// </summary>
[Collection("数据库顺序")]
public class WeighManagerSettingRulesTests : IDisposable
{
    private readonly WeighRecordRepository _repo = new();
    private readonly AppConfig _cfg = AppConfig.Current;

    static WeighManagerSettingRulesTests()
    {
        Db.Initialize();   // 幂等建表
    }

    public void Dispose()
    {
        // 还原全局配置，避免影响其他测试
        _cfg.BlockSaveUntilZero = false;
        _cfg.ForbidReweighMinutes = 0;
        _cfg.StableSeconds = 0;
    }

    /// <summary>
    /// 构造注入稳定重量的管理器（端口指向不存在的 COM，避免占用真实串口）。
    /// 磅号取随机值：共享库中残留历史记录，固定磅号的流水号会跨运行撞号。
    /// </summary>
    private static WeighManager CreateManagerWithStableWeight(double kg)
    {
        var cfg = AppConfig.Current;
        cfg.PortName = "COM_TEST_NONE";
        cfg.ScaleNo = $"T{Guid.NewGuid():N}"[..6];
        cfg.StableCount = 3;
        cfg.StableToleranceKg = 5;
        cfg.StableSeconds = 0;
        var manager = new WeighManager();
        for (int i = 0; i < 4; i++) manager.InjectWeight(kg);
        return manager;
    }

    [Fact]
    public void BlockSaveUntilZero_BlocksSecondSaveUntilScaleReturnsToZero()
    {
        string vehicleA = $"测A{Guid.NewGuid():N}"[..7];
        string vehicleB = $"测B{Guid.NewGuid():N}"[..7];
        _cfg.BlockSaveUntilZero = true;

        using var manager = CreateManagerWithStableWeight(50000);

        // 第一次保存成功
        var (ok1, _, _) = manager.SaveFirstWeigh(vehicleA, null, null, null, null, null, null, null, "测试");
        Assert.True(ok1);

        // 未归零再次保存 → 拦截（提示归零）
        var (ok2, msg2, _) = manager.SaveFirstWeigh(vehicleB, null, null, null, null, null, null, null, "测试");
        Assert.False(ok2);
        Assert.Contains("归零", msg2);

        // 仪表回到零位 → 拦截解除（补足稳定采样后重量为 0，报"车辆上磅"提示而非归零拦截）
        for (int i = 0; i < 4; i++) manager.InjectWeight(0);
        var (ok3, msg3, _) = manager.SaveFirstWeigh(vehicleB, null, null, null, null, null, null, null, "测试");
        Assert.False(ok3);
        Assert.DoesNotContain("归零", msg3);
        Assert.Contains("上磅", msg3);
    }

    [Fact]
    public void BlockSaveUntilZero_Disabled_AllowsConsecutiveSaves()
    {
        string vehicleA = $"测C{Guid.NewGuid():N}"[..7];
        string vehicleB = $"测D{Guid.NewGuid():N}"[..7];
        _cfg.BlockSaveUntilZero = false;

        using var manager = CreateManagerWithStableWeight(40000);

        var (ok1, _, _) = manager.SaveFirstWeigh(vehicleA, null, null, null, null, null, null, null, "测试");
        Assert.True(ok1);
        var (ok2, _, _) = manager.SaveFirstWeigh(vehicleB, null, null, null, null, null, null, null, "测试");
        Assert.True(ok2);   // 未启用归零拦截：连续保存不拦
    }

    [Fact]
    public void ForbidReweighMinutes_BlocksWeighWithinWindow()
    {
        string vehicle = $"测E{Guid.NewGuid():N}"[..7];
        string scaleNo = $"T{Guid.NewGuid():N}"[..6];
        _cfg.ForbidReweighMinutes = 10;

        // 该车 5 分钟前刚结算完
        var record = _repo.InsertFirstWeigh(new WeighRecord { ScaleNo = scaleNo, VehicleNo = vehicle });
        _repo.CompleteSecondWeigh(record.Id, 1000, null, "测试");

        using var manager = CreateManagerWithStableWeight(30000);
        var (ok, msg, _) = manager.SaveFirstWeigh(vehicle, null, null, null, null, null, null, null, "测试");
        Assert.False(ok);
        Assert.Contains("严格禁止", msg);

        // 关闭限制后同车可正常保存
        _cfg.ForbidReweighMinutes = 0;
        var (ok2, _, _) = manager.SaveFirstWeigh(vehicle, null, null, null, null, null, null, null, "测试");
        Assert.True(ok2);
    }
}
