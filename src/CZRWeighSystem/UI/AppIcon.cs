namespace CZRWeighSystem.UI;

/// <summary>
/// 程序图标（用户提供的「精锐计量」徽标）：
/// exe 文件图标由 csproj 的 ApplicationIcon 指定，窗体图标从同一份内嵌 app.ico 读取，
/// 保证标题栏、任务栏与桌面快捷方式显示一致，且无外部文件依赖。
/// </summary>
internal static class AppIcon
{
    /// <summary>加载内嵌程序图标（csproj 中 UI\Resources\app.ico 以 EmbeddedResource 打包，含 16~256 多尺寸帧）。</summary>
    public static Icon Load()
    {
        using var stream = typeof(AppIcon).Assembly
            .GetManifestResourceStream($"{typeof(AppIcon).Namespace}.Resources.app.ico")
            ?? throw new InvalidOperationException("缺少内嵌图标资源：app.ico");
        return new Icon(stream);
    }
}