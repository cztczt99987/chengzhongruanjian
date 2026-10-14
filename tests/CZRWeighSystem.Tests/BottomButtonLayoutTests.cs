using CZRWeighSystem.Database;
using CZRWeighSystem.UI;
using System.Reflection;

namespace CZRWeighSystem.Tests;

/// <summary>
/// 底部按钮布局回归测试：收费功能设置与登录窗体的底部按钮曾被上方
/// AutoSize 说明/提示文字压住（高 DPI 下文字更高，遮挡更明显）。
/// 锁定规则：上方文字底边不得低于按钮顶边，按钮须完整落在窗体客户区内。
/// 与窗体测试同集合串行执行：登录窗体构造时会读取用户表填充账号下拉框。
/// </summary>
[Collection("数据库顺序")]
public class BottomButtonLayoutTests
{
    /// <summary>在 STA 线程上执行测试体（WinForms 控件只能在 STA 线程访问）。</summary>
    private static void RunOnSta(Action act)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { act(); }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error != null) throw error;
    }

    [Fact]
    public void 收费功能设置_说明文字不得压住保存与退出按钮()
    {
        RunOnSta(() =>
        {
            using var f = new FeeSettingsDialog();
            var tip = f.Controls.OfType<Label>().First(l => l.Text.StartsWith("净重匹配分段"));
            var save = f.Controls.OfType<Button>().First(b => b.Text == "保 存");
            var exit = f.Controls.OfType<Button>().First(b => b.Text == "退 出");

            Assert.True(tip.Bottom <= save.Top,
                $"说明文字底边({tip.Bottom})压住保存按钮顶边({save.Top})");
            Assert.True(save.Bottom <= f.ClientSize.Height,
                $"保存按钮底边({save.Bottom})超出窗体客户区({f.ClientSize.Height})");
            Assert.True(exit.Bottom <= f.ClientSize.Height,
                $"退出按钮底边({exit.Bottom})超出窗体客户区({f.ClientSize.Height})");
        });
    }

    [Fact]
    public void 登录窗体_错误提示换行后不得压住登录按钮()
    {
        RunOnSta(() =>
        {
            Db.Initialize();   // 与 Program.Main 启动顺序一致：先建表再创建登录框（幂等）
            using var f = new LoginForm();
            var lbl = (Label)typeof(LoginForm).GetField("_lblError",
                BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(f)!;
            var btn = (Button)typeof(LoginForm).GetField("_btnLogin",
                BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(f)!;

            // 模拟账号锁定的长提示（MaximumSize 265 下会换行至两三行）
            lbl.Text = "密码连续错误 5 次，账号已锁定至 10:30:00（剩余 9 分 59 秒），请稍后再试";
            Application.DoEvents();   // AutoSize 重算高度

            Assert.True(lbl.Bottom <= btn.Top,
                $"错误提示底边({lbl.Bottom})压住登录按钮顶边({btn.Top})");
            Assert.True(btn.Bottom <= f.ClientSize.Height,
                $"登录按钮底边({btn.Bottom})超出窗体客户区({f.ClientSize.Height})");
        });
    }

    [Fact]
    public void 登录窗体_账号改为下拉框列出全部用户_仅需填密码()
    {
        RunOnSta(() =>
        {
            Db.Initialize();   // 与 Program.Main 启动顺序一致：先建表再创建登录框（幂等）
            using var f = new LoginForm();
            object Fld(string name) => typeof(LoginForm)
                .GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(f)!;
            var combo = (ComboBox)Fld("_cboUser");
            var pwd = (TextBox)Fld("_txtPwd");

            // 用户要求：账号改为下拉框形式，下拉里是全部用户，用户只需填密码
            Assert.Equal(ComboBoxStyle.DropDownList, combo.DropDownStyle);   // 只能选择，不能手输
            Assert.Equal(new UserRepository().GetAll().Count, combo.Items.Count);
            Assert.True(combo.SelectedIndex >= 0, "下拉框应默认选中一个账号");
            Assert.Equal("", pwd.Text);
            Assert.Same(pwd, f.ActiveControl);   // 账号已选好，焦点直接落在密码框
        });
    }

    [Fact]
    public void 模板管理_最窄宽度下底部按钮栏九键完整可见()
    {
        RunOnSta(() =>
        {
            using var f = new TemplateLibraryForm();
            f.Size = f.MinimumSize;    // 最窄场景：窗口缩到允许的最小宽度
            f.Show();
            Application.DoEvents();    // 触发布局

            static IEnumerable<Control> Descendants(Control c)
            {
                foreach (Control child in c.Controls)
                {
                    yield return child;
                    foreach (var d in Descendants(child)) yield return d;
                }
            }

            var flow = Descendants(f).OfType<FlowLayoutPanel>().Single();
            var buttons = Descendants(flow).OfType<Button>().OrderBy(b => b.Left).ToList();

            Assert.Equal(9, buttons.Count);
            Assert.Equal("关闭", buttons[^1].Text);   // 最后一个按钮
            foreach (var b in buttons)
                Assert.True(b.Right <= flow.ClientSize.Width,
                    $"底部按钮“{b.Text}”右侧({b.Right})超出按钮栏宽度({flow.ClientSize.Width})，会被窗口裁切");
        });
    }
}