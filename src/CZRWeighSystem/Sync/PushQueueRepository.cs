using System.Data.Common;
using CZRWeighSystem.Database;
using CZRWeighSystem.Utils;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;

namespace CZRWeighSystem.Sync;

/// <summary>推送队列项（t_push_queue 一行）。</summary>
public sealed class PushQueueItem
{
    public long Id { get; set; }
    /// <summary>类型：photo / record / blackbox / void</summary>
    public string Kind { get; set; } = "";
    /// <summary>幂等键：记录流水号 / 照片引用号 / BX+黑匣子Id</summary>
    public string ClientRef { get; set; } = "";
    /// <summary>推送载荷 JSON</summary>
    public string Payload { get; set; } = "";
    /// <summary>pending / done / failed</summary>
    public string Status { get; set; } = "pending";
    public int Attempts { get; set; }
    public DateTime NextAttempt { get; set; }
    public string LastError { get; set; } = "";
    /// <summary>成功结果（照片项存 photoId，记录项存平台称重单号）</summary>
    public string Result { get; set; } = "";
}

/// <summary>
/// 本地推送队列仓储（t_push_queue，双方言 SQLite / SQL Server）。
/// 幂等：同 (kind, client_ref) 唯一索引，重复入队直接忽略；
/// 失败重试按对接文档 §6 指数退避（1m/5m/15m/1h/6h，最多 5 次后置 failed）。
/// </summary>
public class PushQueueRepository
{
    public const string KindPhoto = "photo";
    public const string KindRecord = "record";
    public const string KindBlackbox = "blackbox";
    public const string KindVoid = "void";

    /// <summary>可重试退避表（分钟）：失败第 1~5 次的下一次推送时间。</summary>
    internal static readonly TimeSpan[] RetryBackoff =
    [
        TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(15),
        TimeSpan.FromHours(1), TimeSpan.FromHours(6),
    ];

    /// <summary>入队（同 kind+clientRef 已存在时忽略，返回 false——重复推送/重开软件均安全）。</summary>
    public bool Enqueue(string kind, string clientRef, string payload)
    {
        using var conn = Db.CreateConnection();
        if (Exists(conn, kind, clientRef)) return false;

        string now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        const string sql = """
            INSERT INTO t_push_queue
                (kind, client_ref, payload, status, attempts, next_attempt, last_error, result, created_at, updated_at)
            VALUES
                (@kind, @ref, @payload, 'pending', 0, @now, '', '', @now, @now);
            """;
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Add("@kind", kind);
        cmd.Add("@ref", clientRef);
        cmd.Add("@payload", payload);
        cmd.Add("@now", now);
        try
        {
            return cmd.ExecuteNonQuery() > 0;
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
        {
            return false;   // 并发撞唯一索引：已入队
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627)
        {
            return false;
        }
    }

    /// <summary>取到期待推送项（status=pending 且 next_attempt ≤ now，按入队先后）。</summary>
    public List<PushQueueItem> GetDue(string kind, int limit)
    {
        using var conn = Db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = Db.TopSql(
            "SELECT {TOP} * FROM t_push_queue WHERE kind=@kind AND status='pending' AND next_attempt <= @now ",
            "ORDER BY id", limit);
        cmd.Add("@kind", kind);
        cmd.Add("@now", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        using var reader = cmd.ExecuteReader();
        var list = new List<PushQueueItem>();
        while (reader.Read()) list.Add(Map(reader));
        return list;
    }

    /// <summary>推送成功（result 存 photoId / 平台称重单号等）。</summary>
    public void MarkDone(long id, string result)
    {
        const string sql = """
            UPDATE t_push_queue SET status='done', result=@result, last_error='', updated_at=@now
            WHERE id=@id;
            """;
        Exec(sql, ("@result", result), ("@id", id));
    }

    /// <summary>终态失败（鉴权失败/单条校验失败等不可自愈错误），界面可见、可人工重推。</summary>
    public void MarkFailed(long id, string error)
    {
        const string sql = """
            UPDATE t_push_queue SET status='failed', last_error=@err, updated_at=@now WHERE id=@id;
            """;
        Exec(sql, ("@err", error), ("@id", id));
    }

    /// <summary>顺延且不计失败次数（限流、等待关联照片完成）。</summary>
    public void Defer(long id, TimeSpan delay)
    {
        const string sql = """
            UPDATE t_push_queue SET next_attempt=@next, updated_at=@now WHERE id=@id;
            """;
        Exec(sql, ("@next", DateTime.Now.Add(delay).ToString("yyyy-MM-dd HH:mm:ss")), ("@id", id));
    }

    /// <summary>可重试失败：attempts+1，未超上限按退避表顺延，超上限置 failed。</summary>
    public void ScheduleRetry(long id, string error)
    {
        var item = GetById(id);
        if (item == null) return;
        int attempts = item.Attempts + 1;
        if (attempts > RetryBackoff.Length)
        {
            MarkFailed(id, error);
            return;
        }
        const string sql = """
            UPDATE t_push_queue
            SET attempts=@attempts, next_attempt=@next, last_error=@err, updated_at=@now
            WHERE id=@id;
            """;
        Exec(sql,
            ("@attempts", attempts),
            ("@next", DateTime.Now.Add(RetryBackoff[attempts - 1]).ToString("yyyy-MM-dd HH:mm:ss")),
            ("@err", error),
            ("@id", id));
    }

    /// <summary>已完成项的结果查询（照片引用号 → photoId）。</summary>
    public string? GetDoneResult(string kind, string clientRef)
    {
        using var conn = Db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT result FROM t_push_queue
            WHERE kind=@kind AND client_ref=@ref AND status='done';
            """;
        cmd.Add("@kind", kind);
        cmd.Add("@ref", clientRef);
        var val = cmd.ExecuteScalar();
        return val is string s && s.Length > 0 ? s : null;
    }

    /// <summary>队列统计（设置界面同步状态展示）。</summary>
    public (int Pending, int Failed) GetStats()
    {
        using var conn = Db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT
                SUM(CASE WHEN status='pending' THEN 1 ELSE 0 END),
                SUM(CASE WHEN status='failed' THEN 1 ELSE 0 END)
            FROM t_push_queue
            """;
        using var reader = cmd.ExecuteReader();
        reader.Read();
        return (
            reader.IsDBNull(0) ? 0 : Convert.ToInt32(reader.GetValue(0)),
            reader.IsDBNull(1) ? 0 : Convert.ToInt32(reader.GetValue(1)));
    }

    /// <summary>人工重推：全部失败项重置为待推送（attempts 清零重新退避）。返回重置条数。</summary>
    public int RequeueFailed()
    {
        const string sql = """
            UPDATE t_push_queue
            SET status='pending', attempts=0, last_error='', next_attempt=@now, updated_at=@now
            WHERE status='failed';
            """;
        string now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        using var conn = Db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Add("@now", now);
        return cmd.ExecuteNonQuery();
    }

    private static bool Exists(DbConnection conn, string kind, string clientRef)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(1) FROM t_push_queue WHERE kind=@kind AND client_ref=@ref";
        cmd.Add("@kind", kind);
        cmd.Add("@ref", clientRef);
        return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
    }

    private PushQueueItem? GetById(long id)
    {
        using var conn = Db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM t_push_queue WHERE id=@id";
        cmd.Add("@id", id);
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    private static void Exec(string sql, params (string Name, object? Value)[] parameters)
    {
        using var conn = Db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in parameters)
            cmd.Add(name, value);
        cmd.Add("@now", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));   // 各 UPDATE 的 updated_at
        cmd.ExecuteNonQuery();
    }

    private static PushQueueItem Map(DbDataReader r)
    {
        return new PushQueueItem
        {
            Id = r.GetInt64(r.GetOrdinal("id")),
            Kind = r.GetString(r.GetOrdinal("kind")),
            ClientRef = r.GetString(r.GetOrdinal("client_ref")),
            Payload = r.GetString(r.GetOrdinal("payload")),
            Status = r.GetString(r.GetOrdinal("status")),
            Attempts = Convert.ToInt32(r.GetValue(r.GetOrdinal("attempts"))),
            NextAttempt = DateTime.TryParse(r.GetString(r.GetOrdinal("next_attempt")), out var t) ? t : default,
            LastError = GetString(r, "last_error"),
            Result = GetString(r, "result"),
        };
    }

    private static string GetString(DbDataReader r, string col)
    {
        int i = r.GetOrdinal(col);
        return r.IsDBNull(i) ? "" : r.GetString(i);
    }
}
