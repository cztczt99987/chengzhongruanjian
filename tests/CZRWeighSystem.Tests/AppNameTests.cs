using CZRWeighSystem.Database;
using CZRWeighSystem.UI;
using System.Reflection;

namespace CZRWeighSystem.Tests;

/// <summary>
/// 程序显示名称与图标回归测试（2026-09-17 用户要求改名「精锐计量称重软件」；
/// 2026-10-05 用户提供徽标图片作为软件图标，jpg 转换出内嵌 app.ico）：
/// 登录窗口、主窗口标题、exe 文件属性（程序集 Product）必须一致，
/// 内嵌 app.ico 必须存在且含 16~256 多尺寸帧，主窗口与登录窗口均须带上该图标，
/// 避免改名/换图标时漏改某一处导致界面残留旧样式。
/// 与其他窗体测试同集合串行执行：构造主界面会访问 SQLite 库。
/// </summary>
[Collection("数据库顺序")]
public class AppNameTests
{
    private const string AppName = "精锐计量称重软件";

    [Fact]
    public void 主窗口标题_使用新程序名称()
    {
        string title = "";
        var thread = new Thread(() =>
        {
            Db.Initialize();   // 与 Program.Main 启动顺序一致：先建表/迁移再创建主界面（幂等）
            using var form = new MainForm();
            title = form.Text;
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.StartsWith(AppName, title);
    }

    [Fact]
    public void 登录窗口_标题与界面标题_使用新程序名称()
    {
        string caption = "";
        bool hasTitleLabel = false;
        var thread = new Thread(() =>
        {
            using var form = new LoginForm();
            caption = form.Text;
            hasTitleLabel = form.Controls.Cast<Control>()
                .Any(c => c.Text == AppName);
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.Contains(AppName, caption);
        Assert.True(hasTitleLabel, "登录窗口界面内未找到新程序名称标题");
    }

    [Fact]
    public void 程序集产品名称_使用新程序名称_且版本号非空()
    {
        var asm = typeof(MainForm).Assembly;
        Assert.Equal(AppName, asm.GetCustomAttribute<AssemblyProductAttribute>()?.Product);
        Assert.Equal(AppName, asm.GetCustomAttribute<AssemblyTitleAttribute>()?.Title);
        Assert.False(string.IsNullOrWhiteSpace(asm.GetName().Version?.ToString()));
    }

    [Fact]
    public void 程序图标_内嵌资源存在_含16至256尺寸帧且可解析()
    {
        using var stream = typeof(MainForm).Assembly
            .GetManifestResourceStream("CZRWeighSystem.UI.Resources.app.ico");
        Assert.NotNull(stream);

        var bytes = new byte[stream!.Length];
        stream.ReadExactly(bytes);
        Assert.Equal(0, (int)BitConverter.ToUInt16(bytes, 0));   // 保留字段
        Assert.Equal(1, (int)BitConverter.ToUInt16(bytes, 2));   // 类型：图标
        int count = BitConverter.ToUInt16(bytes, 4);

        var sizes = new List<int>();
        for (int i = 0; i < count; i++)
        {
            int w = bytes[6 + 16 * i];
            sizes.Add(w == 0 ? 256 : w);   // 目录项宽 0 表示 256
        }
        foreach (int expected in new[] { 16, 24, 32, 48, 64, 128, 256 })
        {
            Assert.Contains(expected, sizes);
        }

        using var icon = AppIcon.Load();   // 程序实际使用的加载路径必须可解析
        Assert.True(icon.Width >= 16 && icon.Height >= 16);
    }

    [Fact]
    public void 程序图标_主窗口与登录窗口均已设置()
    {
        int mainIconWidth = 0;
        int loginIconWidth = 0;
        var thread = new Thread(() =>
        {
            Db.Initialize();   // 与 Program.Main 启动顺序一致：先建表/迁移再创建主界面（幂等）
            using (var main = new MainForm()) { mainIconWidth = main.Icon?.Width ?? 0; }
            using (var login = new LoginForm()) { loginIconWidth = login.Icon?.Width ?? 0; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.True(mainIconWidth >= 16, $"主窗口图标未正确加载（宽 {mainIconWidth}）");
        Assert.True(loginIconWidth >= 16, $"登录窗口图标未正确加载（宽 {loginIconWidth}）");
    }
}