using System.Data.Common;
using CZRWeighSystem.Core;
using CZRWeighSystem.Utils;

namespace CZRWeighSystem.Database;

/// <summary>
/// 自定义字段定义仓储（t_custom_field）。
/// 双数据库兼容（SQLite / SQL Server）。
/// </summary>
public class CustomFieldRepository
{
    /// <summary>全部字段定义（按 SortOrder、Id 排序）。</summary>
    public List<CustomField> GetAll(bool enabledOnly = false)
    {
        using var conn = Db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = enabledOnly
            ? "SELECT id, name, field_type, options, required, sort_order, enabled FROM t_custom_field WHERE enabled=1 ORDER BY sort_order, id"
            : "SELECT id, name, field_type, options, required, sort_order, enabled FROM t_custom_field ORDER BY sort_order, id";
        using var reader = cmd.ExecuteReader();
        var list = new List<CustomField>();
        while (reader.Read())
        {
            list.Add(new CustomField
            {
                Id = Convert.ToInt64(reader.GetValue(0)),
                Name = reader.GetString(1),
                FieldType = reader.GetString(2),
                Options = reader.IsDBNull(3) ? null : reader.GetString(3),
                Required = Convert.ToInt64(reader.GetValue(4)) == 1,
                SortOrder = Convert.ToInt32(reader.GetValue(5)),
                Enabled = Convert.ToInt64(reader.GetValue(6)) == 1,
            });
        }
        return list;
    }

    /// <summary>新增或更新（按 Id 区分）。</summary>
    public (bool Ok, string Message) Save(CustomField f)
    {
        if (string.IsNullOrWhiteSpace(f.Name))
            return (false, "字段名不能为空");
        if (f.FieldType == "下拉" && f.OptionList.Length == 0)
            return (false, "下拉类型必须填写候选值（逗号分隔）");

        try
        {
            using var conn = Db.CreateConnection();
            using var cmd = conn.CreateCommand();
            if (f.Id == 0)
            {
                cmd.CommandText = """
                    INSERT INTO t_custom_field (name, field_type, options, required, sort_order, enabled)
                    VALUES (@name, @type, @options, @required, @sort, @enabled);
                    """ + Db.LastIdSql;
            }
            else
            {
                cmd.CommandText = """
                    UPDATE t_custom_field SET
                        name=@name, field_type=@type, options=@options,
                        required=@required, sort_order=@sort, enabled=@enabled
                    WHERE id=@id;
                    """ + Db.LastIdSql;
                cmd.Add("@id", f.Id);
            }
            cmd.Add("@name", f.Name.Trim());
            cmd.Add("@type", f.FieldType);
            cmd.Add("@options", string.IsNullOrWhiteSpace(f.Options) ? null : f.Options.Trim());
            cmd.Add("@required", f.Required ? 1 : 0);
            cmd.Add("@sort", f.SortOrder);
            cmd.Add("@enabled", f.Enabled ? 1 : 0);
            f.Id = Convert.ToInt64(cmd.ExecuteScalar());
            return (true, $"字段 [{f.Name}] 已保存");
        }
        catch (Exception ex)
        {
            Log.Error("保存自定义字段失败", ex);
            return (false, ex.Message.Contains("UNIQUE") || ex.Message.Contains("唯一")
                ? "字段名已存在" : "保存失败：" + ex.Message);
        }
    }

    /// <summary>删除字段定义（已有记录中的历史值保留，不再显示）。</summary>
    public (bool Ok, string Message) Delete(long id)
    {
        using var conn = Db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM t_custom_field WHERE id=@id";
        cmd.Add("@id", id);
        int n = cmd.ExecuteNonQuery();
        return n > 0 ? (true, "已删除") : (false, "记录不存在");
    }
}
