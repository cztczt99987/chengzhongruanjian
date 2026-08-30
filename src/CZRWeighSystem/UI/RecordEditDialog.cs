using CZRWeighSystem.Core;
using CZRWeighSystem.Database;

namespace CZRWeighSystem.UI;

/// <summary>
/// 记录修改窗体（对应 PRD F3-03：已完成记录修改需管理员权限并留痕）。
/// 可修改：车号、货物、单位、规格、批次、备注、毛重、皮重；
/// 毛皮重变化时系统自动重算净重。所有变更逐字段留痕。
/// </summary>
public class RecordEditDialog : Form
{
    private readonly WeighRecord _record;
    private readonly AppConfig _cfg = AppConfig.Current;

    private TextBox _txtVehicle = null!;
    private ComboBox _cboGoods = null!;
    private ComboBox _cboCompany = null!;
    private ComboBox _cboSpec = null!;
    private TextBox _txtBatch = null!;
    private TextBox _txtRemark = null!;
    private TextBox _txtGross = null!;
    private TextBox _txtTare = null!;
    private Label _lblNet = null!;

    /// <summary>保存成功后的新值（窗体关闭后由调用方读取）</summary>
    public RecordEditValues? Result { get; private set; }

    public RecordEditDialog(WeighRecord record)
    {
        _record = record;

        Text = $"修改记录 - {record.SerialNo}";
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;   // 高 DPI 下避免文字截断
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(500, 430);
        Font = new Font("Microsoft YaHei UI", 10F);

        BuildUi();
    }

    private void BuildUi()
    {
        int y = 25;
        Label L(string t, int yy) => new Label { Text = t, AutoSize = true, Location = new Point(30, yy + 4) };

        // 车号
        Controls.Add(L("车号：", y));
        _txtVehicle = new TextBox { Location = new Point(140, y), Size = new Size(280, 28), Text = _record.VehicleNo };
        Controls.Add(_txtVehicle);
        y += 42;

        // 货物
        Controls.Add(L("货物：", y));
        _cboGoods = NewCombo(y, _record.Goods, "煤炭", "矿粉", "钢材", "砂石");
        Controls.Add(_cboGoods);
        y += 42;

        // 单位
        Controls.Add(L("单位：", y));
        _cboCompany = NewCombo(y, _record.Company, "供货商A", "收货商B", "临时客户");
        Controls.Add(_cboCompany);
        y += 42;

        // 规格
        Controls.Add(L("规格：", y));
        _cboSpec = NewCombo(y, _record.Spec, "一级", "二级");
        Controls.Add(_cboSpec);
        y += 42;

        // 批次
        Controls.Add(L("批次：", y));
        _txtBatch = new TextBox { Location = new Point(140, y), Size = new Size(280, 28), Text = _record.BatchNo };
        Controls.Add(_txtBatch);
        y += 42;

        // 备注
        Controls.Add(L("备注：", y));
        _txtRemark = new TextBox { Location = new Point(140, y), Size = new Size(280, 28), Text = _record.Remark };
        Controls.Add(_txtRemark);
        y += 42;

        // 毛重 / 皮重（千克存储，按单位显示编辑；并排放宽防截断）
        Controls.Add(L($"毛重({_cfg.Unit})：", y));
        _txtGross = new TextBox { Location = new Point(140, y), Size = new Size(120, 28), Text = Num(_record.GrossKg) };
        Controls.Add(_txtGross);
        Controls.Add(new Label { Text = $"皮重({_cfg.Unit})：", AutoSize = true, Location = new Point(272, y + 4) });
        _txtTare = new TextBox { Location = new Point(380, y), Size = new Size(100, 28), Text = Num(_record.TareKg) };
        Controls.Add(_txtTare);
        y += 42;

        // 净重（自动计算，只读展示）
        Controls.Add(L($"净重({_cfg.Unit})：", y));
        _lblNet = new Label { Text = CalcNet(), AutoSize = true, Location = new Point(140, y + 4), ForeColor = Color.FromArgb(0, 102, 204) };
        Controls.Add(_lblNet);
        _txtGross.TextChanged += (_, _) => _lblNet.Text = CalcNet();
        _txtTare.TextChanged += (_, _) => _lblNet.Text = CalcNet();
        y += 52;

        // 保存 / 取消
        var btnOk = new Button
        {
            Text = "保存修改",
            Location = new Point(140, y),
            Size = new Size(110, 36),
            BackColor = Color.FromArgb(0, 122, 204),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
        };
        var btnCancel = new Button
        {
            Text = "取 消",
            Location = new Point(270, y),
            Size = new Size(110, 36),
            FlatStyle = FlatStyle.Flat,
        };
        btnOk.Click += (_, _) => Save();
        btnCancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        Controls.Add(btnOk);
        Controls.Add(btnCancel);

        AcceptButton = btnOk;
    }

    private ComboBox NewCombo(int y, string? value, params string[] items)
    {
        var cbo = new ComboBox
        {
            Location = new Point(140, y),
            Size = new Size(280, 28),
            DropDownStyle = ComboBoxStyle.DropDown,
            AutoCompleteMode = AutoCompleteMode.SuggestAppend,
            AutoCompleteSource = AutoCompleteSource.ListItems,
            Text = value ?? "",
        };
        cbo.Items.AddRange(items);
        return cbo;
    }

    /// <summary>千克 → 显示单位数值文本。</summary>
    private string Num(double? kg) =>
        kg.HasValue ? (_cfg.Unit == "t" ? kg.Value / 1000.0 : kg.Value).ToString("0.###") : "";

    /// <summary>实时净重预览。</summary>
    private string CalcNet()
    {
        double g = ParseNum(_txtGross.Text), t = ParseNum(_txtTare.Text);
        return Math.Abs(g - t).ToString("0.###");
    }

    private double ParseNum(string text) =>
        double.TryParse(text, out double v) ? (_cfg.Unit == "t" ? v * 1000 : v) : 0;

    /// <summary>校验并保存。</summary>
    private void Save()
    {
        double gross = ParseNum(_txtGross.Text);
        double tare = ParseNum(_txtTare.Text);
        if (string.IsNullOrWhiteSpace(_txtVehicle.Text))
        {
            MessageBox.Show("车号不能为空", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (gross <= 0 || tare <= 0)
        {
            MessageBox.Show("毛重和皮重必须大于 0", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        Result = new RecordEditValues
        {
            VehicleNo = _txtVehicle.Text,
            Goods = _cboGoods.Text,
            Company = _cboCompany.Text,
            Spec = _cboSpec.Text,
            BatchNo = _txtBatch.Text,
            Remark = _txtRemark.Text,
            GrossKg = gross,
            TareKg = tare,
        };
        DialogResult = DialogResult.OK;
        Close();
    }
}
