using CZRWeighSystem.Core;
using CZRWeighSystem.Database;

namespace CZRWeighSystem.UI;

/// <summary>
/// 基础资料管理窗体（对应 PRD 5.9 / F9-01~F9-05）。
/// 三个页签：车辆档案 / 货物档案 / 收发货单位档案。
/// 编辑方式：表格内直接编辑（单元格结束编辑即保存），
/// 新增走对话框，删除需确认。导入 Excel 后续迭代（F9-05）。
/// </summary>
public class BaseInfoForm : Form
{
    private readonly BaseInfoRepository _repo = new();
    private readonly AppConfig _cfg = AppConfig.Current;

    private TabControl _tabs = null!;
    private DataGridView _gridVehicle = null!;
    private DataGridView _gridGoods = null!;
    private DataGridView _gridCompany = null!;

    /// <summary>内部刷新标记：CellEndEdit 保存后重载时防递归</summary>
    private bool _loading;

    public BaseInfoForm()
    {
        Text = "基础资料管理";
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;   // 高 DPI 下避免文字截断
        Size = new Size(1000, 620);
        MinimumSize = new Size(860, 520);
        Font = new Font("Microsoft YaHei UI", 10F);

        BuildTabs();
        Load += (_, _) => ReloadAll();
    }

    // ================== 界面构建 ==================

    private void BuildTabs()
    {
        _tabs = new TabControl
        {
            Dock = DockStyle.Fill,
        };

        var tabVehicle = new TabPage("车辆档案");
        _gridVehicle = BuildGrid();
        tabVehicle.Controls.Add(BuildGridPanel(_gridVehicle, "新增车辆", AddVehicle, DeleteVehicleRow));
        _tabs.TabPages.Add(tabVehicle);

        var tabGoods = new TabPage("货物档案");
        _gridGoods = BuildGrid();
        tabGoods.Controls.Add(BuildGridPanel(_gridGoods, "新增货物", AddGoods, DeleteGoodsRow));
        _tabs.TabPages.Add(tabGoods);

        var tabCompany = new TabPage("收发货单位");
        _gridCompany = BuildGrid();
        tabCompany.Controls.Add(BuildGridPanel(_gridCompany, "新增单位", AddCompany, DeleteCompanyRow));
        _tabs.TabPages.Add(tabCompany);

        // 页签切换时刷新对应表格
        _tabs.SelectedIndexChanged += (_, _) => ReloadCurrent();

        Controls.Add(_tabs);
    }

    /// <summary>构建可编辑表格。</summary>
    private DataGridView BuildGrid()
    {
        var grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = false,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            BackgroundColor = Color.White,
            BorderStyle = BorderStyle.None,
            MultiSelect = false,
        };
        return grid;
    }

    /// <summary>构建"工具栏 + 表格"面板。</summary>
    private Panel BuildGridPanel(DataGridView grid, string addText,
        Action addHandler, Action deleteHandler)
    {
        var panel = new Panel { Dock = DockStyle.Fill };

        var toolbar = new Panel { Dock = DockStyle.Top, Height = 50 };
        var btnAdd = new Button
        {
            Text = addText,
            Location = new Point(12, 8),
            Size = new Size(110, 34),
            BackColor = Color.FromArgb(0, 122, 204),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
        };
        var btnDelete = new Button
        {
            Text = "删除选中",
            Location = new Point(132, 8),
            Size = new Size(110, 34),
            BackColor = Color.FromArgb(200, 60, 60),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
        };
        var tip = new Label
        {
            Text = "提示：单元格可直接编辑，回车/点击别处即自动保存",
            AutoSize = true,
            Location = new Point(260, 16),
            ForeColor = Color.Gray,
            // 限制宽度+省略：窄窗口时不被工具栏边缘截断
            MaximumSize = new Size(400, 0),
            AutoEllipsis = true,
        };
        btnAdd.Click += (_, _) => addHandler();
        btnDelete.Click += (_, _) => deleteHandler();
        toolbar.Controls.AddRange(new Control[] { btnAdd, btnDelete, tip });

        grid.Dock = DockStyle.Fill;
        panel.Controls.Add(grid);
        panel.Controls.Add(toolbar);
        return panel;
    }

    // ================== 数据加载 ==================

    private void ReloadAll()
    {
        _loading = true;
        LoadVehicleGrid();
        LoadGoodsGrid();
        LoadCompanyGrid();
        _loading = false;
    }

    private void ReloadCurrent()
    {
        if (_loading) return;
        _loading = true;
        switch (_tabs.SelectedIndex)
        {
            case 0: LoadVehicleGrid(); break;
            case 1: LoadGoodsGrid(); break;
            case 2: LoadCompanyGrid(); break;
        }
        _loading = false;
    }

    private DataGridViewTextBoxColumn Col(string name, string header, bool readOnly = false)
    {
        return new DataGridViewTextBoxColumn
        {
            Name = name,
            HeaderText = header,
            ReadOnly = readOnly,
            FillWeight = 20,
        };
    }

    private void LoadVehicleGrid()
    {
        var g = _gridVehicle;
        g.CellEndEdit -= VehicleCellSaved;
        g.Columns.Clear();
        g.Rows.Clear();
        g.Columns.Add(Col("VehicleNo", "车号"));
        g.Columns.Add(Col("Tare", $"默认皮重({_cfg.Unit})"));
        g.Columns.Add(Col("Owner", "车主"));
        g.Columns.Add(Col("Phone", "联系电话"));
        g.Columns.Add(Col("Enabled", "启用"));
        foreach (var v in _repo.GetVehicles())
        {
            int i = g.Rows.Add(v.VehicleNo, Num(v.DefaultTareKg), v.Owner ?? "", v.Phone ?? "", v.Enabled ? "是" : "否");
            g.Rows[i].Tag = v;
        }
        g.CellEndEdit += VehicleCellSaved;
    }

    private void VehicleCellSaved(object? sender, DataGridViewCellEventArgs e)
    {
        if (_loading || e.RowIndex < 0) return;
        if (_gridVehicle.Rows[e.RowIndex].Tag is not Vehicle v) return;
        var row = _gridVehicle.Rows[e.RowIndex];
        v.VehicleNo = row.Cells[0].Value?.ToString() ?? "";
        v.DefaultTareKg = ParseKg(row.Cells[1].Value?.ToString());
        v.Owner = NullIfEmpty(row.Cells[2].Value?.ToString());
        v.Phone = NullIfEmpty(row.Cells[3].Value?.ToString());
        v.Enabled = (row.Cells[4].Value?.ToString()) != "否";
        ShowResult(_repo.SaveVehicle(v));
    }

    private void LoadGoodsGrid()
    {
        var g = _gridGoods;
        g.CellEndEdit -= GoodsCellSaved;
        g.Columns.Clear();
        g.Rows.Clear();
        g.Columns.Add(Col("Name", "货物名称"));
        g.Columns.Add(Col("Spec", "规格"));
        g.Columns.Add(Col("Unit", "计量单位"));
        g.Columns.Add(Col("Rate", "默认扣率(%)"));
        g.Columns.Add(Col("Enabled", "启用"));
        foreach (var x in _repo.GetGoods())
        {
            int i = g.Rows.Add(x.Name, x.Spec ?? "", x.Unit ?? "",
                x.DeductRate?.ToString("0.##") ?? "", x.Enabled ? "是" : "否");
            g.Rows[i].Tag = x;
        }
        g.CellEndEdit += GoodsCellSaved;
    }

    private void GoodsCellSaved(object? sender, DataGridViewCellEventArgs e)
    {
        if (_loading || e.RowIndex < 0) return;
        if (_gridGoods.Rows[e.RowIndex].Tag is not GoodsInfo x) return;
        var row = _gridGoods.Rows[e.RowIndex];
        x.Name = row.Cells[0].Value?.ToString() ?? "";
        x.Spec = NullIfEmpty(row.Cells[1].Value?.ToString());
        x.Unit = NullIfEmpty(row.Cells[2].Value?.ToString());
        x.DeductRate = double.TryParse(row.Cells[3].Value?.ToString(), out double rate) ? rate : null;
        x.Enabled = (row.Cells[4].Value?.ToString()) != "否";
        ShowResult(_repo.SaveGoods(x));
    }

    private void LoadCompanyGrid()
    {
        var g = _gridCompany;
        g.CellEndEdit -= CompanyCellSaved;
        g.Columns.Clear();
        g.Rows.Clear();
        g.Columns.Add(Col("Name", "单位名称"));
        g.Columns.Add(Col("Type", "类型"));
        g.Columns.Add(Col("Contact", "联系人"));
        g.Columns.Add(Col("Phone", "联系电话"));
        g.Columns.Add(Col("Enabled", "启用"));
        foreach (var c in _repo.GetCompanies())
        {
            int i = g.Rows.Add(c.Name, c.Type, c.Contact ?? "", c.Phone ?? "", c.Enabled ? "是" : "否");
            g.Rows[i].Tag = c;
        }
        g.CellEndEdit += CompanyCellSaved;
    }

    private void CompanyCellSaved(object? sender, DataGridViewCellEventArgs e)
    {
        if (_loading || e.RowIndex < 0) return;
        if (_gridCompany.Rows[e.RowIndex].Tag is not CompanyInfo c) return;
        var row = _gridCompany.Rows[e.RowIndex];
        c.Name = row.Cells[0].Value?.ToString() ?? "";
        c.Type = row.Cells[1].Value?.ToString() ?? "收货";
        c.Contact = NullIfEmpty(row.Cells[2].Value?.ToString());
        c.Phone = NullIfEmpty(row.Cells[3].Value?.ToString());
        c.Enabled = (row.Cells[4].Value?.ToString()) != "否";
        ShowResult(_repo.SaveCompany(c));
    }

    // ================== 新增（对话框录入）==================

    private void AddVehicle()
    {
        string? no = Prompt("请输入车号：", "新增车辆");
        if (string.IsNullOrWhiteSpace(no)) return;
        ShowResult(_repo.SaveVehicle(new Vehicle { VehicleNo = no }));
        ReloadCurrent();
    }

    private void AddGoods()
    {
        string? name = Prompt("请输入货物名称：", "新增货物");
        if (string.IsNullOrWhiteSpace(name)) return;
        ShowResult(_repo.SaveGoods(new GoodsInfo { Name = name }));
        ReloadCurrent();
    }

    private void AddCompany()
    {
        string? name = Prompt("请输入单位名称：", "新增单位");
        if (string.IsNullOrWhiteSpace(name)) return;
        ShowResult(_repo.SaveCompany(new CompanyInfo { Name = name }));
        ReloadCurrent();
    }

    // ================== 删除 ==================

    private void DeleteVehicleRow() => DeleteRow(_gridVehicle, id => _repo.DeleteVehicle(id));
    private void DeleteGoodsRow() => DeleteRow(_gridGoods, id => _repo.DeleteGoods(id));
    private void DeleteCompanyRow() => DeleteRow(_gridCompany, id => _repo.DeleteCompany(id));

    private void DeleteRow(DataGridView grid, Func<long, (bool, string)> delete)
    {
        if (grid.CurrentRow?.Tag == null) return;
        var tag = (dynamic)grid.CurrentRow.Tag;
        long id = tag.Id;
        if (MessageBox.Show("确定删除选中的档案记录？删除后不可恢复。", "确认删除",
            MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        var (ok, msg) = delete(id);
        MessageBox.Show(msg, ok ? "成功" : "提示",
            MessageBoxButtons.OK, ok ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        ReloadCurrent();
    }

    // ================== 通用小工具 ==================

    /// <summary>简易输入对话框，返回输入值（取消返回 null）。</summary>
    private static string? Prompt(string text, string title)
    {
        Form prompt = new()
        {
            Width = 400,
            Height = 170,
            Text = title,
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            Font = new Font("Microsoft YaHei UI", 10F),
        };
        var lbl = new Label { Text = text, AutoSize = true, Location = new Point(15, 15) };
        var input = new TextBox { Location = new Point(15, 45), Width = 350 };
        var ok = new Button { Text = "确定", DialogResult = DialogResult.OK, Location = new Point(190, 85), Width = 80 };
        var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, Location = new Point(285, 85), Width = 80 };
        prompt.Controls.AddRange(new Control[] { lbl, input, ok, cancel });
        prompt.AcceptButton = ok;
        return prompt.ShowDialog() == DialogResult.OK ? input.Text.Trim() : null;
    }

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    /// <summary>显示单位 → 千克。</summary>
    private double? ParseKg(string? text)
    {
        if (!double.TryParse(text, out double v)) return null;
        return _cfg.Unit == "t" ? v * 1000 : v;
    }

    /// <summary>千克 → 显示单位文本。</summary>
    private string Num(double? kg) =>
        kg.HasValue ? (_cfg.Unit == "t" ? kg.Value / 1000.0 : kg.Value).ToString("0.###") : "";

    private void ShowResult((bool Ok, string Message) r)
    {
        if (!r.Ok)
            MessageBox.Show(r.Message, "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }
}
