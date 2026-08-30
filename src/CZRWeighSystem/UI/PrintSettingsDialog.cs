using CZRWeighSystem.Core;
using CZRWeighSystem.Report;

namespace CZRWeighSystem.UI;

/// <summary>
/// 打印设置对话框（对应 PRD F9-04：磅单纸张、联数、字段显隐、自动打印开关）。
/// [预览效果] 即时套用界面上的选择渲染磅单预览（不落盘）；
/// [保存] 写入 config.json；[取消] 还原为打开时的配置。
/// </summary>
public class PrintSettingsDialog : Form
{
    private ComboBox _cboPaper = null!;
    private NumericUpDown _numCopies = null!;
    private CheckBox _chkAutoPrint = null!;
    private CheckedListBox _lstFields = null!;

    /// <summary>字段目录的 key 列表（与 _lstFields 条目按序对应）</summary>
    private readonly List<string> _keys = [];

    // 打开时的配置快照（取消时还原）
    private string _snapPaper = "";
    private List<string> _snapHidden = [];
    private int _snapCopies;
    private bool _snapAuto;

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
        Size = new Size(470, 585);

        // 纸张与联数
        var grpPaper = new GroupBox
        { Text = "纸张与联数", Location = new Point(12, 10), Size = new Size(430, 130) };
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
        { Text = "磅单字段（勾选＝打印）", Location = new Point(12, 148), Size = new Size(430, 336) };
        _lstFields = new CheckedListBox
        {
            CheckOnClick = true,
            Location = new Point(15, 28),
            Size = new Size(400, 293),
        };
        foreach (var (key, label) in PoundTicketPrinter.FieldCatalog)
        {
            _keys.Add(key);
            _lstFields.Items.Add(label, !AppConfig.Current.PrintHiddenFields.Contains(key));
        }
        grpFields.Controls.Add(_lstFields);

        var btnPreview = new Button
        { Text = "预览效果", Location = new Point(12, 496), Size = new Size(110, 34) };
        btnPreview.Click += (_, _) => Preview();

        var btnOk = new Button
        {
            Text = "保存",
            Location = new Point(244, 496),
            Size = new Size(100, 34),
            BackColor = Color.FromArgb(0, 122, 204),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
        };
        btnOk.Click += (_, _) => SaveAndClose();

        var btnCancel = new Button
        { Text = "取消", Location = new Point(354, 496), Size = new Size(88, 34) };
        btnCancel.Click += (_, _) => { RestoreSnapshot(); Close(); };

        Controls.AddRange(new Control[] { grpPaper, grpFields, btnPreview, btnOk, btnCancel });

        // 载入当前配置
        var cfg = AppConfig.Current;
        int paperIdx = Array.FindIndex(PoundTicketPrinter.Papers, p => p.Key == cfg.PrintPaper);
        _cboPaper.SelectedIndex = paperIdx >= 0 ? paperIdx : 0;
        _numCopies.Value = Math.Clamp(cfg.PrintCopies, 1, 3);
        _chkAutoPrint.Checked = cfg.AutoPrintEnabled;
        _snapPaper = cfg.PrintPaper;
        _snapHidden = [.. cfg.PrintHiddenFields];
        _snapCopies = cfg.PrintCopies;
        _snapAuto = cfg.AutoPrintEnabled;
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
    }

    /// <summary>保存并关闭。</summary>
    private void SaveAndClose()
    {
        ApplyToConfig();
        AppConfig.Current.Save();
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
    }

    /// <summary>预览用的示例记录。</summary>
    private static WeighRecord SampleRecord() => new()
    {
        SerialNo = "PREVIEW-0001",
        VehicleNo = "冀A12345",
        Goods = "煤炭",
        Company = "CZR 收货商B",
        Spec = "一级",
        BatchNo = "B2026",
        GrossKg = 34600,
        TareKg = 11600,
        NetKg = 23000,
        FirstTime = DateTime.Now.AddHours(-1),
        SecondTime = DateTime.Now,
        Operator = AppSession.DisplayName,
        Status = "已完成",
    };
}
