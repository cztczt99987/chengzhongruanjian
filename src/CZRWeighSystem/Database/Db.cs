using System.Data.Common;
using CZRWeighSystem.Utils;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;

namespace CZRWeighSystem.Database;

/// <summary>
/// 数据库入口（对应 PRD F9-05 数据库设置、第 11 章 V1.1 网络版）。
/// 通过配置在两种数据库间切换：
///   Sqlite    —— 单机版：exe 目录 data/weigh.db，免安装
///   SqlServer —— 网络版：多客户端共享同一数据库（连接串见 config.json）
/// 全部仓储层使用 DbConnection/DbCommand 基类编写，双方言通用。
/// </summary>
public static class Db
{
    /// <summary>是否为 SQL Server 网络版</summary>
    public static bool IsSqlServer =>
        string.Equals(AppConfig.Current.DatabaseType, "SqlServer", StringComparison.OrdinalIgnoreCase);

    /// <summary>SQLite 数据库文件路径（单机版）</summary>
    public static string DbPath => Path.Combine(AppContext.BaseDirectory, "data", "weigh.db");

    /// <summary>
    /// 创建数据库连接（调用方负责 using 释放；网络版指向 SQL Server，单机版指向 SQLite）。
    /// </summary>
    public static DbConnection CreateConnection()
    {
        DbConnection conn = IsSqlServer
            ? new SqlConnection(AppConfig.Current.SqlServerConnectionString)
            : new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = DbPath,
                Mode = SqliteOpenMode.ReadWriteCreate,
            }.ToString());
        conn.Open();
        return conn;
    }

    /// <summary>
    /// 读取自增 Id：INSERT 后紧接执行（同命令文本）。
    /// SQLite 用 last_insert_rowid()，SQL Server 用 SCOPE_IDENTITY()。
    /// </summary>
    public static string LastIdSql =>
        IsSqlServer ? "SELECT CAST(SCOPE_IDENTITY() AS BIGINT);" : "SELECT last_insert_rowid();";

    /// <summary>
    /// 限制行数：SQLite 用 LIMIT n（放句尾），SQL Server 用 SELECT TOP (n)。
    /// </summary>
    public static string TopSql(string selectSql, string orderBy, int n) =>
        IsSqlServer
            ? selectSql.Replace("{TOP}", $"TOP ({n})") + orderBy
            : selectSql.Replace("{TOP}", "") + orderBy + $" LIMIT {n}";

    /// <summary>
    /// 初始化：创建数据目录并建表（首次运行），随后执行种子数据。
    /// </summary>
    public static void Initialize()
    {
        if (!IsSqlServer)
            Directory.CreateDirectory(Path.GetDirectoryName(DbPath)!);

        using var conn = CreateConnection();
        ExecuteSchema(conn);
        UserRepository.SeedDefaultUser(conn);
        Log.Info($"数据库初始化完成：{(IsSqlServer ? "SQL Server 网络版" : DbPath)}");
    }

    /// <summary>执行建表脚本（各表已存在时自动跳过）。</summary>
    private static void ExecuteSchema(DbConnection conn)
    {
        string sql = IsSqlServer ? SqlServerSchema : SqliteSchema;
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
        EnsureExtColumn(conn);
        EnsureCompanyDeductColumns(conn);
        EnsurePrintCountColumn(conn);
        EnsureAmountColumn(conn);
    }

    /// <summary>
    /// 旧库升级：t_weigh_record 补 ext_json 列（自定义字段值，JSON 存储）。
    /// SQLite 的 ADD COLUMN 不支持 IF NOT EXISTS，重复执行报"列已存在"直接忽略。
    /// </summary>
    private static void EnsureExtColumn(DbConnection conn)
    {
        using var cmd = conn.CreateCommand();
        if (IsSqlServer)
        {
            cmd.CommandText = """
                IF NOT EXISTS (SELECT 1 FROM sys.columns
                    WHERE object_id = OBJECT_ID(N't_weigh_record') AND name = 'ext_json')
                ALTER TABLE t_weigh_record ADD ext_json NVARCHAR(500);
                """;
            cmd.ExecuteNonQuery();
        }
        else
        {
            cmd.CommandText = "ALTER TABLE t_weigh_record ADD COLUMN ext_json TEXT;";
            try { cmd.ExecuteNonQuery(); }
            catch { /* 列已存在 */ }
        }
    }

    /// <summary>
    /// 旧库升级：t_weigh_record 补 sender_company / receiver_company / deduct_kg 列
    /// （原"单位"拆分为发货/收货单位，新增扣重；SQLite ADD COLUMN 不支持 IF NOT EXISTS，
    /// 重复执行报"列已存在"直接忽略）。旧 company 列数据迁移到 receiver_company。
    /// </summary>
    private static void EnsureCompanyDeductColumns(DbConnection conn)
    {
        void Exec(string sql)
        {
            using var c = conn.CreateCommand();
            c.CommandText = sql;
            try { c.ExecuteNonQuery(); }
            catch when (!IsSqlServer) { /* SQLite：列已存在 */ }
        }

        if (IsSqlServer)
        {
            Exec("""
                IF NOT EXISTS (SELECT 1 FROM sys.columns
                    WHERE object_id = OBJECT_ID(N't_weigh_record') AND name = 'sender_company')
                ALTER TABLE t_weigh_record ADD sender_company NVARCHAR(100);
                """);
            Exec("""
                IF NOT EXISTS (SELECT 1 FROM sys.columns
                    WHERE object_id = OBJECT_ID(N't_weigh_record') AND name = 'receiver_company')
                ALTER TABLE t_weigh_record ADD receiver_company NVARCHAR(100);
                """);
            Exec("""
                IF NOT EXISTS (SELECT 1 FROM sys.columns
                    WHERE object_id = OBJECT_ID(N't_weigh_record') AND name = 'deduct_kg')
                ALTER TABLE t_weigh_record ADD deduct_kg FLOAT;
                """);
        }
        else
        {
            Exec("ALTER TABLE t_weigh_record ADD COLUMN sender_company TEXT;");
            Exec("ALTER TABLE t_weigh_record ADD COLUMN receiver_company TEXT;");
            Exec("ALTER TABLE t_weigh_record ADD COLUMN deduct_kg REAL;");
        }

        // 旧"单位"数据归入收货单位（仅补空值，幂等）
        Exec("""
            UPDATE t_weigh_record SET receiver_company = company
            WHERE receiver_company IS NULL AND company IS NOT NULL;
            """);
    }

    /// <summary>
    /// 旧库升级：t_weigh_record 补 print_count 列（磅单打印次数累计，综合设置-基本设置）。
    /// 重复执行报"列已存在"直接忽略（SQLite 无 IF NOT EXISTS）。
    /// </summary>
    private static void EnsurePrintCountColumn(DbConnection conn)
    {
        using var cmd = conn.CreateCommand();
        if (IsSqlServer)
        {
            cmd.CommandText = """
                IF NOT EXISTS (SELECT 1 FROM sys.columns
                    WHERE object_id = OBJECT_ID(N't_weigh_record') AND name = 'print_count')
                ALTER TABLE t_weigh_record ADD print_count INT NOT NULL DEFAULT 0;
                """;
            cmd.ExecuteNonQuery();
        }
        else
        {
            cmd.CommandText = "ALTER TABLE t_weigh_record ADD COLUMN print_count INTEGER NOT NULL DEFAULT 0;";
            try { cmd.ExecuteNonQuery(); }
            catch { /* 列已存在 */ }
        }
    }

    /// <summary>
    /// 旧库升级：t_weigh_record 补 amount_yuan 列（二次磅结算应收金额，元；
    /// 按重量分段固定金额收费）。重复执行报"列已存在"直接忽略（SQLite 无 IF NOT EXISTS）。
    /// </summary>
    private static void EnsureAmountColumn(DbConnection conn)
    {
        using var cmd = conn.CreateCommand();
        if (IsSqlServer)
        {
            cmd.CommandText = """
                IF NOT EXISTS (SELECT 1 FROM sys.columns
                    WHERE object_id = OBJECT_ID(N't_weigh_record') AND name = 'amount_yuan')
                ALTER TABLE t_weigh_record ADD amount_yuan FLOAT;
                """;
            cmd.ExecuteNonQuery();
        }
        else
        {
            cmd.CommandText = "ALTER TABLE t_weigh_record ADD COLUMN amount_yuan REAL;";
            try { cmd.ExecuteNonQuery(); }
            catch { /* 列已存在 */ }
        }
    }

    // ==================== 建表脚本（双方言） ====================

    private const string SqliteSchema = """
        CREATE TABLE IF NOT EXISTS t_weigh_record (
            id           INTEGER PRIMARY KEY AUTOINCREMENT,
            serial_no    TEXT NOT NULL UNIQUE,
            scale_no     TEXT NOT NULL DEFAULT '01',
            vehicle_no   TEXT NOT NULL,
            goods        TEXT,
            company      TEXT,
            spec         TEXT,
            batch_no     TEXT,
            remark       TEXT,
            gross_kg     REAL,
            tare_kg      REAL,
            net_kg       REAL,
            first_time   TEXT,
            second_time  TEXT,
            operator     TEXT,
            status       TEXT NOT NULL DEFAULT '未完成',
            is_manual    INTEGER NOT NULL DEFAULT 0,
            print_count  INTEGER NOT NULL DEFAULT 0,
            created_at   TEXT
        );
        CREATE INDEX IF NOT EXISTS idx_record_vehicle ON t_weigh_record(vehicle_no, status);
        CREATE INDEX IF NOT EXISTS idx_record_first_time ON t_weigh_record(first_time);

        CREATE TABLE IF NOT EXISTS t_user (
            id            INTEGER PRIMARY KEY AUTOINCREMENT,
            username      TEXT NOT NULL UNIQUE,
            display_name  TEXT NOT NULL,
            password_hash TEXT NOT NULL,
            salt          TEXT NOT NULL,
            role          TEXT NOT NULL DEFAULT '操作员',
            enabled       INTEGER NOT NULL DEFAULT 1,
            is_default_pwd INTEGER NOT NULL DEFAULT 0,
            fail_count    INTEGER NOT NULL DEFAULT 0,
            lock_until    TEXT,
            last_login    TEXT,
            created_at    TEXT
        );

        CREATE TABLE IF NOT EXISTS t_record_audit (
            id         INTEGER PRIMARY KEY AUTOINCREMENT,
            record_id  INTEGER NOT NULL,
            action     TEXT NOT NULL,
            field      TEXT,
            old_value  TEXT,
            new_value  TEXT,
            operator   TEXT,
            time       TEXT
        );
        CREATE INDEX IF NOT EXISTS idx_audit_record ON t_record_audit(record_id);

        CREATE TABLE IF NOT EXISTS t_custom_field (
            id          INTEGER PRIMARY KEY AUTOINCREMENT,
            name        TEXT NOT NULL UNIQUE,
            field_type  TEXT NOT NULL DEFAULT '文本',
            options     TEXT,
            required    INTEGER NOT NULL DEFAULT 0,
            sort_order  INTEGER NOT NULL DEFAULT 0,
            enabled     INTEGER NOT NULL DEFAULT 1
        );

        CREATE TABLE IF NOT EXISTS t_black_box (
            id        INTEGER PRIMARY KEY AUTOINCREMENT,
            time      TEXT NOT NULL,
            weight_kg REAL NOT NULL,
            images    TEXT
        );
        CREATE INDEX IF NOT EXISTS idx_blackbox_time ON t_black_box(time);
        """;

    private const string SqlServerSchema = """
        IF OBJECT_ID(N't_weigh_record', N'U') IS NULL
        CREATE TABLE t_weigh_record (
            id           BIGINT IDENTITY(1,1) PRIMARY KEY,
            serial_no    NVARCHAR(50) NOT NULL UNIQUE,
            scale_no     NVARCHAR(10) NOT NULL DEFAULT '01',
            vehicle_no   NVARCHAR(20) NOT NULL,
            goods        NVARCHAR(50),
            company      NVARCHAR(100),
            sender_company    NVARCHAR(100),
            receiver_company  NVARCHAR(100),
            deduct_kg    FLOAT,
            spec         NVARCHAR(50),
            batch_no     NVARCHAR(50),
            remark       NVARCHAR(200),
            gross_kg     FLOAT,
            tare_kg      FLOAT,
            net_kg       FLOAT,
            first_time   NVARCHAR(20),
            second_time  NVARCHAR(20),
            operator     NVARCHAR(20),
            status       NVARCHAR(10) NOT NULL DEFAULT N'未完成',
            is_manual    BIT NOT NULL DEFAULT 0,
            print_count  INT NOT NULL DEFAULT 0,
            created_at   NVARCHAR(20)
        );
        IF OBJECT_ID(N't_user', N'U') IS NULL
        CREATE TABLE t_user (
            id            BIGINT IDENTITY(1,1) PRIMARY KEY,
            username      NVARCHAR(30) NOT NULL UNIQUE,
            display_name  NVARCHAR(30) NOT NULL,
            password_hash NVARCHAR(100) NOT NULL,
            salt          NVARCHAR(50) NOT NULL,
            role          NVARCHAR(20) NOT NULL DEFAULT N'操作员',
            enabled       BIT NOT NULL DEFAULT 1,
            is_default_pwd BIT NOT NULL DEFAULT 0,
            fail_count    INT NOT NULL DEFAULT 0,
            lock_until    NVARCHAR(20),
            last_login    NVARCHAR(20),
            created_at    NVARCHAR(20)
        );
        IF OBJECT_ID(N't_record_audit', N'U') IS NULL
        CREATE TABLE t_record_audit (
            id         BIGINT IDENTITY(1,1) PRIMARY KEY,
            record_id  BIGINT NOT NULL,
            action     NVARCHAR(10) NOT NULL,
            field      NVARCHAR(20),
            old_value  NVARCHAR(100),
            new_value  NVARCHAR(200),
            operator   NVARCHAR(20),
            time       NVARCHAR(20)
        );
        IF OBJECT_ID(N't_custom_field', N'U') IS NULL
        CREATE TABLE t_custom_field (
            id          BIGINT IDENTITY(1,1) PRIMARY KEY,
            name        NVARCHAR(50) NOT NULL UNIQUE,
            field_type  NVARCHAR(10) NOT NULL DEFAULT N'文本',
            options     NVARCHAR(200),
            required    BIT NOT NULL DEFAULT 0,
            sort_order  INT NOT NULL DEFAULT 0,
            enabled     BIT NOT NULL DEFAULT 1
        );
        """;
}
