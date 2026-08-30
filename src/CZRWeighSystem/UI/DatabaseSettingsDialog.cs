using CZRWeighSystem.Database;
using Microsoft.Data.SqlClient;

namespace CZRWeighSystem.UI;

/// <summary>
/// 数据库设置窗体（对应 PRD F9-05：单机 SQLite / 网络 SQL Server，连接测试）。
/// 切换数据库类型或修改连接串后需重启程序生效；
/// 网络版多客户端只需指向同一 SQL Server 库即可共享数据。
/// </summary>
public class DatabaseSettingsDialog : Form
{
    private ComboBox _cboType = null!;      // 数据库类型
    private TextBox _txtConn = null!;       // SQL Server 连接串
    private Label _lblHint = null!;

    public DatabaseSettingsDialog()
    {
        Text = "数据库设置";
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;   // 高 DPI 下避免文字截断
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(560, 260);
        Font = new Font("Microsoft YaHei UI", 10F);

        BuildUi();
    }

    private void BuildUi()
    {
        var cfg = AppConfig.Current;

        var lblType = new Label { Text = "数据库类型：", AutoSize = true, Location = new Point(25, 30) };
        _cboType = new ComboBox
        {
            Location = new Point(140, 26),
            Size = new Size(300, 28),
            DropDownStyle = ComboBoxStyle.DropDownList,
        };
        _cboType.Items.Add("Sqlite（单机版，免安装）");
        _cboType.Items.Add("SqlServer（网络版，多机共享）");
        _cboType.SelectedIndex = Db.IsSqlServer ? 1 : 0;
        _cboType.SelectedIndexChanged += (_, _) => UpdateHint();

        var lblConn = new Label { Text = "连接字符串：", AutoSize = true, Location = new Point(25, 70) };
        _txtConn = new TextBox
        {
            Location = new Point(140, 66),
            Size = new Size(390, 28),
            Text = cfg.SqlServerConnectionString,
            Multiline = true,
            Height = 56,
        };

        _lblHint = new Label
        {
            Text = "",
            AutoSize = true,
            Location = new Point(25, 140),
            ForeColor = Color.Gray,
        };

        var btnTest = new Button
        {
            Text = "测试连接",
            Location = new Point(140, 170),
            Size = new Size(110, 34),
            FlatStyle = FlatStyle.Flat,
        };
        var btnSave = new Button
        {
            Text = "保存设置",
            Location = new Point(265, 170),
            Size = new Size(110, 34),
            BackColor = Color.FromArgb(0, 122, 204),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
        };
        var btnCancel = new Button
        {
            Text = "取 消",
            Location = new Point(390, 170),
            Size = new Size(110, 34),
            FlatStyle = FlatStyle.Flat,
        };

        btnTest.Click += (_, _) => TestConnection();
        btnSave.Click += (_, _) => Save();
        btnCancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };

        Controls.AddRange(new Control[]
        { lblType, _cboType, lblConn, _txtConn, _lblHint, btnTest, btnSave, btnCancel });

        UpdateHint();
    }

    /// <summary>当前选择是否为 SQL Server。</summary>
    private bool IsSqlServer => _cboType.SelectedIndex == 1;

    private void UpdateHint()
    {
        _lblHint.Text = IsSqlServer
            ? "网络版：多台电脑连接同一 SQL Server 数据库即可共享数据。切换/修改后需重启程序生效。"
            : "单机版：数据保存在本机 data/weigh.db，无需安装任何数据库服务。";
    }

    /// <summary>
    /// 测试连接：SQLite 尝试打开文件；SQL Server 打开连接并建库（首次连接时）。
    /// </summary>
    private void TestConnection()
    {
        try
        {
            if (IsSqlServer)
            {
                using var conn = new SqlConnection(_txtConn.Text.Trim());
                conn.Open();
                // 首次使用时自动创建 CZRWeigh 库
                using var cmd = conn.CreateCommand();
                cmd.CommandText = """
                    IF DB_ID(N'CZRWeigh') IS NULL CREATE DATABASE CZRWeigh;
                    """;
                cmd.ExecuteNonQuery();
                MessageBox.Show("SQL Server 连接成功（数据库 CZRWeigh 已就绪）", "成功",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                // 单机 SQLite：目录可写即可用
                Directory.CreateDirectory(Path.GetDirectoryName(Db.DbPath)!);
                MessageBox.Show("单机 SQLite 可用，数据库文件位置：\n" + Db.DbPath, "成功",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show("连接失败：" + ex.Message, "错误",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>保存设置（重启程序后生效）。</summary>
    private void Save()
    {
        var cfg = AppConfig.Current;
        cfg.DatabaseType = IsSqlServer ? "SqlServer" : "Sqlite";
        cfg.SqlServerConnectionString = _txtConn.Text.Trim();
        cfg.Save();
        Log.Info($"数据库设置已保存：{cfg.DatabaseType}");
        MessageBox.Show("设置已保存，重启程序后生效。", "成功",
            MessageBoxButtons.OK, MessageBoxIcon.Information);
        DialogResult = DialogResult.OK;
        Close();
    }
}
