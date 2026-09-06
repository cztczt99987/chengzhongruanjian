using CZRWeighSystem.Core;
using CZRWeighSystem.Database;
using CZRWeighSystem.Utils;

namespace CZRWeighSystem.Tests;

/// <summary>
/// 首次称重有效天数（超期未配对一次磅自动删除）测试。
/// 使用测试输出目录下的 SQLite 库（Db.Initialize 幂等建表）。
/// </summary>
public class FirstWeighExpireTests
{
    private readonly WeighRecordRepository _repo = new();

    static FirstWeighExpireTests()
    {
        // 每次测试运行前重建空库：避免上次运行残留的记录影响断言
        if (File.Exists(Db.DbPath)) File.Delete(Db.DbPath);
    }

    public FirstWeighExpireTests() => Db.Initialize();

    /// <summary>插入一条未完成一次磅，返回记录（可再改 first_time/状态）。
    /// 流水号按"日期+秤号+计数"生成且删除会使计数回落，
    /// 各测试须用不同秤号避免串号冲突。</summary>
    private WeighRecord Insert(string vehicleNo, string scaleNo)
    {
        return _repo.InsertFirstWeigh(new WeighRecord
        {
            ScaleNo = scaleNo,
            VehicleNo = vehicleNo,
        });
    }

    /// <summary>直接改写记录的 first_time（测试超期场景用）。</summary>
    private void SetFirstTime(long id, DateTime time)
    {
        using var conn = Db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE t_weigh_record SET first_time=@t WHERE id=@id";
        cmd.Add("@t", time.ToString("yyyy-MM-dd HH:mm:ss"));
        cmd.Add("@id", id);
        cmd.ExecuteNonQuery();
    }

    [Fact]
    public void DeleteExpiredUnfinished_RemovesOnlyExpiredUnfinished()
    {
        // 过期未完成（10 天前）→ 应删除
        var expired = Insert($"测A{Guid.NewGuid():N}"[..7], "01");
        SetFirstTime(expired.Id, DateTime.Now.AddDays(-10));

        // 未过期未完成（1 天前）→ 应保留
        var recent = Insert($"测B{Guid.NewGuid():N}"[..7], "01");
        SetFirstTime(recent.Id, DateTime.Now.AddDays(-1));

        // 过期但已完成 → 应保留
        var done = Insert($"测C{Guid.NewGuid():N}"[..7], "01");
        SetFirstTime(done.Id, DateTime.Now.AddDays(-10));
        _repo.CompleteSecondWeigh(done.Id, 100, null, "测试");

        int deleted = _repo.DeleteExpiredUnfinished(7);

        Assert.Equal(1, deleted);
        var unfinished = _repo.GetUnfinishedList();
        Assert.DoesNotContain(unfinished, r => r.Id == expired.Id);
        Assert.Contains(unfinished, r => r.Id == recent.Id);
        Assert.DoesNotContain(unfinished, r => r.Id == done.Id);
    }

    [Fact]
    public void DeleteExpiredUnfinished_ZeroDays_DoesNothing()
    {
        var r = Insert($"测D{Guid.NewGuid():N}"[..7], "02");
        SetFirstTime(r.Id, DateTime.Now.AddDays(-100));

        Assert.Equal(0, _repo.DeleteExpiredUnfinished(0));
        Assert.Contains(_repo.GetUnfinishedList(), x => x.Id == r.Id);
    }
}
