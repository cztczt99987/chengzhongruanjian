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
            // 统一固定 yyyy-MM-dd：Short 格式跟随系统区域，英文系统下会显示成 "31/ 7/2026"
            Format = DateTimePickerFormat.Custom,
            CustomFormat = "yyyy-MM-dd",
            Location = new Point(70, 38),
            Size = new Size(120, 28),
            Value = DateTime.Today.AddDays(-30),   // 默认近 30 天
        };
        _dtpTo = new DateTimePicker
        {
            Format = DateTimePickerFormat.Custom,
            CustomFormat = "yyyy-MM-dd",
            // x=222：给"至"标签（x=193，宽约 20px）留出间距，避免压住本框左边框
            Location = new Point(222, 38),
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
            // 高度留出底部两行：统计文字行（y≈578）+ 按钮行（y=608）
            Size = new Size(1210, 444),
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            ReadOnly = true,
            AllowUserToAddRows = false,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.AllCells,
            // 表头/行高随字体自适应：雅黑 10pt + 高 DPI 下默认固定表头高度会截字
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize,
            AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCellsExceptHeaders,
            BackgroundColor = Color.White,
        };

        // 固定列与「空车去皮/重车过磅」录入弹窗字段一致：车牌、品种、地主姓名/电话、
        // 司机电话、货主姓名 + 结算结果；AllCells 模式按内容自适应列宽（完整展示），
        // 列总宽超出窗体时自动出现横向滚动条
        _grid.Columns.Add("SerialNo", "流水号");
        _grid.Columns.Add("VehicleNo", "车牌号码");
        _grid.Columns.Add("Goods", "品种");
        _grid.Columns.Add("Owner", "地主姓名");
        _grid.Columns.Add("OwnerPhone", "地主电话");
        _grid.Columns.Add("DriverPhone", "司机电话");
        _grid.Columns.Add("CargoOwner", "货主姓名");
        _grid.Columns.Add("UnitPrice", "单价");
        _grid.Columns.Add("Gross", $"毛重({_cfg.UnitDisplay})");
        _grid.Columns.Add("Tare", $"皮重({_cfg.UnitDisplay})");
        _grid.Columns.Add("Net", $"净重({_cfg.UnitDisplay})");
        _grid.Columns.Add("AmountYuan", "金额(元)");
        _grid.Columns.Add("FirstTime", "一次磅时间");
        _grid.Columns.Add("SecondTime", "二次磅时间");
        _grid.Columns.Add("Operator", "司磅员");
        _grid.Columns.Add("Status", "状态");
        // 自定义字段动态列（自定义表单）
        foreach (var f in new CustomFieldRepository().GetAll(enabledOnly: true))
            _grid.Columns.Add("Ext_" + f.Name, f.Name);

        Controls.Add(_grid);
    }

    // ---------- 底部合计与导出 ----------
    private void BuildBottom()
    {
        // 合计标签：独占按钮行上方一行（左对齐、不限宽），完整显示条数与净重合计
        _lblSummary = new Label
        {
            Text = "共 0 条",
            AutoSize = true,
            Location = new Point(20, 578),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left,
            Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold),
            ForeColor = Color.FromArgb(0, 102, 204),
        };

        var btnExport = new Button
        {
            Text = "导出 Excel(CSV)",
            // 右侧按钮链右对齐重排：导出 869 / 磅单预览 1009 / 补打 1119（原磅单预览被导出整个盖住）
            Location = new Point(869, 608),
            Size = new Size(130, 36),
            BackColor = Color.FromArgb(0, 153, 0),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
            AutoSize = true,
            MinimumSize = new Size(130, 36),
        };
        btnExport.Click += (_, _) =>
            CsvExporter.Export(_grid, $"称重记录_{DateTime.Now:yyyyMMdd_HHmmss}.csv");

        // 补打磅单（对应 PRD F5-04：选中已完成记录补打）
        var btnReprint = new Button
        {
            Text = "补打磅单",
            Location = new Point(1119, 608),
            Size = new Size(100, 36),
            BackColor = Color.FromArgb(0, 122, 204),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
            AutoSize = true,
            MinimumSize = new Size(100, 36),
        };
        btnReprint.Click += (_, _) => Reprint();

        // 查看抓拍图片（对应 PRD F7-03）：放在左侧按钮组末尾，与合计标签互不遮挡
        var btnCaptures = new Button
        {
            Text = "查看抓拍",
            Location = new Point(380, 608),
            Size = new Size(110, 36),
            FlatStyle = FlatStyle.Flat,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left,
        };
        btnCaptures.Click += (_, _) => ShowCaptures();

        // 磅单打印预览（打印前确认版式，不耗纸）
        var btnPreview = new Button
        {
            Text = "磅单预览",
            Location = new Point(1009, 608),
            Size = new Size(100, 36),
            FlatStyle = FlatStyle.Flat,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
            AutoSize = true,
            MinimumSize = new Size(100, 36),
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
        Controls.Add(btnCaptures);   // 原先遗漏未加入，导致按钮从未显示

        // 管理员专属：作废 / 留痕（对应 PRD F3-04）。
        // 记录不可修改：称重数据一经保存即固化，错误记录通过"作废 + 重新过磅"修正
        if (_canManage)
        {
            var btnVoid = new Button
            {
                Text = "作废记录",
                Location = new Point(20, 608),
                Size = new Size(110, 36),
                BackColor = Color.FromArgb(200, 60, 60),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left,
            };
            var btnAudit = new Button
            {
                Text = "查看留痕",
                Location = new Point(140, 608),
                Size = new Size(110, 36),
                FlatStyle = FlatStyle.Flat,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left,
            };
            btnVoid.Click += (_, _) => VoidRecord();
            btnAudit.Click += (_, _) => ShowAudits();
            Controls.Add(btnVoid);
            Controls.Add(btnAudit);
        }
    }

    /// <summary>获取选中行对应的记录（未选中返回 null）。</summary>
    private WeighRecord? SelectedRecord => _grid.CurrentRow?.Tag as WeighRecord;

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
        if (ok)
        {
            Sync.PushScheduler.Current?.EnqueueVoid(r.SerialNo, dlg.Reason);   // 平台侧同步作废
            DoQuery();
        }
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
        using (var pi = new PrintingIndicator())
        {
            pi.Show(this);
            Report.PoundTicketPrinter.Print(r);
        }
    }

    // ---------- 业务动作 ----------

    /// <summary>执行查询并填充表格。</summary>
    private void DoQuery()
    {
        string status = _cboStatus.SelectedIndex == 0 ? "" : _cboStatus.Text;

        var list = _repo.Query(_dtpFrom.Value.Date, _dtpTo.Value.Date,
            _txtVehicle.Text, _txtGoods.Text, _txtCompany.Text, status);

        _grid.Rows.Clear();
        var extFields = new CustomFieldRepository().GetAll(enabledOnly: true);
        foreach (var r in list)
        {
            var ext = r.ParseExt();
            int i = _grid.Rows.Add(
                r.SerialNo,
                r.VehicleNo,
                r.Goods ?? "",
                ext.GetValueOrDefault("地主姓名", ""),
                ext.GetValueOrDefault("地主电话", ""),
                ext.GetValueOrDefault("司机电话", ""),
                ext.GetValueOrDefault("货主姓名", ""),
                ext.GetValueOrDefault("单价", ""),
                Fmt(r.GrossKg),
                Fmt(r.TareKg),
                Fmt(r.NetKg),
                r.AmountYuan?.ToString("0.##") ?? "",
                r.FirstTime?.ToString("yyyy-MM-dd HH:mm") ?? "",
                r.SecondTime?.ToString("yyyy-MM-dd HH:mm") ?? "",
                r.Operator ?? "",
                r.Status);
            // 自定义字段动态列取值（固定列 16 个，动态列从索引 16 起）
            for (int k = 0; k < extFields.Count; k++)
                _grid.Rows[i].Cells[16 + k].Value = ext.TryGetValue(extFields[k].Name, out var v) ? v : "";
            _grid.Rows[i].Tag = r;   // 行 Tag 保存完整记录，供补打使用
        }

        // 合计行：车数与净重合计（仅统计已完成记录，对应 PRD F4-01）
        var done = list.Where(r => r.Status == "已完成").ToList();
        double netSum = done.Sum(r => r.NetKg ?? 0);
        _lblSummary.Text = $"共 {list.Count} 条记录（已完成 {done.Count} 车），" +
                           $"净重合计：{_cfg.FormatWeight(netSum)}";
    }

    /// <summary>重量显示（null 显示空）；单位在表头，单元格只显示数字。</summary>
    private string Fmt(double? kg) => kg.HasValue ? _cfg.FormatWeightNumber(kg.Value) : "";

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
