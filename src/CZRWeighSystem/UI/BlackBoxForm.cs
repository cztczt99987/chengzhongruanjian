using CZRWeighSystem.Core;
using CZRWeighSystem.Database;
using CZRWeighSystem.Utils;

namespace CZRWeighSystem.UI;

/// <summary>
/// 黑匣子记录查询窗体。
/// 条件：日期范围 + 重量上下限；支持查看抓拍图片、导出 Excel(CSV)。
/// </summary>
public class BlackBoxForm : Form
{
    private readonly BlackBoxRepository _repo = new();
    private readonly AppConfig _cfg = AppConfig.Current;

    private DateTimePicker _dtpFrom = null!;     // 开始日期
    private DateTimePicker _dtpTo = null!;       // 结束日期
    private NumericUpDown _numMinKg = null!;     // 重量下限（kg，可空）
    private NumericUpDown _numMaxKg = null!;     // 重量上限（kg，可空）
    private CheckBox _chkMin = null!;            // 启用下限
    private CheckBox _chkMax = null!;            // 启用上限
    private DataGridView _grid = null!;          // 结果表格
    private Label _lblSummary = null!;           // 合计信息

    public BlackBoxForm()
    {
        Text = "黑匣子记录";
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
            Value = DateTime.Today.AddDays(-30),
        };
        _dtpTo = new DateTimePicker
        {
            Format = DateTimePickerFormat.Custom,
            CustomFormat = "yyyy-MM-dd",
            Location = new Point(222, 38),
            Size = new Size(120, 28),
            Value = DateTime.Today,
        };
        _chkMin = new CheckBox { Text = "重量≥", AutoSize = true, Location = new Point(360, 36) };
        _numMinKg = new NumericUpDown
        {
            Location = new Point(420, 35),
            Size = new Size(100, 28),
            DecimalPlaces = 1,
            Maximum = 1000000,
            Enabled = false,
        };
        _chkMin.CheckedChanged += (_, _) => _numMinKg.Enabled = _chkMin.Checked;
        _chkMax = new CheckBox { Text = "重量≤", AutoSize = true, Location = new Point(540, 36) };
        _numMaxKg = new NumericUpDown
        {
            Location = new Point(600, 35),
            Size = new Size(100, 28),
            DecimalPlaces = 1,
            Maximum = 1000000,
            Enabled = false,
        };
        _chkMax.CheckedChanged += (_, _) => _numMaxKg.Enabled = _chkMax.Checked;

        var btnQuery = new Button
        {
            Text = "查 询",
            Location = new Point(930, 33),
            Size = new Size(85, 32),
            BackColor = Color.FromArgb(0, 122, 204),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
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

        panel.Controls.AddRange(new Control[]
        {
            L("日期：", 15, 40), _dtpFrom, L("至", 193, 40), _dtpTo,
            _chkMin, _numMinKg, L("kg", 523, 40), _chkMax, _numMaxKg, L("kg", 703, 40),
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
            MultiSelect = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.AllCells,
            // 表头/行高随字体自适应：雅黑 10pt + 高 DPI 下默认固定表头高度会截字
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize,
            AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCellsExceptHeaders,
            BackgroundColor = Color.White,
        };

        _grid.Columns.Add("Time", "记录时间");
        _grid.Columns.Add("Weight", $"重量({_cfg.Unit})");
        _grid.Columns.Add("Images", "抓拍图片");

        Controls.Add(_grid);
    }

    // ---------- 底部合计与操作 ----------
    private void BuildBottom()
    {
        _lblSummary = new Label
        {
            Text = "共 0 条",
            AutoSize = true,
            Location = new Point(735, 616),
            Anchor = AnchorStyles.Bottom,
            Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold),
            MaximumSize = new Size(130, 0),
            AutoEllipsis = true,
        };

        var btnView = new Button
        {
            Text = "查看抓拍",
            Location = new Point(380, 608),
            Size = new Size(110, 36),
            FlatStyle = FlatStyle.Flat,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left,
        };
        btnView.Click += (_, _) => ShowImages();

        var btnExport = new Button
        {
            Text = "导出 Excel(CSV)",
            Location = new Point(1000, 608),
            Size = new Size(130, 36),
            BackColor = Color.FromArgb(0, 153, 0),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
            AutoSize = true,
            MinimumSize = new Size(130, 36),
        };
        btnExport.Click += (_, _) =>
            CsvExporter.Export(_grid, $"黑匣子记录_{DateTime.Now:yyyyMMdd_HHmmss}.csv");

        Controls.Add(_lblSummary);
        Controls.Add(btnView);
        Controls.Add(btnExport);
    }

    /// <summary>获取选中行对应的记录（未选中返回 null）。</summary>
    private BlackBoxRecord? SelectedRecord => _grid.CurrentRow?.Tag as BlackBoxRecord;

    /// <summary>查看选中记录的抓拍图片（复用抓拍查看窗体）。</summary>
    private void ShowImages()
    {
        var r = SelectedRecord;
        if (r == null)
        {
            MessageBox.Show("请先选择一条记录", "提示",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        // 图片可能被移动/删除，只展示仍存在的文件
        var files = r.ParseImages().Where(File.Exists).ToList();
        if (files.Count == 0)
        {
            MessageBox.Show($"该记录没有抓拍图片\n（未启用摄像头，或图片文件已被移动/删除）",
                "抓拍查询", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        using var f = new CaptureViewForm($"黑匣子 {r.Time:yyyy-MM-dd HH:mm:ss}", files);
        f.ShowDialog(this);
    }

    // ---------- 业务动作 ----------

    /// <summary>执行查询并填充表格。</summary>
    private void DoQuery()
    {
        double? minKg = _chkMin.Checked ? (double)_numMinKg.Value : null;
        double? maxKg = _chkMax.Checked ? (double)_numMaxKg.Value : null;

        var list = _repo.Query(_dtpFrom.Value.Date, _dtpTo.Value.Date, minKg, maxKg);

        _grid.Rows.Clear();
        foreach (var r in list)
        {
            int i = _grid.Rows.Add(
                r.Time.ToString("yyyy-MM-dd HH:mm:ss"),
                _cfg.FormatWeight(r.WeightKg).Replace(" " + _cfg.Unit, ""),
                r.ParseImages().Count);
            _grid.Rows[i].Tag = r;   // 行 Tag 保存完整记录，供查看图片使用
        }

        _lblSummary.Text = $"共 {list.Count} 条";
    }

    /// <summary>重置查询条件。</summary>
    private void ResetCondition()
    {
        _dtpFrom.Value = DateTime.Today.AddDays(-30);
        _dtpTo.Value = DateTime.Today;
        _chkMin.Checked = false;
        _chkMax.Checked = false;
        _numMinKg.Value = 0;
        _numMaxKg.Value = 0;
        DoQuery();
    }
}
