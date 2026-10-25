using CZRWeighSystem.Database;
using CZRWeighSystem.Sync;
using CZRWeighSystem.Utils;

namespace CZRWeighSystem.Tests;

/// <summary>
/// 本地推送队列仓储测试（t_push_queue）：
/// 幂等入队、到期查询、结果回写、退避重试（1m/5m/15m/1h/6h，最多 5 次后置 failed）、
/// 失败项人工重推与统计。
/// </summary>
[Collection("数据库顺序")]
public class PushQueueRepositoryTests
{
    private readonly PushQueueRepository _repo = new();

    public PushQueueRepositoryTests()
    {
        Db.Initialize();   // 幂等建表（含 t_push_queue）
        // 共享测试库跨轮次累积 pending 记录会淹没 GetDue 的前 100 条窗口：新入队项被历史积压挤出后
        // First(i => i.ClientRef == @ref) 查不到自己（2026-10-04 全量失败：库中积压 114 条）。
        // 队列表仅本类用例使用，逐用例清空保证各用例独立、结果确定。
        using var conn = Db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM t_push_queue";
        cmd.ExecuteNonQuery();
    }

    /// <summary>每条测试用独立引用号，避免互相影响。</summary>
    private static string Ref() => "T" + Guid.NewGuid().ToString("N")[..20];

    [Fact]
    public void Enqueue_同键幂等只入队一次()
    {
        string @ref = Ref();
        Assert.True(_repo.Enqueue(PushQueueRepository.KindRecord, @ref, "{}"));
        Assert.False(_repo.Enqueue(PushQueueRepository.KindRecord, @ref, "{}"));   // 重复忽略

        // 同 ref 不同 kind 各自独立（照片与记录引用号不冲突）
        Assert.True(_repo.Enqueue(PushQueueRepository.KindPhoto, @ref, "{}"));
    }

    [Fact]
    public void GetDue_只取到期待推送项且限量()
    {
        string due = Ref(), deferred = Ref();
        _repo.Enqueue(PushQueueRepository.KindRecord, due, "{}");
        _repo.Enqueue(PushQueueRepository.KindRecord, deferred, "{}");
        _repo.Defer(_repo.GetDue(PushQueueRepository.KindRecord, 100)
            .First(i => i.ClientRef == deferred).Id, TimeSpan.FromHours(1));

        var dueItems = _repo.GetDue(PushQueueRepository.KindRecord, 100);
        Assert.Contains(dueItems, i => i.ClientRef == due);
        Assert.DoesNotContain(dueItems, i => i.ClientRef == deferred);

        // kind 隔离与限量（共享库中可能有其他 pending 记录，只验证上限）
        Assert.DoesNotContain(_repo.GetDue(PushQueueRepository.KindPhoto, 100), i => i.ClientRef == due);
        Assert.InRange(_repo.GetDue(PushQueueRepository.KindRecord, 1).Count, 0, 1);
    }

    [Fact]
    public void MarkDone_结果可按引用号查回()
    {
        string @ref = Ref();
        _repo.Enqueue(PushQueueRepository.KindPhoto, @ref, "{}");
        var item = _repo.GetDue(PushQueueRepository.KindPhoto, 100).First(i => i.ClientRef == @ref);

        _repo.MarkDone(item.Id, "ph_9f8e7d6c");

        Assert.Equal("ph_9f8e7d6c", _repo.GetDoneResult(PushQueueRepository.KindPhoto, @ref));
        Assert.Null(_repo.GetDoneResult(PushQueueRepository.KindRecord, @ref));   // kind 隔离
        Assert.DoesNotContain(_repo.GetDue(PushQueueRepository.KindPhoto, 100),
            i => i.ClientRef == @ref);   // 已完成不再推送
    }

    [Fact]
    public void GetStatus_返回状态与最后错误_不存在为null()
    {
        string @ref = Ref();
        Assert.Null(_repo.GetStatus(PushQueueRepository.KindPhoto, @ref));

        _repo.Enqueue(PushQueueRepository.KindPhoto, @ref, "{}");
        Assert.Equal("pending", _repo.GetStatus(PushQueueRepository.KindPhoto, @ref)!.Value.Status);

        long id = _repo.GetDue(PushQueueRepository.KindPhoto, 100).First(i => i.ClientRef == @ref).Id;
        _repo.MarkFailed(id, "文件损坏");
        var st = _repo.GetStatus(PushQueueRepository.KindPhoto, @ref)!.Value;
        Assert.Equal("failed", st.Status);
        Assert.Equal("文件损坏", st.LastError);
        // kind 隔离：同 ref 的记录项不受照片失败影响
        Assert.Null(_repo.GetStatus(PushQueueRepository.KindRecord, @ref));
    }

    [Fact]
    public void ScheduleRetry_按退避表顺延_超五次置失败()
    {
        string @ref = Ref();
        _repo.Enqueue(PushQueueRepository.KindRecord, @ref, "{}");
        long id = _repo.GetDue(PushQueueRepository.KindRecord, 100).First(i => i.ClientRef == @ref).Id;

        // 第 1~5 次失败：仍是 pending，按 1m/5m/15m/1h/6h 顺延且计次
        for (int attempt = 1; attempt <= 5; attempt++)
        {
            _repo.ScheduleRetry(id, $"网络异常 #{attempt}");
            var item = GetItem(id);
            Assert.Equal("pending", item!.Status);
            Assert.Equal(attempt, item.Attempts);
            double delayMin = (item.NextAttempt - DateTime.Now).TotalMinutes;
            double expected = PushQueueRepository.RetryBackoff[attempt - 1].TotalMinutes;
            Assert.InRange(delayMin, expected - 2, expected);
        }

        // 第 6 次失败：置终态 failed 并保留原因
        _repo.ScheduleRetry(id, "网络异常 #6");
        var failed = GetItem(id);
        Assert.Equal("failed", failed!.Status);
        Assert.Equal("网络异常 #6", failed.LastError);
    }

    [Fact]
    public void MarkFailed_直接置失败并保留原因()
    {
        string @ref = Ref();
        _repo.Enqueue(PushQueueRepository.KindRecord, @ref, "{}");
        long id = _repo.GetDue(PushQueueRepository.KindRecord, 100).First(i => i.ClientRef == @ref).Id;

        _repo.MarkFailed(id, "地主电话非法");

        var item = GetItem(id);
        Assert.Equal("failed", item!.Status);
        Assert.Equal("地主电话非法", item.LastError);
    }

    [Fact]
    public void RequeueFailed_失败项重置待推送_成功项不动()
    {
        string failedRef = Ref(), doneRef = Ref();
        _repo.Enqueue(PushQueueRepository.KindRecord, failedRef, "{}");
        _repo.Enqueue(PushQueueRepository.KindRecord, doneRef, "{}");
        var items = _repo.GetDue(PushQueueRepository.KindRecord, 100);
        _repo.MarkFailed(items.First(i => i.ClientRef == failedRef).Id, "鉴权失败");
        _repo.MarkDone(items.First(i => i.ClientRef == doneRef).Id, "CZ001202609300001");

        _repo.RequeueFailed();

        // 失败项重新可推送且计次清零；已完成项不被重推
        var redone = _repo.GetDue(PushQueueRepository.KindRecord, 100);
        var requeued = redone.FirstOrDefault(i => i.ClientRef == failedRef);
        Assert.NotNull(requeued);
        Assert.Equal(0, requeued.Attempts);
        Assert.DoesNotContain(redone, i => i.ClientRef == doneRef);
        Assert.Equal(0, _repo.GetStats().Failed);
    }

    [Fact]
    public void GetStats_统计待推送与失败()
    {
        string @ref = Ref();
        _repo.Enqueue(PushQueueRepository.KindVoid, @ref, "{}");
        long id = _repo.GetDue(PushQueueRepository.KindVoid, 100).First(i => i.ClientRef == @ref).Id;

        var (pendingBefore, _) = _repo.GetStats();
        _repo.MarkFailed(id, "平台暂无该记录");
        var (pendingAfter, failed) = _repo.GetStats();

        Assert.Equal(pendingBefore - 1, pendingAfter);
        Assert.True(failed >= 1);
    }

    private PushQueueItem? GetItem(long id)
    {
        // 直接读库回查（GetDue 只出 pending，失败后需按 Id 查）
        using var conn = Db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM t_push_queue WHERE id=@id";
        cmd.Add("@id", id);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;
        return new PushQueueItem
        {
            Id = id,
            Status = reader.GetString(reader.GetOrdinal("status")),
            Attempts = Convert.ToInt32(reader.GetValue(reader.GetOrdinal("attempts"))),
            NextAttempt = DateTime.Parse(reader.GetString(reader.GetOrdinal("next_attempt"))),
            LastError = reader.IsDBNull(reader.GetOrdinal("last_error"))
                ? "" : reader.GetString(reader.GetOrdinal("last_error")),
        };
    }
}
