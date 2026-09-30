using System.Data;
using CZRWeighSystem.Video;

namespace CZRWeighSystem.UI;

/// <summary>
/// 摄像头设置窗体（对应 PRD F7-01，限管理员）。
/// 本地网络直连方式配置摄像头：列表每行一路画面，填 IP/端口/用户名/密码
/// （可点"搜索设备"自动发现局域网内海康摄像头），RTSP 取流地址自动生成。
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
        _table.Columns.Add("IP地址", typeof(string));
        _table.Columns.Add("端口", typeof(int));
        _table.Columns.Add("用户名", typeof(string));
        _table.Columns.Add("密码", typeof(string));
        foreach (var cam in cfg.Cameras)
            _table.Rows.Add(cam.Name, cam.Ip, cam.Port, cam.User, cam.Password);

        _grid = new DataGridView
        {
            Location = new Point(25, 82),
            Size = new Size(650, 250),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            RowHeadersVisible = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            // 高 DPI 缩放下默认列头高度(23px)放不下中文，列头/行高按字体自适应
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize,
            AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells,
            BackgroundColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            EditMode = DataGridViewEditMode.EditOnEnter,
        };
        // 手动建列（不依赖自动生成：控件未入窗体前绑定 DataSource 不会立即生成列，
        // 直接按列名取 Columns 会拿到 null —— 真实崩溃案例）
        _grid.AutoGenerateColumns = false;
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "名称", HeaderText = "名称", DataPropertyName = "名称", FillWeight = 20,
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "IP地址", HeaderText = "IP 地址", DataPropertyName = "IP地址", FillWeight = 26,
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "端口", HeaderText = "端口", DataPropertyName = "端口", FillWeight = 10,
            ToolTipText = "RTSP 端口，海康默认 554",
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "用户名", HeaderText = "用户名", DataPropertyName = "用户名", FillWeight = 16,
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "密码", HeaderText = "密码", DataPropertyName = "密码", FillWeight = 28,
        });
        _grid.DataSource = _table;
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
        btnAdd.Click += (_, _) => AddDefaultRow();
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

        // ---- 局域网搜索（海康 SADP 协议）----
        var btnSearch = new Button
        {
            Text = "搜索设备",
            Location = new Point(265, 342),
            Size = new Size(110, 30),
            Anchor = AnchorStyles.Left | AnchorStyles.Bottom,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(0, 122, 204),
            ForeColor = Color.White,
        };
        new ToolTip().SetToolTip(btnSearch, "自动搜索局域网内的海康摄像头，填入选中行");
        btnSearch.Click += async (_, _) =>
        {
            btnSearch.Enabled = false;
            btnSearch.Text = "搜索中…";
            try
            {
                // SADP 广播收包需等待数秒，放后台线程避免界面卡死
                var devices = await Task.Run(() => HikDeviceDiscovery.Search());
                using var dlg = new DeviceListDialog(devices);
                if (dlg.ShowDialog(this) == DialogResult.OK && dlg.SelectedDevice != null)
                    FillDevice(dlg.SelectedDevice);
            }
            finally
            {
                btnSearch.Text = "搜索设备";
                btnSearch.Enabled = true;
            }
        };
        Controls.Add(btnSearch);

        // ---- 说明 ----
        var lblHelp = new Label
        {
            Text = "搜索不到或非海康设备时直接手动填 IP/端口/用户名/密码（端口/用户名一般保持 554/admin）；\r\n" +
                   "密码为摄像头激活时设置的管理密码（萤石云 App → 设备设置中可查），明文保存在本机 config.json；\r\n" +
                   "IP 留空 = 该路显示模拟画面（调试用）。",
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

    /// <summary>新增一行并填海康默认值（端口 554 / 用户名 admin）。</summary>
    private void AddDefaultRow() => _table.Rows.Add("", "", 554, "admin", "");

    /// <summary>把搜索到的设备填入当前选中行（用户名留空补 admin；无选中行则新增一行）。</summary>
    private void FillDevice(HikDeviceInfo device)
    {
        int idx = _grid.CurrentCell?.RowIndex ?? -1;
        DataRow row = idx >= 0 && idx < _table.Rows.Count
            ? _table.Rows[idx]
            : _table.Rows.Add();
        row["IP地址"] = device.Ip;
        if (string.IsNullOrWhiteSpace(row["用户名"]?.ToString())) row["用户名"] = "admin";
        if (row["端口"] is DBNull || (int)row["端口"] <= 0) row["端口"] = 554;
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
                Ip = row["IP地址"]?.ToString()?.Trim() ?? "",
                Port = int.TryParse(row["端口"]?.ToString(), out int port) && port > 0 ? port : 554,
                User = row["用户名"]?.ToString()?.Trim() ?? "",
                Password = row["密码"]?.ToString() ?? "",
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

    /// <summary>
    /// 搜索结果选择对话框：列出 SADP 搜索到的局域网海康设备，
    /// 选中后"填入"（或双击）回填到设置列表的选中行。
    /// </summary>
    private class DeviceListDialog : Form
    {
        /// <summary>用户选中的设备（点"填入"或双击行后非空）</summary>
        public HikDeviceInfo? SelectedDevice { get; private set; }

        public DeviceListDialog(List<HikDeviceInfo> devices)
        {
            Text = "搜索到的海康设备";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(520, 360);
            Font = new Font("Microsoft YaHei UI", 10F);

            var list = new ListView
            {
                Location = new Point(20, 15),
                Size = new Size(480, 250),
                View = View.Details,
                FullRowSelect = true,
                HideSelection = false,
            };
            list.Columns.Add("IP 地址", 150);
            list.Columns.Add("型号", 160);
            list.Columns.Add("MAC 地址", 110);
            list.Columns.Add("SDK 端口", 60);
            if (devices.Count == 0)
                list.Items.Add(new ListViewItem("未搜索到设备（可检查摄像头已通电接入局域网，或改手动填写）"));
            else
                foreach (var d in devices)
                    list.Items.Add(new ListViewItem([d.Ip, d.Model, d.Mac, d.SdkPort.ToString()]));

            var btnFill = new Button
            {
                Text = "填 入",
                Location = new Point(265, 285),
                Size = new Size(110, 34),
                Enabled = false,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(0, 122, 204),
                ForeColor = Color.White,
            };
            var btnCancel = new Button
            {
                Text = "取 消",
                Location = new Point(390, 285),
                Size = new Size(110, 34),
                FlatStyle = FlatStyle.Flat,
            };

            list.SelectedIndexChanged += (_, _) =>
                btnFill.Enabled = list.SelectedIndices.Count > 0 && devices.Count > 0;
            list.DoubleClick += (_, _) => { if (Pick(list)) Close(); };
            btnFill.Click += (_, _) => { if (Pick(list)) Close(); };
            btnCancel.Click += (_, _) => Close();
            Controls.Add(list);
            Controls.AddRange(new Control[] { btnFill, btnCancel });
            return;

            // 选中行转 DialogResult：无选中行时提示并保持打开
            bool Pick(ListView lv)
            {
                if (lv.SelectedItems.Count == 0 || devices.Count == 0) return false;
                string ip = lv.SelectedItems[0].SubItems[0].Text;
                SelectedDevice = devices.FirstOrDefault(d => d.Ip == ip);
                if (SelectedDevice == null)
                {
                    MessageBox.Show("请选择有效的设备行", "提示",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return false;
                }
                DialogResult = DialogResult.OK;
                return true;
            }
        }
    }
}
