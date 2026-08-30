using System.Data.Common;
using CZRWeighSystem.Core;
using CZRWeighSystem.Utils;

namespace CZRWeighSystem.Database;

/// <summary>
/// 称重记录仓储（对应 PRD 5.3 记录管理）。
/// 使用 DbConnection/DbCommand 基类编写，同时兼容 SQLite 与 SQL Server（网络版）。
/// </summary>
public class WeighRecordRepository
{
    /// <summary>
    /// 保存一次磅记录（状态：未完成）。
    /// </summary>
    public WeighRecord InsertFirstWeigh(WeighRecord r)
    {
        r.SerialNo = GenerateSerialNo(r.ScaleNo);
        r.FirstTime = DateTime.Now;
        r.Status = "未完成";

        using var conn = Db.CreateConnection();
        // 自增 Id 回读语句按数据库类型拼接（SQLite/SQL Server 方言不同）
        string sql = """
            INSERT INTO t_weigh_record
                (serial_no, scale_no, vehicle_no, goods, company, spec, batch_no, remark,
                 gross_kg, first_time, operator, status, is_manual, created_at)
            VALUES
                (@serial_no, @scale_no, @vehicle_no, @goods, @company, @spec, @batch_no, @remark,
                 @gross_kg, @first_time, @operator, @status, @is_manual, @created_at);
            """ + Db.LastIdSql;

        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        FillParams(cmd, r);
        r.Id = Convert.ToInt64(cmd.ExecuteScalar());
        return r;
    }

    /// <summary>
    /// 二次称重并结算：大者为毛重、小者为皮重（兼容先皮后毛与先毛后皮），
    /// 净重 = |一次重量 - 二次重量|（对应 PRD 4.1 规则 1）。
    /// </summary>
    public WeighRecord CompleteSecondWeigh(long id, double secondKg, string? operatorName)
    {
        using var conn = Db.CreateConnection();

        // 事务：读取一次磅 → 结算 → 更新，保证原子性（对应 PRD 6.2 可靠性）
        using var tx = conn.BeginTransaction();
        try
        {
            var first = GetById(conn, id);
            if (first == null)
                throw new InvalidOperationException("未找到指定的一次磅记录");
            if (first.Status != "未完成")
                throw new InvalidOperationException("该记录已完成配对，不能重复结算");

            double firstKg = first.GrossKg ?? 0;
            first.GrossKg = Math.Max(firstKg, secondKg);   // 毛重 = 大值
            first.TareKg = Math.Min(firstKg, secondKg);    // 皮重 = 小值
            first.NetKg = Math.Abs(firstKg - secondKg);    // 净重 = 差值
            first.SecondTime = DateTime.Now;
            first.Status = "已完成";
            first.Operator = operatorName ?? first.Operator;

            const string sql = """
                UPDATE t_weigh_record SET
                    gross_kg=@gross_kg, tare_kg=@tare_kg, net_kg=@net_kg,
                    second_time=@second_time, status=@status
                WHERE id=@id;
                """;
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = sql;
                cmd.Add("@gross_kg", first.GrossKg);
                cmd.Add("@tare_kg", first.TareKg);
                cmd.Add("@net_kg", first.NetKg);
                cmd.Add("@second_time", first.SecondTime?.ToString("yyyy-MM-dd HH:mm:ss"));
                cmd.Add("@status", first.Status);
                cmd.Add("@id", id);
                cmd.ExecuteNonQuery();
            }
            tx.Commit();
            return first;
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    /// <summary>
    /// 查询未完成的一次磅列表（对应主界面"一次磅未完成列表"）。
    /// </summary>
    public List<WeighRecord> GetUnfinishedList()
    {
        using var conn = Db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = Db.TopSql(
            "SELECT {TOP} * FROM t_weigh_record WHERE status='未完成' ",
            "ORDER BY id DESC", 200);
        using var reader = cmd.ExecuteReader();
        var list = new List<WeighRecord>();
        while (reader.Read()) list.Add(Map(reader));
        return list;
    }

    /// <summary>
    /// 多条件组合查询称重记录（对应 PRD F3-06）。
    /// </summary>
    public List<WeighRecord> Query(DateTime? from, DateTime? to, string vehicleNo,
        string goods, string company, string status)
    {
        // 动态拼接 WHERE 条件（全部使用参数化，防注入）
        var where = new List<string>();
        using var conn = Db.CreateConnection();
        using var cmd = conn.CreateCommand();

        if (from.HasValue)
        {
            where.Add("first_time >= @from");
            cmd.Add("@from", from.Value.ToString("yyyy-MM-dd 00:00:00"));
        }
        if (to.HasValue)
        {
            where.Add("first_time <= @to");
            cmd.Add("@to", to.Value.ToString("yyyy-MM-dd 23:59:59"));
        }
        if (!string.IsNullOrWhiteSpace(vehicleNo))
        {
            where.Add("vehicle_no LIKE @v");
            cmd.Add("@v", $"%{vehicleNo.Trim()}%");
        }
        if (!string.IsNullOrWhiteSpace(goods))
        {
            where.Add("goods LIKE @g");
            cmd.Add("@g", $"%{goods.Trim()}%");
        }
        if (!string.IsNullOrWhiteSpace(company))
        {
            where.Add("company LIKE @c");
            cmd.Add("@c", $"%{company.Trim()}%");
        }
        if (!string.IsNullOrWhiteSpace(status))
        {
            where.Add("status = @s");
            cmd.Add("@s", status);
        }

        string whereSql = where.Count == 0 ? "" : "WHERE " + string.Join(" AND ", where);
        cmd.CommandText = Db.TopSql(
            $"SELECT {{TOP}} * FROM t_weigh_record {whereSql} ", "ORDER BY id DESC", 5000);

        using var reader = cmd.ExecuteReader();
        var list = new List<WeighRecord>();
        while (reader.Read()) list.Add(Map(reader));
        return list;
    }

    /// <summary>
    /// 日结报表：按天汇总已完成记录的车数与毛/皮/净重（对应 PRD F4-02）。
    /// 日期分组在内存完成（substr/CONVERT 双方言不兼容），数据量在分页上限内。
    /// </summary>
    public List<ReportRow> SummaryDaily(DateTime from, DateTime to)
    {
        const string sql = """
            SELECT first_time, gross_kg, tare_kg, net_kg
            FROM t_weigh_record
            WHERE status='已完成' AND first_time >= @from AND first_time <= @to
            """;
        var rows = QueryRawRows(sql, from, to);

        return rows.GroupBy(r => (r.firstTime ?? "")[..Math.Min(10, (r.firstTime ?? " ").Length)])
            .OrderBy(g => g.Key)
            .Select(g => new ReportRow
            {
                Key = g.Key,
                Count = g.Count(),
                GrossKg = g.Sum(x => x.gross),
                TareKg = g.Sum(x => x.tare),
                NetKg = g.Sum(x => x.net),
            }).ToList();
    }

    /// <summary>
    /// 维度汇总报表：按货物/单位/车号/司磅员汇总（对应 PRD F4-03）。
    /// </summary>
    /// <param name="field">分组字段，仅允许 goods/company/vehicle_no/operator</param>
    public List<ReportRow> SummaryBy(string field, DateTime from, DateTime to)
    {
        // 字段白名单校验，防止 SQL 注入
        string col = field switch
        {
            "goods" => "goods",
            "company" => "company",
            "vehicle_no" => "vehicle_no",
            "operator" => "operator",
            _ => throw new ArgumentException("不支持的汇总字段：" + field),
        };

        // COALESCE 为 SQLite/SQL Server 双方言通用
        string sql = $"""
            SELECT COALESCE({col}, '(未填写)'), COUNT(*),
                   COALESCE(SUM(gross_kg),0), COALESCE(SUM(tare_kg),0), COALESCE(SUM(net_kg),0)
            FROM t_weigh_record
            WHERE status='已完成' AND first_time >= @from AND first_time <= @to
            GROUP BY {col} ORDER BY SUM(net_kg) DESC
            """;
        return QuerySummary(sql, from, to);
    }

    /// <summary>汇总查询私有实现。</summary>
    private static List<ReportRow> QuerySummary(string sql, DateTime from, DateTime to)
    {
        using var conn = Db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Add("@from", from.ToString("yyyy-MM-dd 00:00:00"));
        cmd.Add("@to", to.ToString("yyyy-MM-dd 23:59:59"));
        using var reader = cmd.ExecuteReader();

        var list = new List<ReportRow>();
        while (reader.Read())
        {
            list.Add(new ReportRow
            {
                Key = reader.GetString(0),
                Count = Convert.ToInt32(reader.GetValue(1)),
                GrossKg = Convert.ToDouble(reader.GetValue(2)),
                TareKg = Convert.ToDouble(reader.GetValue(3)),
                NetKg = Convert.ToDouble(reader.GetValue(4)),
            });
        }
        return list;
    }

    /// <summary>日结用原始行查询（日期分组移到内存，规避方言函数差异）。</summary>
    private static List<(string? firstTime, double gross, double tare, double net)> QueryRawRows(
        string sql, DateTime from, DateTime to)
    {
        using var conn = Db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Add("@from", from.ToString("yyyy-MM-dd 00:00:00"));
        cmd.Add("@to", to.ToString("yyyy-MM-dd 23:59:59"));
        using var reader = cmd.ExecuteReader();

        var list = new List<(string?, double, double, double)>();
        while (reader.Read())
        {
            list.Add((
                reader.IsDBNull(0) ? null : reader.GetString(0),
                reader.IsDBNull(1) ? 0 : Convert.ToDouble(reader.GetValue(1)),
                reader.IsDBNull(2) ? 0 : Convert.ToDouble(reader.GetValue(2)),
                reader.IsDBNull(3) ? 0 : Convert.ToDouble(reader.GetValue(3))));
        }
        return list;
    }

    /// <summary>
    /// 生成流水号：yyyyMMdd-磅号-序号（对应 PRD F3-02）。
    /// </summary>
    private static string GenerateSerialNo(string scaleNo)
    {
        string datePart = DateTime.Now.ToString("yyyyMMdd");
        string prefix = $"{datePart}-{scaleNo}-";

        using var conn = Db.CreateConnection();
        using var cmd = conn.CreateCommand();
        // "%" 拼进参数值而非 SQL 文本，避免 || 与 + 的方言连接符差异
        cmd.CommandText = "SELECT COUNT(*) FROM t_weigh_record WHERE serial_no LIKE @p";
        cmd.Add("@p", prefix + "%");
        long count = Convert.ToInt64(cmd.ExecuteScalar());
        return $"{prefix}{count + 1:D4}";
    }

    private static WeighRecord? GetById(DbConnection conn, long id)
    {
        const string sql = "SELECT * FROM t_weigh_record WHERE id=@id";
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Add("@id", id);
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    /// <summary>填充插入参数。</summary>
    private static void FillParams(DbCommand cmd, WeighRecord r)
    {
        cmd.Add("@serial_no", r.SerialNo);
        cmd.Add("@scale_no", r.ScaleNo);
        cmd.Add("@vehicle_no", r.VehicleNo);
        cmd.Add("@goods", r.Goods);
        cmd.Add("@company", r.Company);
        cmd.Add("@spec", r.Spec);
        cmd.Add("@batch_no", r.BatchNo);
        cmd.Add("@remark", r.Remark);
        cmd.Add("@gross_kg", r.GrossKg);
        cmd.Add("@first_time", r.FirstTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "");
        cmd.Add("@operator", r.Operator);
        cmd.Add("@status", r.Status);
        cmd.Add("@is_manual", r.IsManual ? 1 : 0);
        cmd.Add("@created_at", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
    }

    /// <summary>数据行映射为对象。</summary>
    private static WeighRecord Map(DbDataReader reader)
    {
        return new WeighRecord
        {
            Id = reader.GetInt64(reader.GetOrdinal("id")),
            SerialNo = reader.GetString(reader.GetOrdinal("serial_no")),
            ScaleNo = reader.GetString(reader.GetOrdinal("scale_no")),
            VehicleNo = reader.GetString(reader.GetOrdinal("vehicle_no")),
            Goods = GetStr(reader, "goods"),
            Company = GetStr(reader, "company"),
            Spec = GetStr(reader, "spec"),
            BatchNo = GetStr(reader, "batch_no"),
            Remark = GetStr(reader, "remark"),
            GrossKg = GetDbl(reader, "gross_kg"),
            TareKg = GetDbl(reader, "tare_kg"),
            NetKg = GetDbl(reader, "net_kg"),
            FirstTime = ParseTime(reader, "first_time"),
            SecondTime = ParseTime(reader, "second_time"),
            Operator = GetStr(reader, "operator"),
            Status = reader.GetString(reader.GetOrdinal("status")),
            IsManual = Convert.ToInt64(reader.GetValue(reader.GetOrdinal("is_manual"))) == 1,
        };
    }

    private static string? GetStr(DbDataReader r, string col)
    {
        int i = r.GetOrdinal(col);
        return r.IsDBNull(i) ? null : r.GetString(i);
    }

    private static double? GetDbl(DbDataReader r, string col)
    {
        int i = r.GetOrdinal(col);
        return r.IsDBNull(i) ? null : Convert.ToDouble(r.GetValue(i));
    }

    private static DateTime? ParseTime(DbDataReader reader, string col)
    {
        int i = reader.GetOrdinal(col);
        if (reader.IsDBNull(i)) return null;
        return DateTime.TryParse(reader.GetString(i), out var t) ? t : null;
    }
}
