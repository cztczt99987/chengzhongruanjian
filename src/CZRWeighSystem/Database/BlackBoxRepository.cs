using System.Data.Common;
using CZRWeighSystem.Core;
using CZRWeighSystem.Utils;

namespace CZRWeighSystem.Database;

/// <summary>
/// 黑匣子记录仓储（t_black_box）。
/// 使用 DbConnection/DbCommand 基类编写，同时兼容 SQLite 与 SQL Server（网络版）。
/// </summary>
public class BlackBoxRepository
{
    /// <summary>保存一条黑匣子记录。</summary>
    public BlackBoxRecord Insert(BlackBoxRecord r)
    {
        using var conn = Db.CreateConnection();
        const string sql = """
            INSERT INTO t_black_box (time, weight_kg, images)
            VALUES (@time, @weight_kg, @images);
            """;
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql + Db.LastIdSql;
        cmd.Add("@time", r.Time.ToString("yyyy-MM-dd HH:mm:ss"));
        cmd.Add("@weight_kg", r.WeightKg);
        cmd.Add("@images", r.ImagesJson);
        r.Id = Convert.ToInt64(cmd.ExecuteScalar());
        return r;
    }

    /// <summary>
    /// 多条件组合查询黑匣子记录：日期范围 + 重量上下限（均为可选）。
    /// </summary>
    public List<BlackBoxRecord> Query(DateTime? from, DateTime? to, double? minKg, double? maxKg)
    {
        var where = new List<string>();
        using var conn = Db.CreateConnection();
        using var cmd = conn.CreateCommand();

        if (from.HasValue)
        {
            where.Add("time >= @from");
            cmd.Add("@from", from.Value.ToString("yyyy-MM-dd 00:00:00"));
        }
        if (to.HasValue)
        {
            where.Add("time <= @to");
            cmd.Add("@to", to.Value.ToString("yyyy-MM-dd 23:59:59"));
        }
        if (minKg.HasValue)
        {
            where.Add("weight_kg >= @minkg");
            cmd.Add("@minkg", minKg.Value);
        }
        if (maxKg.HasValue)
        {
            where.Add("weight_kg <= @maxkg");
            cmd.Add("@maxkg", maxKg.Value);
        }

        string whereSql = where.Count == 0 ? "" : "WHERE " + string.Join(" AND ", where);
        cmd.CommandText = Db.TopSql(
            $"SELECT {{TOP}} * FROM t_black_box {whereSql} ", "ORDER BY id DESC", 5000);

        using var reader = cmd.ExecuteReader();
        var list = new List<BlackBoxRecord>();
        while (reader.Read())
        {
            list.Add(new BlackBoxRecord
            {
                Id = reader.GetInt64(reader.GetOrdinal("id")),
                Time = DateTime.Parse(reader.GetString(reader.GetOrdinal("time"))),
                WeightKg = Convert.ToDouble(reader.GetValue(reader.GetOrdinal("weight_kg"))),
                ImagesJson = GetStr(reader, "images"),
            });
        }
        return list;
    }

    private static string? GetStr(DbDataReader r, string col)
    {
        int i = r.GetOrdinal(col);
        return r.IsDBNull(i) ? null : r.GetString(i);
    }
}
