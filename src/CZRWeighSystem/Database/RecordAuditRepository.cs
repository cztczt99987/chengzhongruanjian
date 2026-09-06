using System.Data.Common;
using CZRWeighSystem.Core;
using CZRWeighSystem.Utils;

namespace CZRWeighSystem.Database;

/// <summary>
/// 称重记录作废留痕（对应 PRD F3-04）。
/// 记录不可修改；作废写入 t_record_audit，留痕只增不删；
/// 全程数据库事务保证"更新记录+写留痕"的原子性。
/// 双数据库兼容（SQLite / SQL Server）。
/// </summary>
public class RecordAuditRepository
{
    /// <summary>
    /// 作废记录（状态→已作废，留痕存作废原因）。
    /// </summary>
    public (bool Ok, string Message) VoidRecord(long recordId, string reason, string operatorName)
    {
        if (string.IsNullOrWhiteSpace(reason))
            return (false, "作废必须填写原因");

        using var conn = Db.CreateConnection();
        using var tx = conn.BeginTransaction();
        try
        {
            var record = GetById(conn, recordId);
            if (record == null) return (false, "记录不存在");
            if (record.Status == "已作废") return (false, "该记录已作废，不能重复作废");

            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "UPDATE t_weigh_record SET status='已作废' WHERE id=@id";
                cmd.Add("@id", recordId);
                cmd.ExecuteNonQuery();
            }

            InsertAudit(conn, tx, new RecordAudit
            {
                RecordId = recordId,
                Action = "作废",
                NewValue = reason.Trim(),
                Operator = operatorName,
                Time = DateTime.Now,
            });

            tx.Commit();
            Log.Info($"记录作废：{record.SerialNo} 原因：{reason} 操作人：{operatorName}");
            return (true, $"记录 {record.SerialNo} 已作废");
        }
        catch (Exception ex)
        {
            tx.Rollback();
            Log.Error("作废记录失败", ex);
            return (false, "作废失败：" + ex.Message);
        }
    }

    /// <summary>
    /// 查询某条记录的全部留痕（按时间正序）。
    /// </summary>
    public List<RecordAudit> GetAudits(long recordId)
    {
        using var conn = Db.CreateConnection();
        const string sql = """
            SELECT id, record_id, action, field, old_value, new_value, operator, time
            FROM t_record_audit WHERE record_id=@id ORDER BY id;
            """;
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Add("@id", recordId);
        using var reader = cmd.ExecuteReader();

        var list = new List<RecordAudit>();
        while (reader.Read())
        {
            list.Add(new RecordAudit
            {
                Id = Convert.ToInt64(reader.GetValue(0)),
                RecordId = Convert.ToInt64(reader.GetValue(1)),
                Action = reader.GetString(2),
                Field = reader.IsDBNull(3) ? null : reader.GetString(3),
                OldValue = reader.IsDBNull(4) ? null : reader.GetString(4),
                NewValue = reader.IsDBNull(5) ? null : reader.GetString(5),
                Operator = reader.IsDBNull(6) ? null : reader.GetString(6),
                Time = DateTime.TryParse(reader.GetString(7), out var t) ? t : default,
            });
        }
        return list;
    }

    // ---------- 内部工具 ----------

    private static WeighRecord? GetById(DbConnection conn, long id)
    {
        const string sql = "SELECT * FROM t_weigh_record WHERE id=@id";
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Add("@id", id);
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? MapRecord(reader) : null;
    }

    /// <summary>写留痕。</summary>
    private static void InsertAudit(DbConnection conn, DbTransaction tx, RecordAudit a)
    {
        const string sql = """
            INSERT INTO t_record_audit (record_id, action, field, old_value, new_value, operator, time)
            VALUES (@rid, @action, @field, @old, @new, @op, @time);
            """;
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        cmd.Add("@rid", a.RecordId);
        cmd.Add("@action", a.Action);
        cmd.Add("@field", a.Field);
        cmd.Add("@old", a.OldValue);
        cmd.Add("@new", a.NewValue);
        cmd.Add("@op", a.Operator);
        cmd.Add("@time", a.Time.ToString("yyyy-MM-dd HH:mm:ss"));
        cmd.ExecuteNonQuery();
    }

    /// <summary>记录映射（与 WeighRecordRepository.Map 一致的精简版，避免循环依赖）。</summary>
    private static WeighRecord MapRecord(DbDataReader reader)
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
            Status = reader.GetString(reader.GetOrdinal("status")),
            IsManual = Convert.ToInt64(reader.GetValue(reader.GetOrdinal("is_manual"))) == 1,
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
}
