using CZRWeighSystem.Core;
using CZRWeighSystem.Database;
using CZRWeighSystem.Utils;

namespace CZRWeighSystem.Tests;

/// <summary>
/// 流水号生成回归测试：序号取同前缀最大序号 + 1。
/// 历史背景：原实现按「行数 + 1」编号，同磅号记录被删除产生序号空洞后，
/// 新增会撞上已存在的流水号（UNIQUE 冲突导致保存失败）。
/// </summary>
[Collection("数据库顺序")]
public class SerialNoTests
{
    [Fact]
    public void InsertFirstWeigh_删除历史记录产生序号空洞_新增不重号()
    {
        Db.Initialize();
        var repo = new WeighRecordRepository();
        string scale = $"G{Guid.NewGuid():N}"[..6];   // 独立磅号，避免与其他测试互相影响

        var first = repo.InsertFirstWeigh(new WeighRecord { ScaleNo = scale, VehicleNo = "号测001" });
        repo.InsertFirstWeigh(new WeighRecord { ScaleNo = scale, VehicleNo = "号测002" });

        // 删除第一行，制造序号空洞（0001 没了，0002 还在）
        using (var conn = Db.CreateConnection())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "DELETE FROM t_weigh_record WHERE id=@id";
            cmd.Add("@id", first.Id);
            cmd.ExecuteNonQuery();
        }

        // 修复前：按行数编号 → 生成已存在的 0002 → UNIQUE 冲突；修复后：取最大序号 + 1 → 0003
        var third = repo.InsertFirstWeigh(new WeighRecord { ScaleNo = scale, VehicleNo = "号测003" });
        Assert.NotEqual(first.SerialNo, third.SerialNo);
        Assert.EndsWith("0003", third.SerialNo);
    }
}
