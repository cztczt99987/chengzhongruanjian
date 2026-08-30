using CZRWeighSystem.Database;

namespace CZRWeighSystem.UI;

/// <summary>
/// 修改密码窗体（对应 PRD F1-04 修改密码）。
/// 首次登录强制改密与主界面菜单改密共用本窗体。
/// </summary>
public class ChangePasswordForm : Form
{
    private readonly UserRepository _repo = new();
    private readonly long _userId;

    private TextBox _txtOld = null!;   // 原密码
    private TextBox _txtNew = null!;   // 新密码
    private TextBox _txtConfirm = null!; // 确认新密码
    private Label _lblError = null!;

    public ChangePasswordForm(long userId)
    {
        _userId = userId;

        Text = "修改密码";
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;   // 高 DPI 下避免文字截断
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(380, 260);
        Font = new Font("Microsoft YaHei UI", 10F);

        BuildUi();
    }

    private void BuildUi()
    {
        // 标签列 x=30，输入框列 x=140：确认"确认新密码："这类 6 字标签不与输入框重叠
        var lblOld = new Label { Text = "原密码：", AutoSize = true, Location = new Point(30, 30) };
        _txtOld = new TextBox
        { Location = new Point(140, 25), Size = new Size(210, 28), UseSystemPasswordChar = true };

        var lblNew = new Label { Text = "新密码：", AutoSize = true, Location = new Point(30, 75) };
        _txtNew = new TextBox
        { Location = new Point(140, 70), Size = new Size(210, 28), UseSystemPasswordChar = true };

        var lblConfirm = new Label { Text = "确认新密码：", AutoSize = true, Location = new Point(30, 120) };
        _txtConfirm = new TextBox
        { Location = new Point(140, 115), Size = new Size(210, 28), UseSystemPasswordChar = true };

        _lblError = new Label
        {
            Text = "新密码长度至少 6 位",
            ForeColor = Color.Gray,
            AutoSize = true,
            Location = new Point(140, 152),
        };

        var btnOk = new Button
        {
            Text = "确  定",
            Location = new Point(140, 185),
            Size = new Size(100, 34),
            BackColor = Color.FromArgb(0, 122, 204),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
        };
        var btnCancel = new Button
        {
            Text = "取  消",
            Location = new Point(250, 185),
            Size = new Size(100, 34),
            FlatStyle = FlatStyle.Flat,
        };

        btnOk.Click += (_, _) => DoChange();
        btnCancel.Click += (_, _) =>
        {
            DialogResult = DialogResult.Cancel;
            Close();
        };

        Controls.AddRange(new Control[]
        { lblOld, _txtOld, lblNew, _txtNew, lblConfirm, _txtConfirm, _lblError, btnOk, btnCancel });

        ActiveControl = _txtOld;
    }

    /// <summary>提交修改。</summary>
    private void DoChange()
    {
        var (ok, msg) = _repo.ChangePassword(
            _userId, _txtOld.Text, _txtNew.Text, _txtConfirm.Text);

        if (!ok)
        {
            _lblError.ForeColor = Color.Red;
            _lblError.Text = msg;
            return;
        }

        MessageBox.Show(msg, "成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
        DialogResult = DialogResult.OK;
        Close();
    }
}
