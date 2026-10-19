using CZRWeighSystem.Core;
using CZRWeighSystem.Report;

namespace CZRWeighSystem.UI;

/// <summary>
/// 打印设置对话框（对应 PRD F9-04：磅单纸张、联数、字段显隐、自动打印开关）。
/// [预览效果] 即时套用界面上的选择渲染磅单预览（不落盘）；
/// [保存] 写入 config.json；[取消] 还原为打开时的配置。
/// 注意：[预览效果] 会临时改写内存中的全局配置（渲染需要），因此关闭窗口时必须还原，
/// 否则用标题栏的叉关闭会把预览时的联数等设置残留在本次运行中（点一次打印出多张）。
/// </summary>
public class PrintSettingsDialog : Form
{
    private ComboBox _cboPaper = null!;
    private NumericUpDown _numCopies = null!;
    private CheckBox _chkAutoPrint = null!;
    private CheckedListBox _lstFields = null!;
    private ComboBox _cboTemplate = null!;       // 打印模板选择（空项 = 不用模板）

    /// <summary>字段目录的 key 列表（与 _lstFields 条目按序对应）</summary>
    private readonly List<string> _keys = [];

    /// <summary>点过[保存]：关闭时不再还原快照（否则会把已保存的值又从内存里回滚掉）</summary>
    private bool _saved;

    // 打开时的配置快照（取消/叉关闭时还原）
    private string _snapPaper = "";
    private List<string> _snapHidden = [];
    private int _snapCopies;
    private bool _snapAuto;
    private string _snapTpl = "";

    public PrintSettingsDialog()
    {
        Text = "打印设置";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.Dpi;   // 高 DPI 下避免文字截断
        Font = new Font("Microsoft YaHei UI", 10F);
        Size = new Size(470, 600);

        // 打印模板（二期：启用后版式由模板决定，纸张/字段显隐仅对内置版式生效）
        var grpTpl = new GroupBox
        { Text = "打印模板", Location = new Point(12, 10), Size = new Size(430, 110) };
        _cboTemplate = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(80, 30),
            Size = new Size(230, 28),
        };
        RefreshTemplateItems();
        var btnDesigner = new Button
        {
            Text = "模板设计器",
            Location = new Point(316, 24),
            Size = new Size(100, 28),
        };
        btnDesigner.Click += (_, _) => OpenDesigner();
        var btnManage = new Button
        {
            Text = "管理模板",
            Location = new Point(316, 56),
            Size = new Size(100, 28),
        };
        btnManage.Click += (_, _) => OpenManager();
        var lblTplHint = new Label
        {
            Text = "选择模板后按模板版式打印；纸张与字段勾选仅对内置版式生效。",
            Location = new Point(15, 84),
            AutoSize = true,
            ForeColor = Color.FromArgb(96, 96, 96),
        };
        grpTpl.Controls.AddRange(new Control[]
        {
            new Label { Text = "模板：", AutoSize = true, Location = new Point(15, 33) },
            _cboTemplate, btnDesigner, lblTplHint,
        });

        // 纸张与联数
        var grpPaper = new GroupBox
        { Text = "纸张与联数", Location = new Point(12, 128), Size = new Size(430, 130) };
        _cboPaper = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(80, 34),
            Size = new Size(230, 28),
        };
        foreach (var p in PoundTicketPrinter.Papers) _cboPaper.Items.Add(p.Name);
        _numCopies = new NumericUpDown
        {
            Location = new Point(80, 76),
            Size = new Size(70, 28),
            Minimum = 1,
            Maximum = 3,
        };
        _chkAutoPrint = new CheckBox
        {
            Text = "结算完成后自动打印磅单",
            Location = new Point(170, 78),
            AutoSize = true,
        };
        grpPaper.Controls.AddRange(new Control[]
        {
            new Label { Text = "纸张：", AutoSize = true, Location = new Point(15, 37) },
            _cboPaper,
            new Label { Text = "联数：", AutoSize = true, Location = new Point(15, 79) },
            _numCopies,
            _chkAutoPrint,
        });

        // 字段显隐
        var grpFields = new GroupBox
        { Text = "磅单字段（勾选＝打印）", Location = new Point(12, 266), Size = new Size(430, 232) };
        _lstFields = new CheckedListBox
        {
            CheckOnClick = true,
            Location = new Point(15, 28),
            Size = new Size(400, 189),
        };
        foreach (var (key, label) in PoundTicketPrinter.FieldCatalog)
        {
            _keys.Add(key);
            _lstFields.Items.Add(label, !AppConfig.Current.PrintHiddenFields.Contains(key));
        }
        grpFields.Controls.Add(_lstFields);

        var btnPreview = new Button
        { Text = "预览效果", Location = new Point(12, 510), Size = new Size(110, 34) };
        btnPreview.Click += (_, _) => Preview();

        var btnOk = new Button
        {
            Text = "保存",
            Location = new Point(244, 510),
            Size = new Size(100, 34),
            BackColor = Color.FromArgb(0, 122, 204),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
        };
        btnOk.Click += (_, _) => SaveAndClose();

        var btnCancel = new Button
        { Text = "取消", Location = new Point(354, 510), Size = new Size(88, 34) };
        btnCancel.Click += (_, _) => Close();   // 还原快照由 FormClosing 统一处理

        Controls.AddRange(new Control[] { grpTpl, grpPaper, grpFields, btnPreview, btnOk, btnCancel });

        // 载入当前配置
        var cfg = AppConfig.Current;
        int paperIdx = Array.FindIndex(PoundTicketPrinter.Papers, p => p.Key == cfg.PrintPaper);
        _cboPaper.SelectedIndex = paperIdx >= 0 ? paperIdx : 0;
        _numCopies.Value = Math.Clamp(cfg.PrintCopies, 1, 3);
        _chkAutoPrint.Checked = cfg.AutoPrintEnabled;
        SelectTemplateInCombo(cfg.PrintTemplateFile);
        _snapPaper = cfg.PrintPaper;
        _snapHidden = [.. cfg.PrintHiddenFields];
        _snapCopies = cfg.PrintCopies;
        _snapAuto = cfg.AutoPrintEnabled;
        _snapTpl = cfg.PrintTemplateFile;

        // 任何关闭方式（取消按钮、标题栏的叉）都还原快照：点[预览效果]已把界面选择写进内存全局配置，
        // 不还原会把预览用的联数等残留到本次运行（表现为点一次打印出好几张），config.json 却仍是旧值。
        FormClosing += (_, _) =>
        {
            if (!_saved) RestoreSnapshot();
        };
    }

    /// <summary>重新加载模板目录到下拉框（首项"不使用模板"）。</summary>
    private void RefreshTemplateItems()
    {
        _cboTemplate.Items.Clear();
        _cboTemplate.Items.Add("（不使用模板 - 内置版式）");
        foreach (var name in PrintTemplate.ListNames())
            _cboTemplate.Items.Add(name);
    }

    /// <summary>把下拉框定位到指定模板文件名。</summary>
    private void SelectTemplateInCombo(string file)
    {
        _cboTemplate.SelectedIndex =
            string.IsNullOrWhiteSpace(file) ? 0 : _cboTemplate.Items.IndexOf(file);
    }

    /// <summary>打开模板设计器；返回后刷新模板列表并选中刚编辑的文件。</summary>
    private void OpenDesigner()
    {
        using var f = new TemplateDesignerForm();
        f.ShowDialog(this);
        RefreshTemplateItems();
        if (!string.IsNullOrWhiteSpace(f.CurrentFile))
            SelectTemplateInCombo(f.CurrentFile);
    }

    /// <summary>打开模板库管理；返回后按当前配置刷新模板下拉（管理中可能已启用其他模板）。</summary>
    private void OpenManager()
    {
        using var f = new TemplateLibraryForm();
        f.ShowDialog(this);
        RefreshTemplateItems();
        SelectTemplateInCombo(AppConfig.Current.PrintTemplateFile);
    }

    /// <summary>把界面选择套用到配置对象（不落盘）。</summary>
    private void ApplyToConfig()
    {
        var cfg = AppConfig.Current;
        int idx = _cboPaper.SelectedIndex >= 0 ? _cboPaper.SelectedIndex : 0;
        cfg.PrintPaper = PoundTicketPrinter.Papers[idx].Key;
        cfg.PrintCopies = (int)_numCopies.Value;
        cfg.AutoPrintEnabled = _chkAutoPrint.Checked;
        cfg.PrintHiddenFields = _keys
            .Where((_, i) => !_lstFields.GetItemChecked(i))
            .ToList();
        // 下拉索引 0 = 不使用模板
        cfg.PrintTemplateFile = _cboTemplate.SelectedIndex > 0
            ? _cboTemplate.Text : "";
    }

    /// <summary>保存并关闭。</summary>
    private void SaveAndClose()
    {
        ApplyToConfig();
        AppConfig.Current.Save();
        _saved = true;   // 已落盘：关闭时不要再还原（否则内存配置会与 config.json 不一致）
        Close();
    }

    /// <summary>预览：临时套用界面选择渲染磅单（不写配置文件）。</summary>
    private void Preview()
    {
        ApplyToConfig();
        PoundTicketPrinter.PrintPreview(SampleRecord());
    }

    /// <summary>取消：还原为打开对话框时的配置。</summary>
    private void RestoreSnapshot()
    {
        var cfg = AppConfig.Current;
        cfg.PrintPaper = _snapPaper;
        cfg.PrintHiddenFields = [.. _snapHidden];
        cfg.PrintCopies = _snapCopies;
        cfg.AutoPrintEnabled = _snapAuto;
        cfg.PrintTemplateFile = _snapTpl;
    }

    /// <summary>预览用的示例记录。</summary>
    private static WeighRecord SampleRecord() => new()
    {
        SerialNo = "PREVIEW-0001",
        VehicleNo = "冀A12345",
        Goods = "煤炭",
        SenderCompany = "CZR 供货商A",
        ReceiverCompany = "CZR 收货商B",
        Spec = "一级",
        BatchNo = "B2026",
        GrossKg = 34600,
        TareKg = 11600,
        NetKg = 23000,
        DeductKg = 200,
        FirstTime = DateTime.Now.AddHours(-1),
        SecondTime = DateTime.Now,
        Operator = AppSession.DisplayName,
        Status = "已完成",
    };
}
