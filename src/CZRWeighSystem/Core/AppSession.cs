namespace CZRWeighSystem.Core;

/// <summary>
/// 全局会话：保存当前登录用户，供主界面与业务层使用。
/// </summary>
public static class AppSession
{
    /// <summary>当前登录用户（登录成功后赋值）</summary>
    public static User? CurrentUser { get; set; }

    /// <summary>是否请求注销并重新登录（主界面"注销"菜单置位）</summary>
    public static bool ReLoginRequested { get; set; }

    /// <summary>当前用户显示名（未登录时返回空串）</summary>
    public static string DisplayName => CurrentUser?.DisplayName ?? "";
}
