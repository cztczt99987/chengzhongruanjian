using CZRWeighSystem.Core;
using CZRWeighSystem.Database;
using CZRWeighSystem.Utils;

namespace CZRWeighSystem.UI;

/// <summary>
/// 称重记录查询窗体（对应 PRD 5.3 记录管理 / F3-06 多条件查询、F3-07 导出）。
/// 条件：日期范围、车号、货物、单位、状态；结果含车数与净重合计。
/// </summary>
public class RecordQueryForm : Form
{
    private readonly WeighRecordRepository _repo = new();
    private readonly RecordAuditRepository _auditRepo = new();
    private readonly AppConfig _cfg = AppConfig.Current;

    /// <summary>是否具备修改/作废权限（管理员及以上，对应 PRD 第 3 章权限矩阵）</summary>
    private readonly bool _canManage =
        Core.AppSession.CurrentUser?.Role.Contains("管理员") == true;

    private DateTimePicker _dtpFrom = null!;    // 开始日期
    private DateTimePicker _dtpTo = null!;      // 结束日期
    private TextBox _txtVehicle = null!;        // 车号
    private TextBox _txtGoods = null!;          // 货物
    private TextBox _txtCompany = null!;        // 单位
    private ComboBox _cboStatus = null!;        // 状态
    private DataGridView _grid = null!;         // 结果表格
    private Label _lblSummary = null!;          // 合计信息

    public RecordQueryForm()
    {
        Text = "称重记录查询";
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;   // 高 DPI 下避免文字截断
        Size = new Size(1250, 700);
        MinimumSize = new Size(1000, 560);
        Font = new Font("Microsoft YaHei UI", 10F);

        BuildCondition();
        BuildGrid();
        BuildBottom();

        Load += (_, _) => DoQuery();   // 打开即默认查询（近 30 天）
    }

    // ---------- 条件区 ----------
    private void BuildCondition()
    {
        var panel = new GroupBox
        {
            Text = "查询条件",
            Location = new Point(12, 10),
            Size = new Size(1210, 100),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
        };

        Label L(string t, int x, int y) => new Label { Text = t, AutoSize = true, Location = new Point(x, y) };

        _dtpFrom = new DateTimePicker
        {
            Format = DateTimePickerFormat.Short,
            Location = new Point(70, 38),
            Size = new Size(120, 28),
            Value = DateTime.Today.AddDays(-30),   // 默认近 30 天
        };
        _dtpTo = new DateTimePicker
        {
            Format = DateTimePickerFormat.Short,
            Location = new Point(210, 38),
            Size = new Size(120, 28),
            Value = DateTime.Today,
        };
        _txtVehicle = new TextBox { Location = new Point(350, 35), Size = new Size(100, 28) };
        _txtGoods = new TextBox { Location = new Point(500, 35), Size = new Size(100, 28) };
        _txtCompany = new TextBox { Location = new Point(650, 35), Size = new Size(110, 28) };
        _cboStatus = new ComboBox
        {
            Location = new Point(820, 35),
            Size = new Size(90, 28),
            DropDownStyle = ComboBoxStyle.DropDownList,
        };
        _cboStatus.Items.AddRange(new string[] { "全部", "已完成", "未完成", "已作废" });
        _cboStatus.SelectedIndex = 0;

        var btnQuery = new Button
        {
            Text = "查 询",
            Location = new Point(930, 33),
            Size = new Size(85, 32),
            BackColor = Color.FromArgb(0, 122, 204),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            // 跟随面板右缘：窄窗口时不被截断
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
        };
        var btnReset = new Button
        {
            Text = "重 置",
            Location = new Point(1025, 33),
            Size = new Size(75, 32),
            FlatStyle = FlatStyle.Flat,
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
        };
        btnQuery.Click += (_, _) => DoQuery();
        btnReset.Click += (_, _) => ResetCondition();

        // 回车触发查询
        foreach (var txt in new Control[] { _txtVehicle, _txtGoods, _txtCompany })
            txt.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { DoQuery(); e.SuppressKeyPress = true; } };

        panel.Controls.AddRange(new Control[]
        {
            L("日期：", 15, 40), _dtpFrom, L("至", 193, 40), _dtpTo,
            L("车号：", 350 + 0, 15), _txtVehicle,
            L("货物：", 500, 15), _txtGoods,
            L("单位：", 650, 15), _txtCompany,
            L("状态：", 820, 15), _cboStatus,
            btnQuery, btnReset,
        });
        Controls.Add(panel);
    }

    // ---------- 结果表格 ----------
    private void BuildGrid()
    {
        _grid = new DataGridView
        {
            Location = new Point(12, 118),
            Size = new Size(1210, 480),
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            ReadOnly = true,
            AllowUserToAddRows = false,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.AllCells,
            BackgroundColor = Color.White,
        };

        _grid.Columns.Add("SerialNo", "流水号");
        _grid.Columns.Add("VehicleNo", "车号");
        _grid.Columns.Add("Goods", "货物");
        _grid.Columns.Add("Company", "单位");
        _grid.Columns.Add("Gross", $"毛重({_cfg.Unit})");
        _grid.Columns.Add("Tare", $"皮重({_cfg.Unit})");
        _grid.Columns.Add("Net", $"净重({_cfg.Unit})");
        _grid.Columns.Add("FirstTime", "一次磅时间");
        _grid.Columns.Add("SecondTime", "二次磅时间");
        _grid.Columns.Add("Operator", "司磅员");
        _grid.Columns.Add("Status", "状态");

        Controls.Add(_grid);
    }

    // ---------- 底部合计与导出 ----------
    private void BuildBottom()
    {
        // 合计标签：放在底部居中偏左（避开左侧管理按钮区与右侧导出按钮区）
        _lblSummary = new Label
        {
            Text = "共 0 条",
            AutoSize = true,
            Location = new Point(500, 616),
            Anchor = AnchorStyles.Bottom,
            Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold),
            MaximumSize = new Size(360, 0),
            AutoEllipsis = true,
        };

        var btnExport = new Button
        {
            Text = "导出 Excel(CSV)",
            Location = new Point(880, 608),
            Size = new Size(165, 36),
            BackColor = Color.FromArgb(0, 153, 0),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
            AutoSize = true,
            MinimumSize = new Size(165, 36),
        };
        btnExport.Click += (_, _) =>
            CsvExporter.Export(_grid, $"称重记录_{DateTime.Now:yyyyMMdd_HHmmss}.csv");

        // 补打磅单（对应 PRD F5-04：选中已完成记录补打）
        var btnReprint = new Button
        {
            Text = "补打磅单",
            Location = new Point(1055, 608),
            Size = new Size(120, 36),
            BackColor = Color.FromArgb(0, 122, 204),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
            AutoSize = true,
            MinimumSize = new Size(120, 36),
        };
        btnReprint.Click += (_, _) => Reprint();

        // 查看抓拍图片（对应 PRD F7-03）：右锚区第三位，避免与左侧按钮重叠
        var btnCaptures = new Button
        {
            Text = "查看抓拍",
            Location = new Point(750, 608),
            Size = new Size(110, 36),
            FlatStyle = FlatStyle.Flat,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
        };
        btnCaptures.Click += (_, _) => ShowCaptures();

        // 磅单打印预览（打印前确认版式，不耗纸）
        var btnPreview = new Button
        {
            Text = "磅单预览",
            Location = new Point(925, 608),
            Size = new Size(120, 36),
            FlatStyle = FlatStyle.Flat,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
            AutoSize = true,
            MinimumSize = new Size(120, 36),
        };
        btnPreview.Click += (_, _) =>
        {
            if (SelectedRecord is not { } r)
            {
                MessageBox.Show("请先选择一条记录", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            Report.PoundTicketPrinter.PrintPreview(r);
        };

        Controls.Add(_lblSummary);
        Controls.Add(btnExport);
        Controls.Add(btnReprint);
        Controls.Add(btnPreview);

        // 管理员专属：修改 / 作废 / 留痕（对应 PRD F3-03、F3-04）
        if (_canManage)
        {
            var btnEdit = new Button
            {
                Text = "修改记录",
                Location = new Point(20, 608),
                Size = new Size(110, 36),
                BackColor = Color.FromArgb(0, 122, 204),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left,
            };
            var btnVoid = new Button
            {
                Text = "作废记录",
                Location = new Point(140, 608),
                Size = new Size(110, 36),
                BackColor = Color.FromArgb(200, 60, 60),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left,
            };
            var btnAudit = new Button
            {
                Text = "查看留痕",
                Location = new Point(260, 608),
                Size = new Size(110, 36),
                FlatStyle = FlatStyle.Flat,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left,
            };
            btnEdit.Click += (_, _) => EditRecord();
            btnVoid.Click += (_, _) => VoidRecord();
            btnAudit.Click += (_, _) => ShowAudits();
            Controls.Add(btnEdit);
            Controls.Add(btnVoid);
            Controls.Add(btnAudit);
        }
    }

    /// <summary>获取选中行对应的记录（未选中返回 null）。</summary>
    private WeighRecord? SelectedRecord => _grid.CurrentRow?.Tag as WeighRecord;

    /// <summary>修改选中记录（管理员，逐字段留痕，对应 PRD F3-03）。</summary>
    private void EditRecord()
    {
        var r = SelectedRecord;
        if (r == null)
        {
            MessageBox.Show("请先选择一条记录", "提示",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (r.Status == "已作废")
        {
            MessageBox.Show("已作废记录不能修改", "提示",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        using var dlg = new RecordEditDialog(r);
        if (dlg.ShowDialog(this) != DialogResult.OK || dlg.Result == null) return;

        var (ok, msg) = _auditRepo.UpdateRecord(
            r.Id, dlg.Result, Core.AppSession.DisplayName);
        MessageBox.Show(msg, ok ? "成功" : "提示",
            MessageBoxButtons.OK, ok ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        if (ok) DoQuery();
    }

    /// <summary>作废选中记录（管理员，原因留痕，对应 PRD F3-04）。</summary>
    private void VoidRecord()
    {
        var r = SelectedRecord;
        if (r == null)
        {
            MessageBox.Show("请先选择一条记录", "提示",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (r.Status == "已作废")
        {
            MessageBox.Show("该记录已作废", "提示",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        using var dlg = new VoidReasonDialog(r.SerialNo);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        var (ok, msg) = _auditRepo.VoidRecord(r.Id, dlg.Reason, Core.AppSession.DisplayName);
        MessageBox.Show(msg, ok ? "成功" : "提示",
            MessageBoxButtons.OK, ok ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        if (ok) DoQuery();
    }

    /// <summary>查看选中记录的修改/作废留痕（对应 PRD F3-03 修改前后值）。</summary>
    private void ShowAudits()
    {
        var r = SelectedRecord;
        if (r == null)
        {
            MessageBox.Show("请先选择一条记录", "提示",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var audits = _auditRepo.GetAudits(r.Id);
        if (audits.Count == 0)
        {
            MessageBox.Show($"记录 {r.SerialNo} 没有任何修改/作废留痕", "留痕查询",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        // 简易文本展示（后续可升级为表格窗体）
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"记录 {r.SerialNo} 的留痕（共 {audits.Count} 条）：");
        sb.AppendLine();
        foreach (var a in audits)
        {
            sb.AppendLine($"[{a.Time:yyyy-MM-dd HH:mm:ss}] {a.Operator} {a.Action}" +
                (string.IsNullOrEmpty(a.Field) ? "" : $" - {a.Field}") +
                (a.Action == "作废"
                    ? $"，原因：{a.NewValue}"
                    : $"：{a.OldValue ?? "(空)"} → {a.NewValue ?? "(空)"}"));
        }
        MessageBox.Show(sb.ToString(), "留痕查询",
            MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    /// <summary>查看选中记录关联的抓拍图片（对应 PRD F7-03）。</summary>
    private void ShowCaptures()
    {
        var r = SelectedRecord;
        if (r == null)
        {
            MessageBox.Show("请先选择一条记录", "提示",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var files = Video.CameraManager.FindCaptures(r.SerialNo);
        if (files.Count == 0)
        {
            MessageBox.Show($"记录 {r.SerialNo} 没有抓拍图片\n（抓拍图片保存在 exe 目录 captures/ 下）",
                "抓拍查询", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        using var f = new CaptureViewForm(r.SerialNo, files);
        f.ShowDialog(this);
    }

    /// <summary>补打选中记录的磅单（仅已完成记录，对应 PRD F5-04）。</summary>
    private void Reprint()
    {
        if (_grid.CurrentRow?.Tag is not WeighRecord r)
        {
            MessageBox.Show("请先选择一条记录", "提示",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (r.Status != "已完成")
        {
            MessageBox.Show("只有已完成结算的记录才能打印磅单", "提示",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        Report.PoundTicketPrinter.Print(r);
    }

    // ---------- 业务动作 ----------

    /// <summary>执行查询并填充表格。</summary>
    private void DoQuery()
    {
        string status = _cboStatus.SelectedIndex == 0 ? "" : _cboStatus.Text;

        var list = _repo.Query(_dtpFrom.Value.Date, _dtpTo.Value.Date,
            _txtVehicle.Text, _txtGoods.Text, _txtCompany.Text, status);

        _grid.Rows.Clear();
        foreach (var r in list)
        {
            int i = _grid.Rows.Add(
                r.SerialNo,
                r.VehicleNo,
                r.Goods ?? "",
                r.Company ?? "",
                Fmt(r.GrossKg),
                Fmt(r.TareKg),
                Fmt(r.NetKg),
                r.FirstTime?.ToString("yyyy-MM-dd HH:mm") ?? "",
                r.SecondTime?.ToString("yyyy-MM-dd HH:mm") ?? "",
                r.Operator ?? "",
                r.Status);
            _grid.Rows[i].Tag = r;   // 行 Tag 保存完整记录，供补打使用
        }

        // 合计行：车数与净重合计（仅统计已完成记录，对应 PRD F4-01）
        var done = list.Where(r => r.Status == "已完成").ToList();
        double netSum = done.Sum(r => r.NetKg ?? 0);
        _lblSummary.Text = $"共 {list.Count} 条记录（已完成 {done.Count} 车），" +
                           $"净重合计：{_cfg.FormatWeight(netSum)}";
    }

    /// <summary>重量显示（null 显示空）。</summary>
    private string Fmt(double? kg) => kg.HasValue ? _cfg.FormatWeight(kg.Value).Replace(" " + _cfg.Unit, "") : "";

    /// <summary>重置查询条件。</summary>
    private void ResetCondition()
    {
        _dtpFrom.Value = DateTime.Today.AddDays(-30);
        _dtpTo.Value = DateTime.Today;
        _txtVehicle.Clear();
        _txtGoods.Clear();
        _txtCompany.Clear();
        _cboStatus.SelectedIndex = 0;
        DoQuery();
    }
}
