namespace CZRWeighSystem.Core;

/// <summary>
/// 用户模型（对应 PRD 第 8 章 t_user 表、5.1 登录与用户管理）。
/// </summary>
public class User
{
    /// <summary>角色：管理员（可进用户管理等管理功能）</summary>
    public const string RoleAdmin = "管理员";
    /// <summary>角色：称重员（仅称重业务操作）</summary>
    public const string RoleWeigher = "称重员";

    public long Id { get; set; }
    /// <summary>登录账号（唯一）</summary>
    public string Username { get; set; } = "";
    /// <summary>显示名（司磅员姓名）</summary>
    public string DisplayName { get; set; } = "";
    /// <summary>角色：管理员/称重员（历史库可能存在"超级管理员"，按管理员对待）</summary>
    public string Role { get; set; } = RoleWeigher;

    /// <summary>是否管理员（含历史库遗留的"超级管理员"）。</summary>
    public bool IsAdmin => Role.Contains(RoleAdmin);
    /// <summary>是否启用</summary>
    public bool Enabled { get; set; } = true;
    /// <summary>是否仍为初始密码（首次登录强制修改，对应 PRD 3 章说明）</summary>
    public bool IsDefaultPwd { get; set; }
    /// <summary>连续登录失败次数</summary>
    public int FailCount { get; set; }
    /// <summary>锁定截止时间（null 表示未锁定）</summary>
    public DateTime? LockUntil { get; set; }
    /// <summary>最后登录时间</summary>
    public DateTime? LastLogin { get; set; }
}
