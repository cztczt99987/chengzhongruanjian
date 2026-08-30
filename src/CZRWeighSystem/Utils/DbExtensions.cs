using System.Data.Common;

namespace CZRWeighSystem.Utils;

/// <summary>
/// DbCommand 参数扩展（对应 PRD F9-05 双数据库支持）。
/// DbParameterCollection 基类没有 AddWithValue，用扩展方法统一
/// SQLite / SQL Server 两种驱动的参数添加写法。
/// </summary>
public static class DbExtensions
{
    /// <summary>添加一个参数（null 自动转 DBNull）。</summary>
    public static DbCommand Add(this DbCommand cmd, string name, object? value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value ?? DBNull.Value;
        cmd.Parameters.Add(p);
        return cmd;
    }

    /// <summary>执行查询并将首行映射为读取器（简化 using 书写）。</summary>
    public static int IntScalar(this DbCommand cmd)
        => Convert.ToInt64(cmd.ExecuteScalar()) switch { long l => (int)l };
}
