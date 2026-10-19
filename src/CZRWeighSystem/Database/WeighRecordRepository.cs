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
                (serial_no, scale_no, vehicle_no, goods, sender_company, receiver_company,
                 deduct_kg, spec, batch_no, remark,
                 gross_kg, tare_kg, first_time, first_is_tare, operator, status, is_manual, ext_json, created_at)
            VALUES
                (@serial_no, @scale_no, @vehicle_no, @goods, @sender_company, @receiver_company,
                 @deduct_kg, @spec, @batch_no, @remark,
                 @gross_kg, @tare_kg, @first_time, @first_is_tare, @operator, @status, @is_manual, @ext_json, @created_at);
            """ + Db.LastIdSql;

        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        FillParams(cmd, r);
        r.Id = Convert.ToInt64(cmd.ExecuteScalar());
        return r;
    }

    /// <summary>
    /// 二次称重并结算：大者为毛重、小者为皮重（兼容先皮后毛与先毛后皮），
    /// 净重 = |一次重量 - 二次重量|（对应 PRD 4.1 规则 1）；
    /// deductKg 非空时同时更新扣重（实收 = 净重 - 扣重，在显示/打印层计算）；
    /// amountYuan 为按收费分段算出的应收金额（元），null = 未启用收费（不写金额）。
    /// </summary>
    public WeighRecord CompleteSecondWeigh(long id, double secondKg,
        double? deductKg, string? operatorName, decimal? amountYuan = null)
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

            double firstKg = first.FirstWeightKg ?? 0;    // 一次磅重量（可能暂存于毛重列或皮重列）
            first.GrossKg = Math.Max(firstKg, secondKg);   // 毛重 = 大值
            first.TareKg = Math.Min(firstKg, secondKg);    // 皮重 = 小值
            first.NetKg = Math.Abs(firstKg - secondKg);    // 净重 = 差值
            if (deductKg.HasValue) first.DeductKg = deductKg;
            first.AmountYuan = amountYuan;
            first.SecondTime = DateTime.Now;
            first.Status = "已完成";
            first.Operator = operatorName ?? first.Operator;

            const string sql = """
                UPDATE t_weigh_record SET
                    gross_kg=@gross_kg, tare_kg=@tare_kg, net_kg=@net_kg, deduct_kg=@deduct_kg,
                    amount_yuan=@amount_yuan, second_time=@second_time, status=@status
                WHERE id=@id;
                """;
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = sql;
                cmd.Add("@gross_kg", first.GrossKg);
                cmd.Add("@tare_kg", first.TareKg);
                cmd.Add("@net_kg", first.NetKg);
                cmd.Add("@deduct_kg", first.DeductKg);
                cmd.Add("@amount_yuan", first.AmountYuan.HasValue ? (double)first.AmountYuan.Value : null);
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
    /// 覆盖未完成一次磅的重量（同类型重复称重时使用：两次都是皮重→更新皮重列，两次都是毛重→更新毛重列）。
    /// 不触发结算，记录仍为"未完成"状态。
    /// </summary>
    public WeighRecord OverwriteFirstWeight(long id, double weightKg, bool isTare)
    {
        using var conn = Db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = isTare
            ? "UPDATE t_weigh_record SET tare_kg=@kg, first_time=@time, first_is_tare=1 WHERE id=@id AND status='未完成'"
            : "UPDATE t_weigh_record SET gross_kg=@kg, first_time=@time, first_is_tare=0 WHERE id=@id AND status='未完成'";
        cmd.Add("@kg", weightKg);
        cmd.Add("@time", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        cmd.Add("@id", id);
        int rows = cmd.ExecuteNonQuery();
        if (rows == 0)
            throw new InvalidOperationException("未找到未完成的一次磅记录，覆盖失败");

        return GetById(conn, id)!;
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
    /// 查询指定车号最近一次"空车去皮"皮重：取全部历史中最近一条已完成且有皮重的记录
    /// （空车去皮弹窗显示上次重量/时间，并据其与本次皮重的差值做超差预警）。
    /// 本站为先皮后毛流程，皮重即一次磅重量，皮重时间取 FirstTime。无记录返回 null。
    /// </summary>
    public WeighRecord? GetLastCompletedTare(string vehicleNo)
    {
        using var conn = Db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = Db.TopSql(
            "SELECT {TOP} * FROM t_weigh_record " +
            "WHERE status='已完成' AND tare_kg IS NOT NULL AND vehicle_no=@vehicle_no ",
            "ORDER BY id DESC", 1);
        cmd.Add("@vehicle_no", vehicleNo);
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    /// <summary>
    /// 按车号查最新一条未作废记录（用于输入车号时自动回填品种/货主/单价等）。
    /// 直接按车号定位、取一条，不受 Query 的 5000 条上限影响——
    /// 即使该车号只在很久前出现过，也能正确回填其上一次的信息。
    /// </summary>
    public WeighRecord? GetLatestByVehicleNo(string vehicleNo)
    {
        using var conn = Db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = Db.TopSql(
            "SELECT {TOP} * FROM t_weigh_record " +
            "WHERE vehicle_no=@vehicle_no AND status != '已作废' ",
            "ORDER BY COALESCE(second_time, first_time) DESC, id DESC", 1);
        cmd.Add("@vehicle_no", vehicleNo);
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    /// <summary>
    /// 查询所有未作废记录的车号（去重），供车牌输入框自动补全下拉使用。
    /// prefix 非空时只返回以该前缀（省份简称）开头的车号；不受 5000 条上限影响。
    /// </summary>
    public List<string> GetAllVehicleNos(string? prefix = null)
    {
        using var conn = Db.CreateConnection();
        using var cmd = conn.CreateCommand();
        string where = "status != '已作废'";
        if (!string.IsNullOrEmpty(prefix))
        {
            where += " AND vehicle_no LIKE @prefix";
            cmd.Add("@prefix", prefix + "%");
        }
        cmd.CommandText = $"SELECT DISTINCT vehicle_no FROM t_weigh_record WHERE {where} ORDER BY vehicle_no";
        var list = new List<string>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            list.Add(reader.GetString(0));
        return list;
    }

    /// <summary>
    /// 首页统计：全部记录数、今日磅单数、今日毛/皮/净各测了多少车（对应重量非空的今日记录数）、
    /// 今日付称车数（只称了重车、尚未回空车，即 gross 有值而 tare 为空）与
    /// 今日应收金额合计（元，仅已完成记录）。CASE WHEN 为 SQLite / SQL Server 双方言通用。
    /// </summary>
    public (int TotalCount, int TodayCount, int GrossCars, int TareCars, int NetCars,
             int OnlyGrossCars, decimal TodayAmountYuan)
        GetHomeStats()
    {
        using var conn = Db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT COUNT(*),
                   COALESCE(SUM(CASE WHEN first_time >= @from AND status = '已完成' THEN 1 ELSE 0 END), 0),
                   COALESCE(SUM(CASE WHEN first_time >= @from AND gross_kg IS NOT NULL THEN 1 ELSE 0 END), 0),
                   COALESCE(SUM(CASE WHEN first_time >= @from AND tare_kg IS NOT NULL THEN 1 ELSE 0 END), 0),
                   COALESCE(SUM(CASE WHEN first_time >= @from AND net_kg IS NOT NULL THEN 1 ELSE 0 END), 0),
                   COALESCE(SUM(CASE WHEN first_time >= @from AND gross_kg IS NOT NULL AND tare_kg IS NULL THEN 1 ELSE 0 END), 0),
                   COALESCE(SUM(CASE WHEN first_time >= @from AND status = '已完成' THEN amount_yuan ELSE 0 END), 0)
            FROM t_weigh_record
            """;
        cmd.Add("@from", DateTime.Now.ToString("yyyy-MM-dd 00:00:00"));
        using var reader = cmd.ExecuteReader();
        reader.Read();
        return (
            Convert.ToInt32(reader.GetValue(0)),
            Convert.ToInt32(reader.GetValue(1)),
            Convert.ToInt32(reader.GetValue(2)),
            Convert.ToInt32(reader.GetValue(3)),
            Convert.ToInt32(reader.GetValue(4)),
            Convert.ToInt32(reader.GetValue(5)),
            Convert.ToDecimal(reader.GetValue(6)));
    }

    /// <summary>
    /// 删除超过天数仍未配对（未完成）的一次磅记录（首次称重超期自动清理）。
    /// first_time 为 yyyy-MM-dd HH:mm:ss 文本，字符串比较与时间比较等价（双方言通用）。
    /// 返回删除条数；days <= 0 时不清理。
    /// </summary>
    public int DeleteExpiredUnfinished(int days)
    {
        if (days <= 0) return 0;

        using var conn = Db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM t_weigh_record WHERE status='未完成' AND first_time <= @cutoff";
        cmd.Add("@cutoff", DateTime.Now.AddDays(-days).ToString("yyyy-MM-dd HH:mm:ss"));
        return cmd.ExecuteNonQuery();
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
            // 单位条件同时匹配发货/收货单位
            where.Add("(sender_company LIKE @c OR receiver_company LIKE @c)");
            cmd.Add("@c", $"%{company.Trim()}%");
        }
        if (!string.IsNullOrWhiteSpace(status))
        {
            where.Add("status = @s");
            cmd.Add("@s", status);
        }

        string whereSql = where.Count == 0 ? "" : "WHERE " + string.Join(" AND ", where);
        // 按"最后操作时间"倒序：一次磅按 first_time、二次磅完成按 second_time，
        // 保证每次保存后该记录都浮到最上面（旧 ORDER BY id DESC 无法让二次磅更新的记录上浮）。
        cmd.CommandText = Db.TopSql(
            $"SELECT {{TOP}} * FROM t_weigh_record {whereSql} ",
            "ORDER BY COALESCE(second_time, first_time) DESC, id DESC", 5000);

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

    /// <summary>维度汇总报表：按货物/发货单位/收货单位/车号/司磅员汇总（对应 PRD F4-03）。</summary>
    /// <param name="field">分组字段，仅允许 goods/sender_company/receiver_company/vehicle_no/operator</param>
    public List<ReportRow> SummaryBy(string field, DateTime from, DateTime to)
    {
        // 字段白名单校验，防止 SQL 注入
        string col = field switch
        {
            "goods" => "goods",
            "sender_company" => "sender_company",
            "receiver_company" => "receiver_company",
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
        // "%" 拼进参数值而非 SQL 文本，避免 || 与 + 的方言连接符差异；
        // 序号取同前缀最大序号 + 1（按行数编号时删除记录产生空洞会撞已有流水号，
        // UNIQUE 冲突导致保存失败，见 SerialNoTests 回归）
        cmd.CommandText = "SELECT serial_no FROM t_weigh_record WHERE serial_no LIKE @p";
        cmd.Add("@p", prefix + "%");
        int max = 0;
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            string serial = reader.GetString(0);
            if (serial.Length > prefix.Length
                && int.TryParse(serial[prefix.Length..], out int seq) && seq > max)
                max = seq;
        }
        return $"{prefix}{max + 1:D4}";
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

    /// <summary>按流水号查询记录（平台推送入队/作废同步用），无则返回 null。</summary>
    public WeighRecord? GetBySerialNo(string serialNo)
    {
        using var conn = Db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM t_weigh_record WHERE serial_no=@s";
        cmd.Add("@s", serialNo);
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    /// <summary>
    /// 打印次数 +1（磅单打印成功后调用，综合设置开启"打印次数"累计时），返回累计后的次数。
    /// </summary>
    public int IncrementPrintCount(long id)
    {
        using var conn = Db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE t_weigh_record SET print_count = print_count + 1 WHERE id=@id";
        cmd.Add("@id", id);
        cmd.ExecuteNonQuery();

        using var get = conn.CreateCommand();
        get.CommandText = "SELECT print_count FROM t_weigh_record WHERE id=@id";
        get.Add("@id", id);
        return Convert.ToInt32(get.ExecuteScalar());
    }

    /// <summary>
    /// 查询同车号最近一条有二次磅时间的记录时间（严格禁止 X 分钟内二次称重用）。
    /// 无记录返回 null。
    /// </summary>
    public DateTime? GetLastFinishedTime(string vehicleNo)
    {
        using var conn = Db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT MAX(second_time) FROM t_weigh_record
            WHERE vehicle_no=@no AND status='已完成' AND second_time IS NOT NULL AND second_time <> ''
            """;
        cmd.Add("@no", vehicleNo);
        var val = cmd.ExecuteScalar();
        return val is string s && DateTime.TryParse(s, out var t) ? t : null;
    }

    /// <summary>填充插入参数。</summary>
    private static void FillParams(DbCommand cmd, WeighRecord r)
    {
        cmd.Add("@serial_no", r.SerialNo);
        cmd.Add("@scale_no", r.ScaleNo);
        cmd.Add("@vehicle_no", r.VehicleNo);
        cmd.Add("@goods", r.Goods);
        cmd.Add("@sender_company", r.SenderCompany);
        cmd.Add("@receiver_company", r.ReceiverCompany);
        cmd.Add("@deduct_kg", r.DeductKg);
        cmd.Add("@spec", r.Spec);
        cmd.Add("@batch_no", r.BatchNo);
        cmd.Add("@remark", r.Remark);
        cmd.Add("@gross_kg", r.GrossKg);
        cmd.Add("@tare_kg", r.TareKg);
        cmd.Add("@first_time", r.FirstTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "");
        cmd.Add("@first_is_tare", r.FirstIsTare ? 1 : 0);
        cmd.Add("@operator", r.Operator);
        cmd.Add("@status", r.Status);
        cmd.Add("@is_manual", r.IsManual ? 1 : 0);
        cmd.Add("@ext_json", r.ExtJson);
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
            SenderCompany = GetStr(reader, "sender_company"),
            ReceiverCompany = GetStr(reader, "receiver_company"),
            DeductKg = GetDbl(reader, "deduct_kg"),
            Spec = GetStr(reader, "spec"),
            BatchNo = GetStr(reader, "batch_no"),
            Remark = GetStr(reader, "remark"),
            GrossKg = GetDbl(reader, "gross_kg"),
            TareKg = GetDbl(reader, "tare_kg"),
            NetKg = GetDbl(reader, "net_kg"),
            AmountYuan = GetDec(reader, "amount_yuan"),
            FirstTime = ParseTime(reader, "first_time"),
            SecondTime = ParseTime(reader, "second_time"),
            FirstIsTare = Convert.ToInt64(reader.GetValue(reader.GetOrdinal("first_is_tare"))) == 1,
            Operator = GetStr(reader, "operator"),
            Status = reader.GetString(reader.GetOrdinal("status")),
            IsManual = Convert.ToInt64(reader.GetValue(reader.GetOrdinal("is_manual"))) == 1,
            PrintCount = Convert.ToInt32(reader.GetValue(reader.GetOrdinal("print_count"))),
            ExtJson = GetStr(reader, "ext_json"),
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

    /// <summary>读取金额列（元）；空值返回 null（数据库以 REAL/FLOAT 存储，读回转 decimal）。</summary>
    private static decimal? GetDec(DbDataReader r, string col)
    {
        int i = r.GetOrdinal(col);
        return r.IsDBNull(i) ? null : Convert.ToDecimal(r.GetValue(i));
    }

    private static DateTime? ParseTime(DbDataReader reader, string col)
    {
        int i = reader.GetOrdinal(col);
        if (reader.IsDBNull(i)) return null;
        return DateTime.TryParse(reader.GetString(i), out var t) ? t : null;
    }
}
