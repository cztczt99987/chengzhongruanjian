using System.Data;

namespace CZRWeighSystem.UI;

/// <summary>
/// 摄像头设置窗体（对应 PRD F7-01，限管理员）。
/// 以列表方式配置多路摄像头：每行一路画面，行顺序即画面顺序。
/// 取流地址粘贴慧眼智巡/萤石平台"快速集成"生成的 HTTP-FLV / HLS / RTMP
/// 地址（或摄像头本机 RTSP 地址）；地址留空的行显示模拟画面（调试用）。
/// 保存到 config.json 后由主界面重启取流，立即生效。
/// </summary>
public class CameraSettingsDialog : Form
{
    private CheckBox _chkEnabled = null!;
    private DataGridView _grid = null!;

    private readonly DataTable _table = new();

    public CameraSettingsDialog()
    {
        Text = "摄像头设置";
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;   // 高 DPI 下避免文字截断
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(700, 500);
        Font = new Font("Microsoft YaHei UI", 10F);

        BuildUi();
    }

    private void BuildUi()
    {
        var cfg = AppConfig.Current;

        // ---- 总开关 ----
        _chkEnabled = new CheckBox
        {
            Text = "启用视频监控与抓拍（关闭后不取流、保存称重时不抓拍）",
            AutoSize = true,
            Location = new Point(25, 20),
            Checked = cfg.CameraEnabled,
        };
        Controls.Add(_chkEnabled);

        // ---- 摄像头列表 ----
        var lblList = new Label
        {
            Text = "摄像头列表（每行一路画面；名称留空按 CAM1/CAM2… 默认命名）：",
            AutoSize = true,
            Location = new Point(25, 55),
        };
        Controls.Add(lblList);

        _table.Columns.Add("名称", typeof(string));
        _table.Columns.Add("取流地址", typeof(string));
        foreach (var cam in cfg.Cameras)
            _table.Rows.Add(cam.Name, cam.Url);

        _grid = new DataGridView
        {
            Location = new Point(25, 82),
            Size = new Size(650, 250),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom,
            DataSource = _table,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            RowHeadersVisible = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            BackgroundColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            EditMode = DataGridViewEditMode.EditOnEnter,
        };
        _grid.Columns["名称"].FillWeight = 22;
        _grid.Columns["取流地址"].FillWeight = 78;
        Controls.Add(_grid);

        // ---- 列表操作按钮 ----
        var btnAdd = new Button
        {
            Text = "添加摄像头",
            Location = new Point(25, 342),
            Size = new Size(110, 30),
            Anchor = AnchorStyles.Left | AnchorStyles.Bottom,
            FlatStyle = FlatStyle.Flat,
        };
        btnAdd.Click += (_, _) => _table.Rows.Add("", "");
        Controls.Add(btnAdd);

        var btnDelete = new Button
        {
            Text = "删除选中行",
            Location = new Point(145, 342),
            Size = new Size(110, 30),
            Anchor = AnchorStyles.Left | AnchorStyles.Bottom,
            FlatStyle = FlatStyle.Flat,
        };
        btnDelete.Click += (_, _) =>
        {
            if (_grid.SelectedRows.Count == 0)
            {
                MessageBox.Show("请先在列表中选中要删除的摄像头行", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            foreach (DataGridViewRow row in _grid.SelectedRows)
                if (!row.IsNewRow) _grid.Rows.Remove(row);
        };
        Controls.Add(btnDelete);

        // ---- 说明 ----
        var lblHelp = new Label
        {
            Text = "取流地址获取：登录慧眼智巡 WEB 管理端 → 选中设备 → 快速集成 →\r\n" +
                   "生成 HTTP-FLV（推荐，延迟 1-3 秒）/ HLS / RTMP 地址后粘贴至此；\r\n" +
                   "也可填摄像头本机 RTSP 地址。地址留空 = 该路显示模拟画面（调试用）。",
            AutoSize = true,
            Location = new Point(25, 380),
            ForeColor = Color.Gray,
            Anchor = AnchorStyles.Left | AnchorStyles.Bottom,
        };
        Controls.Add(lblHelp);

        // ---- 保存 / 取消 ----
        var btnSave = new Button
        {
            Text = "保存设置",
            Location = new Point(440, 440),
            Size = new Size(110, 34),
            Anchor = AnchorStyles.Right | AnchorStyles.Bottom,
            BackColor = Color.FromArgb(0, 122, 204),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
        };
        var btnCancel = new Button
        {
            Text = "取 消",
            Location = new Point(565, 440),
            Size = new Size(110, 34),
            Anchor = AnchorStyles.Right | AnchorStyles.Bottom,
            FlatStyle = FlatStyle.Flat,
        };
        btnSave.Click += (_, _) => Save();
        btnCancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        Controls.AddRange(new Control[] { btnSave, btnCancel });

        AcceptButton = btnSave;
        CancelButton = btnCancel;
    }

    /// <summary>把列表内容写回配置并持久化（主界面收到 OK 后重启取流）。</summary>
    private void Save()
    {
        var cfg = AppConfig.Current;
        cfg.CameraEnabled = _chkEnabled.Checked;

        var cameras = new List<CameraConfig>();
        foreach (DataRow row in _table.Rows)
        {
            cameras.Add(new CameraConfig
            {
                Name = row["名称"]?.ToString() ?? "",
                Url = row["取流地址"]?.ToString() ?? "",
            });
        }
        cfg.Cameras = cameras;
        cfg.Save();
        Log.Info($"摄像头设置已保存：{cameras.Count} 路，启用={cfg.CameraEnabled}");
        MessageBox.Show("摄像头设置已保存，画面将按新配置刷新。", "成功",
            MessageBoxButtons.OK, MessageBoxIcon.Information);
        DialogResult = DialogResult.OK;
        Close();
    }
}
