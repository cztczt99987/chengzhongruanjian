using CZRWeighSystem.Core;
using CZRWeighSystem.Database;
using CZRWeighSystem.Report;

namespace CZRWeighSystem.Tests;

/// <summary>
/// 磅单"打印次数"累计测试（综合设置-基本设置：启用累计 + 每单最多打印 N 次）：
/// 1) 上限判定：开启累计后已打印次数达到上限即拦截，关闭累计不拦；
/// 2) 计数持久化：打印成功后 print_count +1 并能从库中读回。
/// </summary>
[Collection("数据库顺序")]
public class PrintLimitTests : IDisposable
{
    private readonly WeighRecordRepository _repo = new();
    private readonly AppConfig _cfg = AppConfig.Current;

    static PrintLimitTests()
    {
        Db.Initialize();   // 幂等建表
    }

    public void Dispose()
    {
        _cfg.PrintCountEnabled = true;   // 还原默认
        _cfg.MaxPrintCount = 5;
    }

    [Fact]
    public void ExceedsPrintLimit_Enabled_ReachesMaxBlocks()
    {
        _cfg.PrintCountEnabled = true;
        _cfg.MaxPrintCount = 5;

        Assert.False(PoundTicketPrinter.ExceedsPrintLimit(new WeighRecord { PrintCount = 4 }, _cfg));
        Assert.True(PoundTicketPrinter.ExceedsPrintLimit(new WeighRecord { PrintCount = 5 }, _cfg));

        _cfg.PrintCountEnabled = false;
        Assert.False(PoundTicketPrinter.ExceedsPrintLimit(new WeighRecord { PrintCount = 99 }, _cfg));
    }

    [Fact]
    public void IncrementPrintCount_PersistsAndReturnsNewCount()
    {
        string scaleNo = $"T{Guid.NewGuid():N}"[..6];
        var record = _repo.InsertFirstWeigh(new WeighRecord { ScaleNo = scaleNo, VehicleNo = $"测P{Guid.NewGuid():N}"[..7] });
        Assert.Equal(0, record.PrintCount);

        Assert.Equal(1, _repo.IncrementPrintCount(record.Id));
        Assert.Equal(2, _repo.IncrementPrintCount(record.Id));

        // 从库中读回的记录带累计次数
        var unfinished = _repo.GetUnfinishedList().First(x => x.Id == record.Id);
        Assert.Equal(2, unfinished.PrintCount);
    }
}
