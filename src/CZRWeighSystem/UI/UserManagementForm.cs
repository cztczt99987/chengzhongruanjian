using CZRWeighSystem.Core;
using CZRWeighSystem.Database;

namespace CZRWeighSystem.UI;

/// <summary>
/// 用户管理窗体（对应 PRD F1-02 用户管理，仅管理员可用，对应第 3 章权限矩阵）。
/// 展示用户列表，支持新增用户（角色：管理员/称重员）。
/// </summary>
public class UserManagementForm : Form
{
    private readonly UserRepository _repo = new();
    private DataGridView _grid = null!;      // 用户列表
    private Button _btnAdd = null!;          // 新增用户
    private Button _btnDelete = null!;       // 删除用户

    public UserManagementForm()
    {
        // 防御：非管理员直接拒绝打开（入口菜单已按角色隐藏）
        if (AppSession.CurrentUser?.IsAdmin != true)
        {
            Load += (_, _) => Close();
            return;
        }

        Text = "用户管理";
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;   // 高 DPI 下避免文字截断
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(560, 420);
        Font = new Font("Microsoft YaHei UI", 10F);

        BuildUi();
        RefreshGrid();
    }

    private void BuildUi()
    {
        _grid = new DataGridView
        {
            Location = new Point(15, 15),
            Size = new Size(530, 320),
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize,
            BackgroundColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
        };
        _grid.Columns.Add("Username", "账号");
        _grid.Columns.Add("DisplayName", "姓名");
        _grid.Columns.Add("Role", "角色");
        _grid.Columns.Add("Enabled", "状态");
        _grid.Columns.Add("LastLogin", "最后登录");

        _btnAdd = new Button
        {
            Text = "新增用户",
            Location = new Point(15, 355),
            Size = new Size(120, 38),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left,
            BackColor = Color.FromArgb(0, 122, 204),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
        };
        _btnAdd.Click += (_, _) =>
        {
            using var f = new UserAddForm();
            if (f.ShowDialog(this) == DialogResult.OK)
                RefreshGrid();
        };

        _btnDelete = new Button
        {
            Text = "删除用户",
            Location = new Point(150, 355),
            Size = new Size(120, 38),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left,
            BackColor = Color.FromArgb(192, 0, 0),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
        };
        _btnDelete.Click += (_, _) => DoDelete();

        var btnClose = new Button
        {
            Text = "关  闭",
            Location = new Point(425, 355),
            Size = new Size(120, 38),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
            FlatStyle = FlatStyle.Flat,
        };
        btnClose.Click += (_, _) => Close();

        Controls.AddRange(new Control[] { _grid, _btnAdd, _btnDelete, btnClose });
    }

    /// <summary>刷新用户列表。</summary>
    private void RefreshGrid()
    {
        _grid.Rows.Clear();
        foreach (var u in _repo.GetAll())
        {
            int i = _grid.Rows.Add(
                u.Username,
                u.DisplayName,
                u.Role,
                u.Enabled ? "启用" : "停用",
                u.LastLogin?.ToString("yyyy-MM-dd HH:mm") ?? "从未登录");
            _grid.Rows[i].Tag = u;    // 行 Tag 保存用户对象，删除时取 Id
        }
    }

    /// <summary>删除选中用户（确认后执行；规则见 UserRepository.DeleteUser）。</summary>
    private void DoDelete()
    {
        if (_grid.CurrentRow?.Tag is not User u)
        {
            MessageBox.Show("请先选择要删除的用户", "提示",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var answer = MessageBox.Show(
            $"确定删除用户 [{u.Username}]（{u.DisplayName}）？\n删除后无法恢复！",
            "删除确认", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (answer != DialogResult.Yes) return;

        var result = _repo.DeleteUser(u.Id, AppSession.CurrentUser!.Id);
        MessageBox.Show(result.Message, result.Ok ? "成功" : "无法删除",
            MessageBoxButtons.OK, result.Ok ? MessageBoxIcon.Information : MessageBoxIcon.Warning);

        if (result.Ok) RefreshGrid();
    }
}
