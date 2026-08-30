using System.Data.Common;
using CZRWeighSystem.Core;
using CZRWeighSystem.Utils;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;

namespace CZRWeighSystem.Database;

/// <summary>
/// 基础资料仓储（对应 PRD 5.9 基础资料管理）。
/// 覆盖车辆 / 货物 / 收发货单位三组档案的增删改查（F8-01~F8-03、F8-05）。
/// 双数据库兼容（SQLite / SQL Server）。
/// </summary>
public class BaseInfoRepository
{
    // ==================== 车辆档案 ====================

    public List<Vehicle> GetVehicles(bool onlyEnabled = false)
    {
        using var conn = Db.CreateConnection();
        var sql = "SELECT id, vehicle_no, default_tare_kg, owner, phone, enabled FROM t_vehicle";
        if (onlyEnabled) sql += " WHERE enabled=1";
        sql += " ORDER BY vehicle_no";

        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        using var r = cmd.ExecuteReader();
        var list = new List<Vehicle>();
        while (r.Read())
        {
            list.Add(new Vehicle
            {
                Id = Convert.ToInt64(r.GetValue(0)),
                VehicleNo = r.GetString(1),
                DefaultTareKg = r.IsDBNull(2) ? null : Convert.ToDouble(r.GetValue(2)),
                Owner = r.IsDBNull(3) ? null : r.GetString(3),
                Phone = r.IsDBNull(4) ? null : r.GetString(4),
                Enabled = Convert.ToInt64(r.GetValue(5)) == 1,
            });
        }
        return list;
    }

    public (bool Ok, string Message) SaveVehicle(Vehicle v)
    {
        if (string.IsNullOrWhiteSpace(v.VehicleNo))
            return (false, "车号不能为空");
        return Exec(
            "INSERT INTO t_vehicle (vehicle_no, default_tare_kg, owner, phone, enabled) VALUES (@name, @tare, @owner, @phone, @en)",
            "UPDATE t_vehicle SET vehicle_no=@name, default_tare_kg=@tare, owner=@owner, phone=@phone, enabled=@en WHERE id=@id",
            cmd =>
            {
                cmd.Add("@name", v.VehicleNo.Trim());
                cmd.Add("@tare", v.DefaultTareKg);
                cmd.Add("@owner", v.Owner);
                cmd.Add("@phone", v.Phone);
                cmd.Add("@en", v.Enabled ? 1 : 0);
                cmd.Add("@id", v.Id);
            }, v.Id == 0, "车辆");
    }

    public (bool Ok, string Message) DeleteVehicle(long id) =>
        ExecDelete("t_vehicle", id, "车辆");

    // ==================== 货物档案 ====================

    public List<GoodsInfo> GetGoods(bool onlyEnabled = false)
    {
        using var conn = Db.CreateConnection();
        var sql = "SELECT id, name, spec, unit, deduct_rate, enabled FROM t_goods";
        if (onlyEnabled) sql += " WHERE enabled=1";
        sql += " ORDER BY name";

        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        using var r = cmd.ExecuteReader();
        var list = new List<GoodsInfo>();
        while (r.Read())
        {
            list.Add(new GoodsInfo
            {
                Id = Convert.ToInt64(r.GetValue(0)),
                Name = r.GetString(1),
                Spec = r.IsDBNull(2) ? null : r.GetString(2),
                Unit = r.IsDBNull(3) ? null : r.GetString(3),
                DeductRate = r.IsDBNull(4) ? null : Convert.ToDouble(r.GetValue(4)),
                Enabled = Convert.ToInt64(r.GetValue(5)) == 1,
            });
        }
        return list;
    }

    public (bool Ok, string Message) SaveGoods(GoodsInfo g)
    {
        if (string.IsNullOrWhiteSpace(g.Name))
            return (false, "货物名称不能为空");
        return Exec(
            "INSERT INTO t_goods (name, spec, unit, deduct_rate, enabled) VALUES (@name, @spec, @unit, @rate, @en)",
            "UPDATE t_goods SET name=@name, spec=@spec, unit=@unit, deduct_rate=@rate, enabled=@en WHERE id=@id",
            cmd =>
            {
                cmd.Add("@name", g.Name.Trim());
                cmd.Add("@spec", g.Spec);
                cmd.Add("@unit", g.Unit);
                cmd.Add("@rate", g.DeductRate);
                cmd.Add("@en", g.Enabled ? 1 : 0);
                cmd.Add("@id", g.Id);
            }, g.Id == 0, "货物");
    }

    public (bool Ok, string Message) DeleteGoods(long id) =>
        ExecDelete("t_goods", id, "货物");

    // ==================== 收/发货单位档案 ====================

    public List<CompanyInfo> GetCompanies(bool onlyEnabled = false)
    {
        using var conn = Db.CreateConnection();
        var sql = "SELECT id, name, type, contact, phone, enabled FROM t_company";
        if (onlyEnabled) sql += " WHERE enabled=1";
        sql += " ORDER BY name";

        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        using var r = cmd.ExecuteReader();
        var list = new List<CompanyInfo>();
        while (r.Read())
        {
            list.Add(new CompanyInfo
            {
                Id = Convert.ToInt64(r.GetValue(0)),
                Name = r.GetString(1),
                Type = r.GetString(2),
                Contact = r.IsDBNull(3) ? null : r.GetString(3),
                Phone = r.IsDBNull(4) ? null : r.GetString(4),
                Enabled = Convert.ToInt64(r.GetValue(5)) == 1,
            });
        }
        return list;
    }

    public (bool Ok, string Message) SaveCompany(CompanyInfo c)
    {
        if (string.IsNullOrWhiteSpace(c.Name))
            return (false, "单位名称不能为空");
        return Exec(
            "INSERT INTO t_company (name, type, contact, phone, enabled) VALUES (@name, @type, @contact, @phone, @en)",
            "UPDATE t_company SET name=@name, type=@type, contact=@contact, phone=@phone, enabled=@en WHERE id=@id",
            cmd =>
            {
                cmd.Add("@name", c.Name.Trim());
                cmd.Add("@type", c.Type);
                cmd.Add("@contact", c.Contact);
                cmd.Add("@phone", c.Phone);
                cmd.Add("@en", c.Enabled ? 1 : 0);
                cmd.Add("@id", c.Id);
            }, c.Id == 0, "单位");
    }

    public (bool Ok, string Message) DeleteCompany(long id) =>
        ExecDelete("t_company", id, "单位");

    // ==================== 通用执行 ====================

    /// <summary>执行新增或更新（新增时读取自增 Id）。</summary>
    private static (bool Ok, string Message) Exec(string insertSql,
        string updateSql, Action<DbCommand> fill, bool isInsert, string label)
    {
        try
        {
            using var conn = Db.CreateConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = isInsert
                ? insertSql + ";" + Db.LastIdSql
                : updateSql;
            fill(cmd);
            if (isInsert)
                Convert.ToInt64(cmd.ExecuteScalar());
            else
                cmd.ExecuteNonQuery();
            Log.Info($"{label}档案已保存");
            return (true, $"{label}档案已保存");
        }
        catch (Exception ex) when (IsUniqueViolation(ex))
        {
            // 唯一键冲突（名称重复等）
            return (false, $"{label}名称已存在，不能重复");
        }
        catch (Exception ex)
        {
            Log.Error($"{label}档案保存失败", ex);
            return (false, $"保存失败：{ex.Message}");
        }
    }

    /// <summary>识别唯一键冲突（SQLite 错误码 19 / SQL Server 2601、2627）。</summary>
    private static bool IsUniqueViolation(Exception ex) => ex switch
    {
        SqliteException se => se.SqliteErrorCode == 19,
        SqlException se => se.Number is 2601 or 2627,
        _ => false,
    };

    /// <summary>按主键删除。</summary>
    private static (bool Ok, string Message) ExecDelete(string table, long id, string label)
    {
        try
        {
            using var conn = Db.CreateConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"DELETE FROM {table} WHERE id=@id";
            cmd.Add("@id", id);
            int n = cmd.ExecuteNonQuery();
            Log.Info($"{label}档案已删除 id={id}");
            return n > 0
                ? (true, $"{label}档案已删除")
                : (false, "记录不存在或已被删除");
        }
        catch (Exception ex)
        {
            Log.Error($"{label}档案删除失败", ex);
            return (false, $"删除失败：{ex.Message}");
        }
    }
}
