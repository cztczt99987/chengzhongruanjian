using CZRWeighSystem.Database;
using Microsoft.Data.Sqlite;

namespace CZRWeighSystem.Utils;

/// <summary>
/// 数据备份服务（对应 PRD F10-02 手动+定时备份、F10-03 恢复）。
/// 使用 SQLite BackupDatabase API，复制期间数据一致性有保障。
/// 注意：仅用于单机版（SQLite）；SQL Server 网络版的数据备份由数据库服务器自身机制负责。
/// </summary>
public static class BackupService
{
    /// <summary>备份文件目录（exe 目录下 backups/）</summary>
    public static string BackupDir => Path.Combine(AppContext.BaseDirectory, "backups");

    /// <summary>
    /// 备份数据库到指定文件。
    /// </summary>
    public static (bool Ok, string Message) BackupTo(string targetPath)
    {
        if (Db.IsSqlServer)
            return (false, "网络版数据由 SQL Server 服务器统一备份，无需本地备份");

        try
        {
            using var source = (SqliteConnection)Db.CreateConnection();
            using var dest = new SqliteConnection(
                new SqliteConnectionStringBuilder { DataSource = targetPath }.ToString());
            dest.Open();
            source.BackupDatabase(dest);
            Log.Info($"数据库备份完成：{targetPath}");
            return (true, $"备份完成：{targetPath}");
        }
        catch (Exception ex)
        {
            Log.Error("数据库备份失败", ex);
            return (false, "备份失败：" + ex.Message);
        }
    }

    /// <summary>
    /// 从备份文件恢复（覆盖当前数据库），恢复后需重启程序生效。
    /// </summary>
    public static (bool Ok, string Message) RestoreFrom(string sourcePath)
    {
        if (Db.IsSqlServer)
            return (false, "网络版数据由 SQL Server 服务器统一管理，不支持本地恢复");

        try
        {
            using var source = new SqliteConnection(
                new SqliteConnectionStringBuilder { DataSource = sourcePath }.ToString());
            source.Open();
            using var dest = (SqliteConnection)Db.CreateConnection();
            source.BackupDatabase(dest);
            Log.Info($"数据库已从备份恢复：{sourcePath}");
            return (true, "恢复完成，请重启程序使数据完全生效");
        }
        catch (Exception ex)
        {
            Log.Error("数据库恢复失败", ex);
            return (false, "恢复失败：" + ex.Message);
        }
    }

    /// <summary>
    /// 按配置检查并执行自动备份（超过间隔天数才备份），并清理超量旧备份。
    /// 主窗体加载与定时器中调用。
    /// </summary>
    public static void AutoBackupIfNeeded()
    {
        var cfg = AppConfig.Current;
        if (!cfg.AutoBackupEnabled || Db.IsSqlServer) return;

        try
        {
            Directory.CreateDirectory(BackupDir);
            var latest = Directory.GetFiles(BackupDir, "backup_*.db")
                .Select(f => File.GetLastWriteTime(f))
                .DefaultIfEmpty(DateTime.MinValue)
                .Max();

            if ((DateTime.Now - latest).TotalDays < cfg.AutoBackupDays) return;

            string file = Path.Combine(BackupDir, $"backup_{DateTime.Now:yyyyMMdd_HHmmss}.db");
            var (ok, _) = BackupTo(file);
            if (!ok) return;

            Log.Info($"自动备份完成：{file}");
            CleanupOldBackups(cfg.AutoBackupKeep);
        }
        catch (Exception ex)
        {
            Log.Error("自动备份异常", ex);
        }
    }

    /// <summary>删除超出保留份数的最旧备份。</summary>
    private static void CleanupOldBackups(int keep)
    {
        var files = Directory.GetFiles(BackupDir, "backup_*.db")
            .OrderByDescending(f => File.GetLastWriteTime(f))
            .ToList();
        for (int i = keep; i < files.Count; i++)
        {
            try
            {
                File.Delete(files[i]);
                Log.Info($"清理过期备份：{files[i]}");
            }
            catch { /* 删除失败不影响主流程 */ }
        }
    }
}
