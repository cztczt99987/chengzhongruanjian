using System.Data.Common;
using CZRWeighSystem.Core;
using CZRWeighSystem.Utils;

namespace CZRWeighSystem.Database;

/// <summary>
/// 称重记录作废与修改留痕（对应 PRD F3-03/F3-04）。
/// 修改与作废均写入 t_record_audit，留痕只增不删；
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
    /// 修改记录（管理员操作）：逐字段比对生成留痕；
    /// 毛重/皮重变化时自动重算净重（净重=|毛-皮|）并留痕。
    /// </summary>
    public (bool Ok, string Message) UpdateRecord(long recordId, RecordEditValues v, string operatorName)
    {
        using var conn = Db.CreateConnection();
        using var tx = conn.BeginTransaction();
        try
        {
            var old = GetById(conn, recordId);
            if (old == null) return (false, "记录不存在");
            if (old.Status == "已作废") return (false, "已作废记录不能修改");

            var now = DateTime.Now;
            int auditCount = 0;

            // ---- 文本字段比对 ----
            void CheckText(string field, string label, string? oldVal, string? newVal)
            {
                newVal = string.IsNullOrWhiteSpace(newVal) ? null : newVal.Trim();
                if ((oldVal ?? "") == (newVal ?? "")) return;
                UpdateField(conn, tx, recordId, field, newVal);
                InsertAudit(conn, tx, new RecordAudit
                {
                    RecordId = recordId, Action = "修改", Field = label,
                    OldValue = oldVal, NewValue = newVal,
                    Operator = operatorName, Time = now,
                });
                auditCount++;
            }

            CheckText("vehicle_no", "车号", old.VehicleNo, v.VehicleNo);
            CheckText("goods", "货物", old.Goods, v.Goods);
            CheckText("company", "单位", old.Company, v.Company);
            CheckText("spec", "规格", old.Spec, v.Spec);
            CheckText("batch_no", "批次", old.BatchNo, v.BatchNo);
            CheckText("remark", "备注", old.Remark, v.Remark);

            // ---- 重量字段比对（改毛/皮自动重算净重）----
            double oldGross = old.GrossKg ?? 0, oldTare = old.TareKg ?? 0;
            if (Math.Abs(v.GrossKg - oldGross) > 0.001)
            {
                UpdateField(conn, tx, recordId, "gross_kg", v.GrossKg);
                InsertAudit(conn, tx, new RecordAudit
                {
                    RecordId = recordId, Action = "修改", Field = "毛重",
                    OldValue = oldGross.ToString(), NewValue = v.GrossKg.ToString(),
                    Operator = operatorName, Time = now,
                });
                auditCount++;
            }
            if (Math.Abs(v.TareKg - oldTare) > 0.001)
            {
                UpdateField(conn, tx, recordId, "tare_kg", v.TareKg);
                InsertAudit(conn, tx, new RecordAudit
                {
                    RecordId = recordId, Action = "修改", Field = "皮重",
                    OldValue = oldTare.ToString(), NewValue = v.TareKg.ToString(),
                    Operator = operatorName, Time = now,
                });
                auditCount++;
            }

            // 净重跟随重算（有任一重量修改时）
            if (Math.Abs(v.GrossKg - oldGross) > 0.001 || Math.Abs(v.TareKg - oldTare) > 0.001)
            {
                double net = Math.Abs(v.GrossKg - v.TareKg);
                UpdateField(conn, tx, recordId, "net_kg", net);
                InsertAudit(conn, tx, new RecordAudit
                {
                    RecordId = recordId, Action = "修改", Field = "净重",
                    OldValue = old.NetKg?.ToString(), NewValue = net.ToString(),
                    Operator = operatorName, Time = now,
                });
                auditCount++;
            }

            if (auditCount == 0)
            {
                tx.Rollback();
                return (false, "没有需要修改的内容");
            }

            tx.Commit();
            Log.Info($"记录修改：recordId={recordId} 共 {auditCount} 处变更，操作人：{operatorName}");
            return (true, $"修改成功（{auditCount} 处变更已留痕）");
        }
        catch (Exception ex)
        {
            tx.Rollback();
            Log.Error("修改记录失败", ex);
            return (false, "修改失败：" + ex.Message);
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

    /// <summary>更新单字段。</summary>
    private static void UpdateField(DbConnection conn, DbTransaction tx,
        long id, string field, object? value)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = $"UPDATE t_weigh_record SET {field}=@v WHERE id=@id";
        cmd.Add("@v", value);
        cmd.Add("@id", id);
        cmd.ExecuteNonQuery();
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
            Company = GetStr(reader, "company"),
            Spec = GetStr(reader, "spec"),
            BatchNo = GetStr(reader, "batch_no"),
            Remark = GetStr(reader, "remark"),
            GrossKg = GetDbl(reader, "gross_kg"),
            TareKg = GetDbl(reader, "tare_kg"),
            NetKg = GetDbl(reader, "net_kg"),
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
}

/// <summary>
/// 记录修改的可编辑字段值（供 UpdateRecord 使用）。
/// </summary>
public class RecordEditValues
{
    public string VehicleNo { get; set; } = "";
    public string? Goods { get; set; }
    public string? Company { get; set; }
    public string? Spec { get; set; }
    public string? BatchNo { get; set; }
    public string? Remark { get; set; }
    /// <summary>毛重（千克）</summary>
    public double GrossKg { get; set; }
    /// <summary>皮重（千克）</summary>
    public double TareKg { get; set; }
}
