using System.Data.Common;
using System.Security.Cryptography;
using CZRWeighSystem.Core;
using CZRWeighSystem.Utils;

namespace CZRWeighSystem.Database;

/// <summary>
/// 用户仓储（对应 PRD 5.1 登录与用户管理、6.4 安全性）。
/// 密码采用 PBKDF2 + 随机盐哈希存储，禁止明文（PRD 6.4-1）。
/// 登录失败连续 5 次锁定账号 10 分钟（PRD F1-01）。
/// 双数据库兼容（SQLite / SQL Server）。
/// </summary>
public class UserRepository
{
    /// <summary>允许的最大连续失败次数，达到后锁定</summary>
    private const int MaxFailCount = 5;
    /// <summary>锁定时长（分钟）</summary>
    private const int LockMinutes = 10;

    // ================ 密码哈希 ================

    /// <summary>
    /// 计算密码哈希（PBKDF2-SHA256，10 万次迭代）。
    /// </summary>
    public static (string Hash, string Salt) HashPassword(string password)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(16);
        byte[] hash = Rfc2898DeriveBytes.Pbkdf2(
            password, salt, 100_000, HashAlgorithmName.SHA256, 32);
        return (Convert.ToBase64String(hash), Convert.ToBase64String(salt));
    }

    /// <summary>校验密码是否与存储哈希一致（固定时间比较防时序攻击）。</summary>
    private static bool VerifyPassword(string password, string hash, string salt)
    {
        byte[] saltBytes = Convert.FromBase64String(salt);
        byte[] expect = Convert.FromBase64String(hash);
        byte[] actual = Rfc2898DeriveBytes.Pbkdf2(
            password, saltBytes, 100_000, HashAlgorithmName.SHA256, 32);
        return CryptographicOperations.FixedTimeEquals(actual, expect);
    }

    // ================ 种子数据 ================

    /// <summary>
    /// 无任何用户时插入默认超级管理员：admin / 123456（首次登录强制改密）。
    /// </summary>
    public static void SeedDefaultUser(DbConnection conn)
    {
        using (var count = conn.CreateCommand())
        {
            count.CommandText = "SELECT COUNT(*) FROM t_user";
            if (Convert.ToInt64(count.ExecuteScalar()) > 0) return;
        }

        var (hash, salt) = HashPassword("123456");
        // 中文常量全部参数化（N'..' 为 SQL Server 方言，SQLite 不支持）
        const string sql = """
            INSERT INTO t_user
                (username, display_name, password_hash, salt, role,
                 enabled, is_default_pwd, created_at)
            VALUES
                (@username, @display, @hash, @salt, @role,
                 1, 1, @now);
            """;
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Add("@username", "admin");
        cmd.Add("@display", "管理员");
        cmd.Add("@hash", hash);
        cmd.Add("@salt", salt);
        cmd.Add("@role", User.RoleAdmin);
        cmd.Add("@now", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        cmd.ExecuteNonQuery();
        Log.Info("已创建默认账号 admin（初始密码 123456，首次登录强制修改）");
    }

    // ================ 登录 ================

    /// <summary>登录结果</summary>
    public record LoginResult(bool Ok, string Message, User? User);

    /// <summary>
    /// 登录验证：校验账号密码，并处理失败计数与锁定（PRD F1-01）。
    /// </summary>
    public LoginResult Login(string username, string password)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            return new LoginResult(false, "请输入账号和密码", null);

        using var conn = Db.CreateConnection();
        var user = GetByUsername(conn, username);

        // 统一提示，避免暴露"账号是否存在"（安全性）
        if (user == null)
            return new LoginResult(false, "账号或密码错误", null);
        if (!user.Enabled)
            return new LoginResult(false, "该账号已停用，请联系管理员", null);

        // 锁定检查
        if (user.LockUntil.HasValue && user.LockUntil.Value > DateTime.Now)
        {
            int left = (int)Math.Ceiling((user.LockUntil.Value - DateTime.Now).TotalMinutes);
            return new LoginResult(false, $"密码错误次数过多，账号已锁定，请约 {left} 分钟后重试", null);
        }

        if (VerifyPassword(password, GetHash(conn, user.Id), GetSalt(conn, user.Id)))
        {
            // 登录成功：清零失败计数、更新最后登录时间
            const string okSql = """
                UPDATE t_user SET fail_count=0, lock_until=NULL,
                       last_login=@now WHERE id=@id;
                """;
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = okSql;
                cmd.Add("@now", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                cmd.Add("@id", user.Id);
                cmd.ExecuteNonQuery();
            }
            user.FailCount = 0;
            user.LockUntil = null;
            user.LastLogin = DateTime.Now;
            Log.Info($"用户登录成功：{user.Username}");
            return new LoginResult(true, "登录成功", user);
        }

        // 登录失败：累加计数，达到阈值则锁定
        int fail = user.FailCount + 1;
        bool locked = fail >= MaxFailCount;
        string failSql = locked
            ? "UPDATE t_user SET fail_count=@f, lock_until=@l WHERE id=@id"
            : "UPDATE t_user SET fail_count=@f WHERE id=@id";
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = failSql;
            cmd.Add("@f", fail);
            if (locked)
                cmd.Add("@l", DateTime.Now.AddMinutes(LockMinutes).ToString("yyyy-MM-dd HH:mm:ss"));
            cmd.Add("@id", user.Id);
            cmd.ExecuteNonQuery();
        }

        Log.Info($"用户登录失败：{user.Username}（第 {fail} 次）");
        string msg = locked
            ? $"密码连续错误 {MaxFailCount} 次，账号锁定 {LockMinutes} 分钟"
            : $"账号或密码错误（剩余尝试 {MaxFailCount - fail} 次）";
        return new LoginResult(false, msg, null);
    }

    // ================ 修改密码 ================

    /// <summary>
    /// 修改密码（校验旧密码；对应 PRD F1-04）。
    /// </summary>
    public (bool Ok, string Message) ChangePassword(long userId, string oldPwd, string newPwd, string confirmPwd)
    {
        if (string.IsNullOrWhiteSpace(newPwd) || newPwd.Length < 6)
            return (false, "新密码长度至少 6 位");
        if (newPwd != confirmPwd)
            return (false, "两次输入的新密码不一致");

        using var conn = Db.CreateConnection();
        if (!VerifyPassword(oldPwd, GetHash(conn, userId), GetSalt(conn, userId)))
            return (false, "原密码不正确");

        var (hash, salt) = HashPassword(newPwd);
        const string sql = """
            UPDATE t_user SET password_hash=@h, salt=@s, is_default_pwd=0
            WHERE id=@id;
            """;
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Add("@h", hash);
        cmd.Add("@s", salt);
        cmd.Add("@id", userId);
        cmd.ExecuteNonQuery();

        Log.Info($"用户修改密码成功：userId={userId}");
        return (true, "密码修改成功");
    }

    // ================ 用户管理（仅管理员，对应 PRD F1-02） ================

    /// <summary>用户列表（按创建顺序，供用户管理界面展示）。</summary>
    public List<User> GetAll()
    {
        var list = new List<User>();
        using var conn = Db.CreateConnection();
        const string sql = """
            SELECT id, username, display_name, role, enabled, is_default_pwd,
                   fail_count, lock_until, last_login
            FROM t_user ORDER BY id;
            """;
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new User
            {
                Id = reader.GetInt64(0),
                Username = reader.GetString(1),
                DisplayName = reader.GetString(2),
                Role = reader.GetString(3),
                Enabled = Convert.ToInt64(reader.GetValue(4)) == 1,
                IsDefaultPwd = Convert.ToInt64(reader.GetValue(5)) == 1,
                FailCount = (int)Convert.ToInt64(reader.GetValue(6)),
                LockUntil = ParseTime(reader, 7),
                LastLogin = ParseTime(reader, 8),
            });
        }
        return list;
    }

    /// <summary>新增用户结果。</summary>
    public record AddUserResult(bool Ok, string Message);

    /// <summary>
    /// 新增用户（管理员操作）：账号唯一、初始密码至少 6 位；
    /// 创建后 is_default_pwd=1，首次登录强制修改密码。
    /// </summary>
    public AddUserResult AddUser(string username, string displayName, string role, string password)
    {
        username = username.Trim();
        displayName = displayName.Trim();

        if (username.Length == 0) return new AddUserResult(false, "请输入登录账号");
        if (displayName.Length == 0) return new AddUserResult(false, "请输入姓名");
        if (role != User.RoleAdmin && role != User.RoleWeigher)
            return new AddUserResult(false, "角色无效");
        if (string.IsNullOrWhiteSpace(password) || password.Length < 6)
            return new AddUserResult(false, "初始密码长度至少 6 位");

        using var conn = Db.CreateConnection();
        if (GetByUsername(conn, username) != null)
            return new AddUserResult(false, $"账号 {username} 已存在");

        var (hash, salt) = HashPassword(password);
        const string sql = """
            INSERT INTO t_user
                (username, display_name, password_hash, salt, role,
                 enabled, is_default_pwd, created_at)
            VALUES
                (@username, @display, @hash, @salt, @role,
                 1, 1, @now);
            """;
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Add("@username", username);
        cmd.Add("@display", displayName);
        cmd.Add("@hash", hash);
        cmd.Add("@salt", salt);
        cmd.Add("@role", role);
        cmd.Add("@now", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        cmd.ExecuteNonQuery();

        Log.Info($"管理员新增用户：{username}（{role}）");
        return new AddUserResult(true, $"用户 {username} 创建成功");
    }

    /// <summary>删除用户结果。</summary>
    public record DeleteUserResult(bool Ok, string Message);

    /// <summary>
    /// 删除用户（管理员操作）：不允许删除当前登录账号自己；
    /// 删除后系统必须仍保留至少一个启用的管理员账号。
    /// </summary>
    public DeleteUserResult DeleteUser(long userId, long currentUserId)
    {
        if (userId == currentUserId)
            return new DeleteUserResult(false, "不能删除当前登录的账号");

        using var conn = Db.CreateConnection();
        var target = GetById(conn, userId);
        if (target == null)
            return new DeleteUserResult(false, "该用户不存在");
        if (target.IsAdmin && CountEnabledAdmins(conn) <= 1)
            return new DeleteUserResult(false, "系统必须保留至少一个管理员账号，请先创建其他管理员");

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM t_user WHERE id=@id";
        cmd.Add("@id", userId);
        cmd.ExecuteNonQuery();

        Log.Info($"管理员删除用户：{target.Username}");
        return new DeleteUserResult(true, $"用户 {target.Username} 已删除");
    }

    // ================ 内部查询 ================

    private static User? GetByUsername(DbConnection conn, string username)
    {
        const string sql = """
            SELECT id, username, display_name, role, enabled, is_default_pwd,
                   fail_count, lock_until, last_login
            FROM t_user WHERE username=@u;
            """;
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Add("@u", username.Trim());
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;

        return new User
        {
            Id = reader.GetInt64(0),
            Username = reader.GetString(1),
            DisplayName = reader.GetString(2),
            Role = reader.GetString(3),
            Enabled = Convert.ToInt64(reader.GetValue(4)) == 1,
            IsDefaultPwd = Convert.ToInt64(reader.GetValue(5)) == 1,
            FailCount = (int)Convert.ToInt64(reader.GetValue(6)),
            LockUntil = ParseTime(reader, 7),
            LastLogin = ParseTime(reader, 8),
        };
    }

    /// <summary>按主键读取用户（不存在返回 null）。</summary>
    private static User? GetById(DbConnection conn, long id)
    {
        const string sql = """
            SELECT id, username, display_name, role, enabled, is_default_pwd,
                   fail_count, lock_until, last_login
            FROM t_user WHERE id=@id;
            """;
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Add("@id", id);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;

        return new User
        {
            Id = reader.GetInt64(0),
            Username = reader.GetString(1),
            DisplayName = reader.GetString(2),
            Role = reader.GetString(3),
            Enabled = Convert.ToInt64(reader.GetValue(4)) == 1,
            IsDefaultPwd = Convert.ToInt64(reader.GetValue(5)) == 1,
            FailCount = (int)Convert.ToInt64(reader.GetValue(6)),
            LockUntil = ParseTime(reader, 7),
            LastLogin = ParseTime(reader, 8),
        };
    }

    /// <summary>
    /// 统计启用的管理员数量（role 含"管理员"，兼容历史"超级管理员"）。
    /// </summary>
    private static int CountEnabledAdmins(DbConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM t_user WHERE enabled=1 AND role LIKE @r";
        cmd.Add("@r", "%管理员%");
        return (int)Convert.ToInt64(cmd.ExecuteScalar());
    }

    private static string GetHash(DbConnection conn, long userId) =>
        QueryField(conn, userId, "password_hash");

    private static string GetSalt(DbConnection conn, long userId) =>
        QueryField(conn, userId, "salt");

    private static string QueryField(DbConnection conn, long userId, string field)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT {field} FROM t_user WHERE id=@id";
        cmd.Add("@id", userId);
        return cmd.ExecuteScalar()?.ToString() ?? "";
    }

    private static DateTime? ParseTime(DbDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal)) return null;
        return DateTime.TryParse(reader.GetString(ordinal), out var t) ? t : null;
    }
}
