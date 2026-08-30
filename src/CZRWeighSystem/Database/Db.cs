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
            created_at   TEXT
        );
        CREATE INDEX IF NOT EXISTS idx_record_vehicle ON t_weigh_record(vehicle_no, status);
        CREATE INDEX IF NOT EXISTS idx_record_first_time ON t_weigh_record(first_time);

        CREATE TABLE IF NOT EXISTS t_vehicle (
            id          INTEGER PRIMARY KEY AUTOINCREMENT,
            vehicle_no  TEXT NOT NULL UNIQUE,
            default_tare_kg REAL,
            owner       TEXT,
            phone       TEXT,
            enabled     INTEGER NOT NULL DEFAULT 1
        );

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

        CREATE TABLE IF NOT EXISTS t_goods (
            id          INTEGER PRIMARY KEY AUTOINCREMENT,
            name        TEXT NOT NULL UNIQUE,
            spec        TEXT,
            unit        TEXT,
            deduct_rate REAL,
            enabled     INTEGER NOT NULL DEFAULT 1
        );

        CREATE TABLE IF NOT EXISTS t_company (
            id          INTEGER PRIMARY KEY AUTOINCREMENT,
            name        TEXT NOT NULL,
            type        TEXT NOT NULL DEFAULT '收货',
            contact     TEXT,
            phone       TEXT,
            enabled     INTEGER NOT NULL DEFAULT 1
        );
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
            created_at   NVARCHAR(20)
        );
        IF OBJECT_ID(N't_vehicle', N'U') IS NULL
        CREATE TABLE t_vehicle (
            id          BIGINT IDENTITY(1,1) PRIMARY KEY,
            vehicle_no  NVARCHAR(20) NOT NULL UNIQUE,
            default_tare_kg FLOAT,
            owner       NVARCHAR(50),
            phone       NVARCHAR(20),
            enabled     BIT NOT NULL DEFAULT 1
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
        IF OBJECT_ID(N't_goods', N'U') IS NULL
        CREATE TABLE t_goods (
            id          BIGINT IDENTITY(1,1) PRIMARY KEY,
            name        NVARCHAR(50) NOT NULL UNIQUE,
            spec        NVARCHAR(50),
            unit        NVARCHAR(20),
            deduct_rate FLOAT,
            enabled     BIT NOT NULL DEFAULT 1
        );
        IF OBJECT_ID(N't_company', N'U') IS NULL
        CREATE TABLE t_company (
            id          BIGINT IDENTITY(1,1) PRIMARY KEY,
            name        NVARCHAR(100) NOT NULL,
            type        NVARCHAR(10) NOT NULL DEFAULT N'收货',
            contact     NVARCHAR(30),
            phone       NVARCHAR(20),
            enabled     BIT NOT NULL DEFAULT 1
        );
        """;
}
