using CZRWeighSystem.Core;
using CZRWeighSystem.Database;

namespace CZRWeighSystem.Tests;

/// <summary>
/// 结算金额落库测试：二次磅结算时写入应收金额（按收费分段计算），
/// 金额随结算 UPDATE 持久化，并能从查询中读回。
/// 与其他 MainForm 测试同集合串行执行，避免删库重建并发冲突。
/// </summary>
[Collection("数据库顺序")]
public class FeeSettlementTests
{
    [Fact]
    public void CompleteSecondWeigh_写入应收金额并可读回()
    {
        Db.Initialize();
        var repo = new WeighRecordRepository();
        var first = repo.InsertFirstWeigh(new WeighRecord
        {
            ScaleNo = "92",
            VehicleNo = "费测001",
            FirstTime = DateTime.Now,
            Status = "未完成",
        });

        // 一次磅 1000kg + 二次 2500kg → 净重 1.5 吨（此处金额直接给值，分段计算见 FeeCalculatorTests）
        var done = repo.CompleteSecondWeigh(first.Id, 2500, null, "测试", 3m);
        Assert.Equal(3m, done.AmountYuan);
        Assert.Equal(3m, repo.Query(null, null, "费测001", "", "", "").Single().AmountYuan);
    }

    [Fact]
    public void CompleteSecondWeigh_未传金额_金额为空()
    {
        Db.Initialize();
        var repo = new WeighRecordRepository();
        var first = repo.InsertFirstWeigh(new WeighRecord
        {
            ScaleNo = "92",
            VehicleNo = "费测002",
            FirstTime = DateTime.Now,
            Status = "未完成",
        });

        var done = repo.CompleteSecondWeigh(first.Id, 100, null, "测试");
        Assert.Null(done.AmountYuan);
    }
}
