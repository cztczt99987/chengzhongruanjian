using CZRWeighSystem.Core;
using CZRWeighSystem.Database;

namespace CZRWeighSystem.UI;

/// <summary>
/// 新增用户窗体（对应 PRD F1-02 用户管理，仅管理员可用）。
/// 录入账号/姓名/角色/初始密码，创建后首次登录强制修改密码。
/// </summary>
public class UserAddForm : Form
{
    private readonly UserRepository _repo = new();

    private TextBox _txtUser = null!;       // 登录账号
    private TextBox _txtName = null!;       // 姓名
    private ComboBox _cboRole = null!;      // 角色
    private TextBox _txtPwd = null!;        // 初始密码
    private TextBox _txtConfirm = null!;    // 确认密码
    private Label _lblError = null!;

    public UserAddForm()
    {
        // 防御：非管理员直接拒绝打开（入口菜单已按角色隐藏）
        if (AppSession.CurrentUser?.IsAdmin != true)
        {
            Load += (_, _) => Close();
            return;
        }

        Text = "新增用户";
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;   // 高 DPI 下避免文字截断
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(400, 320);
        Font = new Font("Microsoft YaHei UI", 10F);

        BuildUi();
    }

    private void BuildUi()
    {
        // 标签列 x=30，输入框列 x=130
        var lblUser = new Label { Text = "登录账号：", AutoSize = true, Location = new Point(30, 30) };
        _txtUser = new TextBox { Location = new Point(130, 25), Size = new Size(220, 28) };

        var lblName = new Label { Text = "姓名：", AutoSize = true, Location = new Point(30, 70) };
        _txtName = new TextBox { Location = new Point(130, 65), Size = new Size(220, 28) };

        var lblRole = new Label { Text = "角色：", AutoSize = true, Location = new Point(30, 110) };
        _cboRole = new ComboBox
        {
            Location = new Point(130, 105),
            Size = new Size(220, 28),
            DropDownStyle = ComboBoxStyle.DropDownList,   // 仅可选择
        };
        _cboRole.Items.AddRange([User.RoleAdmin, User.RoleWeigher]);
        _cboRole.SelectedIndex = 1;    // 默认称重员

        var lblPwd = new Label { Text = "初始密码：", AutoSize = true, Location = new Point(30, 150) };
        _txtPwd = new TextBox
        { Location = new Point(130, 145), Size = new Size(220, 28), UseSystemPasswordChar = true };

        var lblConfirm = new Label { Text = "确认密码：", AutoSize = true, Location = new Point(30, 190) };
        _txtConfirm = new TextBox
        { Location = new Point(130, 185), Size = new Size(220, 28), UseSystemPasswordChar = true };

        _lblError = new Label
        {
            Text = "初始密码至少 6 位，用户首次登录时将强制修改",
            ForeColor = Color.Gray,
            AutoSize = true,
            Location = new Point(130, 220),
        };

        var btnOk = new Button
        {
            Text = "创  建",
            Location = new Point(130, 255),
            Size = new Size(100, 36),
            BackColor = Color.FromArgb(0, 122, 204),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
        };
        var btnCancel = new Button
        {
            Text = "取  消",
            Location = new Point(250, 255),
            Size = new Size(100, 36),
            FlatStyle = FlatStyle.Flat,
        };

        btnOk.Click += (_, _) => DoAdd();
        btnCancel.Click += (_, _) =>
        {
            DialogResult = DialogResult.Cancel;
            Close();
        };

        Controls.AddRange(new Control[]
        {
            lblUser, _txtUser, lblName, _txtName, lblRole, _cboRole,
            lblPwd, _txtPwd, lblConfirm, _txtConfirm, _lblError, btnOk, btnCancel
        });

        ActiveControl = _txtUser;
    }

    /// <summary>提交新增。</summary>
    private void DoAdd()
    {
        if (_txtPwd.Text != _txtConfirm.Text)
        {
            _lblError.ForeColor = Color.Red;
            _lblError.Text = "两次输入的密码不一致";
            return;
        }

        var result = _repo.AddUser(
            _txtUser.Text, _txtName.Text,
            _cboRole.SelectedItem?.ToString() ?? User.RoleWeigher,
            _txtPwd.Text);

        if (!result.Ok)
        {
            _lblError.ForeColor = Color.Red;
            _lblError.Text = result.Message;
            return;
        }

        MessageBox.Show(result.Message + "，首次登录时需修改初始密码", "成功",
            MessageBoxButtons.OK, MessageBoxIcon.Information);
        DialogResult = DialogResult.OK;
        Close();
    }
}
