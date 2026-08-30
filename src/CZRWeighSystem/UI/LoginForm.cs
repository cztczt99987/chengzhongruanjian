using CZRWeighSystem.Core;
using CZRWeighSystem.Database;

namespace CZRWeighSystem.UI;

/// <summary>
/// 登录窗体（对应 PRD 5.1 登录与用户管理）。
/// 账号密码登录；连续错误 5 次锁定 10 分钟（F1-01）；
/// 首次登录使用初始密码时强制修改（第 3 章说明）。
/// </summary>
public class LoginForm : Form
{
    private readonly UserRepository _repo = new();

    private TextBox _txtUser = null!;     // 账号
    private TextBox _txtPwd = null!;      // 密码
    private Label _lblError = null!;      // 错误提示
    private Button _btnLogin = null!;     // 登录按钮

    public LoginForm()
    {
        Text = "登录 - CZR 智能称重管理系统";
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;   // 高 DPI 下避免文字截断
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(400, 300);
        Font = new Font("Microsoft YaHei UI", 10F);

        BuildUi();
    }

    private void BuildUi()
    {
        // 标题
        var lblTitle = new Label
        {
            Text = "CZR 智能称重管理系统",
            Font = new Font("Microsoft YaHei UI", 16F, FontStyle.Bold),
            ForeColor = Color.FromArgb(0, 102, 204),
            TextAlign = ContentAlignment.MiddleCenter,
            Location = new Point(20, 30),
            Size = new Size(360, 40),
        };

        var lblUser = new Label
        { Text = "账号：", AutoSize = true, Location = new Point(50, 105) };
        _txtUser = new TextBox
        { Location = new Point(120, 100), Size = new Size(210, 28) };

        var lblPwd = new Label
        { Text = "密码：", AutoSize = true, Location = new Point(50, 150) };
        _txtPwd = new TextBox
        {
            Location = new Point(120, 145),
            Size = new Size(210, 28),
            UseSystemPasswordChar = true,   // 密码掩码显示
        };

        _lblError = new Label
        {
            Text = "默认账号 admin，初始密码 123456",
            ForeColor = Color.Gray,
            AutoSize = true,
            Location = new Point(120, 182),
            // 错误提示可能较长（如锁定倒计时），限制在窗体内省略显示
            MaximumSize = new Size(265, 0),
            AutoEllipsis = true,
        };

        _btnLogin = new Button
        {
            Text = "登  录",
            Location = new Point(120, 215),
            Size = new Size(100, 36),
            BackColor = Color.FromArgb(0, 122, 204),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
        };
        var btnExit = new Button
        {
            Text = "退  出",
            Location = new Point(230, 215),
            Size = new Size(100, 36),
            FlatStyle = FlatStyle.Flat,
        };

        _btnLogin.Click += (_, _) => DoLogin();
        btnExit.Click += (_, _) =>
        {
            DialogResult = DialogResult.Cancel;
            Close();
        };

        // 回车即提交（账号框回车跳到密码框）
        _txtUser.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) { _txtPwd.Focus(); e.SuppressKeyPress = true; }
        };
        _txtPwd.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) { DoLogin(); e.SuppressKeyPress = true; }
        };

        Controls.AddRange(new Control[]
        { lblTitle, lblUser, _txtUser, lblPwd, _txtPwd, _lblError, _btnLogin, btnExit });

        AcceptButton = _btnLogin;          // 窗体级回车触发登录
        ActiveControl = _txtUser;          // 打开即聚焦账号
    }

    /// <summary>执行登录验证。</summary>
    private void DoLogin()
    {
        _lblError.ForeColor = Color.Gray;
        _lblError.Text = "正在验证...";

        var result = _repo.Login(_txtUser.Text, _txtPwd.Text);

        if (!result.Ok)
        {
            _lblError.ForeColor = Color.Red;
            _lblError.Text = result.Message;
            _txtPwd.Clear();
            _txtPwd.Focus();
            return;
        }

        var user = result.User!;

        // 首次登录（初始密码）强制修改，取消则不允许进入系统
        if (user.IsDefaultPwd)
        {
            MessageBox.Show("首次登录请先修改初始密码", "提示",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            using var form = new ChangePasswordForm(user.Id);
            if (form.ShowDialog(this) != DialogResult.OK)
            {
                _lblError.ForeColor = Color.Red;
                _lblError.Text = "必须修改初始密码后才能进入系统";
                _txtPwd.Clear();
                _txtPwd.Focus();
                return;
            }
        }

        // 登录成功：记录会话并关闭登录框
        AppSession.CurrentUser = user;
        Log.Info($"用户进入系统：{user.Username}（{user.Role}）");
        DialogResult = DialogResult.OK;
        Close();
    }
}
