using CZRWeighSystem.Core;
using CZRWeighSystem.Database;

namespace CZRWeighSystem.UI;

/// <summary>
/// 闲置锁屏解锁窗（综合设置-基本设置：电脑闲置 X 分钟屏幕锁定）。
/// 全屏无边框置顶，需输入当前登录账号的密码解锁；
/// 解锁走 UserRepository.Login（与登录一致的失败计数/账号锁定策略，防暴力尝试）。
/// </summary>
public class ScreenLockForm : Form
{
    private readonly TextBox _txtPwd = null!;

    public ScreenLockForm()
    {
        Text = "屏幕锁定";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        Bounds = Screen.PrimaryScreen!.Bounds;
        TopMost = true;
        ShowInTaskbar = false;
        BackColor = Color.FromArgb(28, 41, 61);
        Font = new Font("Microsoft YaHei UI", 10F);

        var lblTitle = new Label
        {
            Text = "屏幕已锁定",
            ForeColor = Color.White,
            Font = new Font("Microsoft YaHei UI", 26F, FontStyle.Bold),
            AutoSize = true,
        };
        var lblHint = new Label
        {
            Text = $"长时间无操作，屏幕已自动锁定。\r\n请输入账号 {AppSession.CurrentUser?.Username} 的密码解锁：",
            ForeColor = Color.Silver,
            AutoSize = true,
        };
        _txtPwd = new TextBox
        {
            UseSystemPasswordChar = true,
            Width = 260,
            Font = new Font("Microsoft YaHei UI", 12F),
        };
        var btnUnlock = new Button
        {
            Text = "解 锁",
            Size = new Size(110, 36),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(0, 122, 204),
            ForeColor = Color.White,
        };
        btnUnlock.Click += (_, _) => TryUnlock();

        Controls.AddRange([lblTitle, lblHint, _txtPwd, btnUnlock]);
        void LayoutControls()
        {
            int cx = ClientSize.Width / 2;
            lblTitle.Location = new Point(cx - lblTitle.PreferredWidth / 2, ClientSize.Height / 2 - 150);
            lblHint.Location = new Point(cx - lblHint.PreferredWidth / 2, ClientSize.Height / 2 - 60);
            _txtPwd.Location = new Point(cx - 190, ClientSize.Height / 2);
            btnUnlock.Location = new Point(_txtPwd.Right + 10, ClientSize.Height / 2 - 3);
        }
        Resize += (_, _) => LayoutControls();
        Shown += (_, _) => LayoutControls();

        AcceptButton = btnUnlock;   // 回车直接解锁
    }

    /// <summary>校验密码：与登录验证一致（失败计数与锁定策略生效）。</summary>
    private void TryUnlock()
    {
        var user = AppSession.CurrentUser;
        if (user == null)
        {
            Close();   // 无会话（异常场景）直接放行回到界面处理
            return;
        }

        var result = new UserRepository().Login(user.Username, _txtPwd.Text);
        if (result.Ok)
        {
            DialogResult = DialogResult.OK;
            Close();
            return;
        }

        MessageBox.Show(result.Message, "解锁失败",
            MessageBoxButtons.OK, MessageBoxIcon.Warning);
        _txtPwd.SelectAll();
        _txtPwd.Focus();
    }
}
