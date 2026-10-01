using CZRWeighSystem.Database;
using CZRWeighSystem.UI;
using System.Reflection;

namespace CZRWeighSystem.Tests;

/// <summary>
/// 程序显示名称回归测试（2026-09-17 用户要求改为「精锐计量称重软件」）：
/// 登录窗口、主窗口标题、exe 文件属性（程序集 Product）必须一致，
/// 避免改名时漏改某一处导致界面上残留旧名称。
/// </summary>
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
}