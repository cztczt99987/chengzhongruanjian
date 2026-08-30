namespace CZRWeighSystem.Core;

/// <summary>
/// 用户模型（对应 PRD 第 8 章 t_user 表、5.1 登录与用户管理）。
/// </summary>
public class User
{
    public long Id { get; set; }
    /// <summary>登录账号（唯一）</summary>
    public string Username { get; set; } = "";
    /// <summary>显示名（司磅员姓名）</summary>
    public string DisplayName { get; set; } = "";
    /// <summary>角色：操作员/统计员/管理员/超级管理员（对应第 3 章权限矩阵）</summary>
    public string Role { get; set; } = "操作员";
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
