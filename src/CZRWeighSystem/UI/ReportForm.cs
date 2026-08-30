using CZRWeighSystem.Core;
using CZRWeighSystem.Database;
using CZRWeighSystem.Utils;

namespace CZRWeighSystem.UI;

/// <summary>
/// 报表统计窗体（对应 PRD 5.4 报表统计）。
/// 支持：日结汇总、按货物/单位/车号/司磅员维度汇总（仅统计已完成记录）。
/// 班结报表随"班次"功能后续扩展（F4-02）。
/// </summary>
public class ReportForm : Form
{
    private readonly WeighRecordRepository _repo = new();
    private readonly AppConfig _cfg = AppConfig.Current;

    /// <summary>报表类型定义：(名称, 分组字段)；日结为特殊分支</summary>
    private static readonly (string Name, string Field)[] ReportTypes =
    {
        ("日结汇总", "daily"),
        ("按货物汇总", "goods"),
        ("按单位汇总", "company"),
        ("按车号汇总", "vehicle_no"),
        ("按司磅员汇总", "operator"),
    };

    private ComboBox _cboType = null!;         // 报表类型
    private DateTimePicker _dtpFrom = null!;   // 开始日期
    private DateTimePicker _dtpTo = null!;     // 结束日期
    private DataGridView _grid = null!;        // 结果表格
    private Label _lblSummary = null!;         // 合计

    public ReportForm()
    {
        Text = "报表统计";
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;   // 高 DPI 下避免文字截断
        Size = new Size(900, 600);
        MinimumSize = new Size(760, 480);
        Font = new Font("Microsoft YaHei UI", 10F);

        BuildTop();
        BuildGrid();
        BuildBottom();

        Load += (_, _) => DoReport();   // 打开即生成默认报表（近 30 天）
    }

    // ---------- 顶部条件 ----------
    private void BuildTop()
    {
        var panel = new GroupBox
        {
            Text = "报表条件",
            Location = new Point(12, 10),
            Size = new Size(860, 80),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
        };

        _cboType = new ComboBox
        {
            Location = new Point(90, 33),
            Size = new Size(130, 28),
            DropDownStyle = ComboBoxStyle.DropDownList,
        };
        foreach (var (name, _) in ReportTypes) _cboType.Items.Add(name);
        _cboType.SelectedIndex = 0;

        _dtpFrom = new DateTimePicker
        {
            Format = DateTimePickerFormat.Short,
            Location = new Point(300, 33),
            Size = new Size(120, 28),
            Value = DateTime.Today.AddDays(-30),
        };
        _dtpTo = new DateTimePicker
        {
            Format = DateTimePickerFormat.Short,
            Location = new Point(440, 33),
            Size = new Size(120, 28),
            Value = DateTime.Today,
        };

        var btnGen = new Button
        {
            Text = "生 成",
            Location = new Point(600, 31),
            Size = new Size(85, 32),
            BackColor = Color.FromArgb(0, 122, 204),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
        };
        btnGen.Click += (_, _) => DoReport();
        _cboType.SelectedIndexChanged += (_, _) => DoReport();

        panel.Controls.AddRange(new Control[]
        {
            new Label { Text = "报表类型：", AutoSize = true, Location = new Point(15, 36) },
            _cboType,
            new Label { Text = "日期：", AutoSize = true, Location = new Point(248, 36) },
            _dtpFrom,
            new Label { Text = "至", AutoSize = true, Location = new Point(423, 36) },
            _dtpTo,
            btnGen,
        });
        Controls.Add(panel);
    }

    // ---------- 结果表格 ----------
    private void BuildGrid()
    {
        _grid = new DataGridView
        {
            Location = new Point(12, 100),
            Size = new Size(860, 390),
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            ReadOnly = true,
            AllowUserToAddRows = false,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            BackgroundColor = Color.White,
        };
        _grid.Columns.Add("Key", "");
        _grid.Columns.Add("Count", "车数");
        _grid.Columns.Add("Gross", $"毛重合计({_cfg.Unit})");
        _grid.Columns.Add("Tare", $"皮重合计({_cfg.Unit})");
        _grid.Columns.Add("Net", $"净重合计({_cfg.Unit})");
        Controls.Add(_grid);
    }

    // ---------- 底部 ----------
    private void BuildBottom()
    {
        // 合计标签：限制宽度避免顶到右侧按钮（长文本省略显示）
        _lblSummary = new Label
        {
            Text = "",
            AutoSize = true,
            Location = new Point(16, 505),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left,
            Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold),
            MaximumSize = new Size(500, 0),
            AutoEllipsis = true,
        };

        var btnExport = new Button
        {
            Text = "导出 Excel(CSV)",
            Location = new Point(710, 498),
            Size = new Size(160, 36),
            BackColor = Color.FromArgb(0, 153, 0),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
            AutoSize = true,
            MinimumSize = new Size(160, 36),
        };
        btnExport.Click += (_, _) =>
            CsvExporter.Export(_grid, $"{_cboType.Text}_{DateTime.Now:yyyyMMdd_HHmmss}.csv");

        Controls.Add(_lblSummary);
        Controls.Add(btnExport);
    }

    // ---------- 业务动作 ----------

    /// <summary>生成当前选中类型的报表。</summary>
    private void DoReport()
    {
        int idx = _cboType.SelectedIndex;
        if (idx < 0) return;

        // 第一列标题随报表类型变化
        _grid.Columns[0].HeaderText = idx == 0 ? "日期" : _cboType.Text.Replace("按", "").Replace("汇总", "");

        List<ReportRow> rows = idx == 0
            ? _repo.SummaryDaily(_dtpFrom.Value.Date, _dtpTo.Value.Date)
            : _repo.SummaryBy(ReportTypes[idx].Field, _dtpFrom.Value.Date, _dtpTo.Value.Date);

        _grid.Rows.Clear();
        foreach (var r in rows)
        {
            _grid.Rows.Add(
                r.Key,
                r.Count,
                Num(r.GrossKg),
                Num(r.TareKg),
                Num(r.NetKg));
        }

        // 底部合计
        _lblSummary.Text = $"合计：{rows.Sum(r => r.Count)} 车，" +
                           $"净重 {_cfg.FormatWeight(rows.Sum(r => r.NetKg))}";
    }

    /// <summary>千克换算为显示单位数值（不带单位后缀）。</summary>
    private string Num(double kg) =>
        (_cfg.Unit == "t" ? kg / 1000.0 : kg).ToString("0.##");
}
