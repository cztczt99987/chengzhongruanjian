using CZRWeighSystem.Core;
using CZRWeighSystem.Database;
using CZRWeighSystem.Sync;
using CZRWeighSystem.Utils;

namespace CZRWeighSystem.Tests;

/// <summary>
/// 历史补推测试：
/// 启动首轮 / 开启平台对接时，把"已完成但从未入队"的记录补进推送队列；
/// 已入队（含已推送成功）的记录不重复补，未启用对接时不入队。
/// </summary>
[Collection("数据库顺序")]
public class PushSchedulerBackfillTests
{
    private static void ClearQueue()
    {
        using var conn = Db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM t_push_queue";
        cmd.ExecuteNonQuery();
    }

    private static void DeleteRecord(string serial)
    {
        using var conn = Db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM t_weigh_record WHERE serial_no=@no";
        cmd.Add("@no", serial);
        cmd.ExecuteNonQuery();
    }

    [Fact]
    public void BackfillHistory_补推已完成未入队记录_重复调用幂等()
    {
        var cfg = AppConfig.Current;
        bool oldEnabled = cfg.PlatformSyncEnabled;
        var records = new WeighRecordRepository();
        var first = records.InsertFirstWeigh(new WeighRecord
        {
            ScaleNo = "94",
            VehicleNo = "补" + Guid.NewGuid().ToString("N")[..6],
            GrossKg = 28000,
        });
        string serial = first.SerialNo;
        records.CompleteSecondWeigh(first.Id, 45000, null, "测试");

        try
        {
            cfg.PlatformSyncEnabled = true;
            ClearQueue();
            var scheduler = new PushScheduler();

            Assert.True(scheduler.BackfillHistory(5000) >= 1);
            Assert.NotNull(new PushQueueRepository().GetStatus(PushQueueRepository.KindRecord, serial));

            // 第二轮：所有已完成记录均已入队 → 不再补充
            Assert.Equal(0, scheduler.BackfillHistory(5000));
        }
        finally
        {
            cfg.PlatformSyncEnabled = oldEnabled;
            ClearQueue();
            DeleteRecord(serial);
        }
    }

    [Fact]
    public void BackfillHistory_未启用平台对接时不入队()
    {
        var cfg = AppConfig.Current;
        bool oldEnabled = cfg.PlatformSyncEnabled;
        try
        {
            cfg.PlatformSyncEnabled = false;
            ClearQueue();

            Assert.Equal(0, new PushScheduler().BackfillHistory());
            Assert.Equal((0, 0), new PushQueueRepository().GetStats());
        }
        finally
        {
            cfg.PlatformSyncEnabled = oldEnabled;
            ClearQueue();
        }
    }
}
