using CZRWeighSystem.Core;
using CZRWeighSystem.Report;
using System.Drawing.Drawing2D;

namespace CZRWeighSystem.UI;

/// <summary>
/// 磅单模板库管理窗体：左侧模板列表（分类筛选 + 名称/文件名搜索）、右侧实时预览。
/// 支持应用启用、打印预览、在设计器打开、复制、重命名、删除、恢复内置模板。
/// 内置模板（文件名"内置_"前缀）首次打开时自动写入 templates/ 目录（不覆盖已有）。
/// </summary>
public class TemplateLibraryForm : Form
{
    /// <summary>列表条目（文件名 + 已加载模板；加载失败的文件不显示）</summary>
    private sealed record Item(string File, PrintTemplate Template);

    private readonly List<Item> _items = [];
    private readonly ListView _list = new();
    private readonly ComboBox _cboCategory = new();
    private readonly TextBox _txtSearch = new();
    private readonly PreviewPanel _preview = new();

    public TemplateLibraryForm()
    {
        Text = "磅单模板管理";
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Microsoft YaHei UI", 9F);
        Size = new Size(1100, 660);
        MinimumSize = new Size(920, 560);

        // Fill 面板必须最先加入：WinForms 停靠按加入逆序布局，后加入的 Top/Left/Bottom 优先占位，
        // 预览面板才能拿到真正的剩余区域（否则铺满整个窗体，画纸被列表遮挡）
        _preview.Dock = DockStyle.Fill;
        Controls.Add(_preview);

        BuildTop();
        BuildList();
        BuildBottom();
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        PrintTemplate.EnsureBuiltinsOnDisk();    // 首次打开写入缺失的内置模板
        ReloadList();
    }

    // ==================== 布局 ====================

    private void BuildTop()
    {
        var top = new Panel { Dock = DockStyle.Top, Height = 40, Padding = new Padding(8, 8, 8, 0) };
        _cboCategory.DropDownStyle = ComboBoxStyle.DropDownList;
        _cboCategory.Width = 120;
        _cboCategory.SelectedIndexChanged += (_, _) => ApplyFilter();
        _txtSearch.Width = 200;
        // 搜索框随输入实时过滤
        _txtSearch.TextChanged += (_, _) => ApplyFilter();

        top.Controls.Add(new Label { Text = "分类：", AutoSize = true, Location = new Point(8, 11) });
        _cboCategory.Location = new Point(56, 7);
        top.Controls.Add(_cboCategory);
        top.Controls.Add(new Label { Text = "搜索：", AutoSize = true, Location = new Point(196, 11) });
        _txtSearch.Location = new Point(244, 7);
        top.Controls.Add(_txtSearch);
        Controls.Add(top);
    }

    private void BuildList()
    {
        _list.Dock = DockStyle.Left;
        _list.Width = 400;
        _list.View = View.Details;
        _list.FullRowSelect = true;
        _list.HideSelection = false;
        _list.MultiSelect = false;
        _list.Columns.Add("名称", 150);
        _list.Columns.Add("分类", 70);
        _list.Columns.Add("文件名", 130);
        _list.Columns.Add("元素数", 46);
        _list.SelectedIndexChanged += (_, _) =>
        {
            _preview.Template = Selected?.Template;
            _preview.Invalidate();
        };
        _list.DoubleClick += (_, _) => OpenInDesigner();
        Controls.Add(_list);
    }

    private void BuildBottom()
    {
        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 48, Padding = new Padding(8, 8, 8, 8) };
        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
        };
        void AddBtn(string text, Action onClick, bool primary = false)
        {
            // 宽度按文字自适应：固定 118px 时 9 个按钮共需约 1134px，
            // 窗口缩窄（如 1024）会把最后一个"关闭"挤出可视区（用户实机复现）
            var b = new Button
            {
                Text = text,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                MinimumSize = new Size(84, 32),
                Padding = new Padding(12, 0, 12, 0),
                Margin = new Padding(0, 0, 8, 0),
            };
            if (primary)
            {
                b.BackColor = Color.FromArgb(0, 122, 204);
                b.ForeColor = Color.White;
                b.FlatStyle = FlatStyle.Flat;
            }
            b.Click += (_, _) => onClick();
            buttons.Controls.Add(b);
        }
        AddBtn("新建", NewTemplate, primary: true);
        AddBtn("应用启用", ApplyEnable);
        AddBtn("打印预览", PreviewTemplate);
        AddBtn("在设计器打开", OpenInDesigner);
        AddBtn("复制", CopyTemplate);
        AddBtn("重命名", RenameTemplate);
        AddBtn("删除", DeleteTemplate);
        AddBtn("恢复内置模板", RestoreBuiltins);
        AddBtn("关闭", Close);
        bottom.Controls.Add(buttons);
        Controls.Add(bottom);
    }

    // ==================== 数据 ====================

    /// <summary>当前选中的模板条目（null = 未选中）。</summary>
    private Item? Selected
        => _list.SelectedItems.Count > 0
            ? _items.FirstOrDefault(i => i.File == (string)_list.SelectedItems[0].Tag!)
            : null;

    /// <summary>从 templates/ 目录重新加载全部模板并按分类/搜索过滤显示。</summary>
    private void ReloadList()
    {
        _items.Clear();
        foreach (var file in PrintTemplate.ListNames())
        {
            try { _items.Add(new Item(file, PrintTemplate.Load(file))); }
            catch (Exception ex) { Log.Error($"模板加载失败（模板管理中已跳过）：{file}", ex); }
        }

        // 分类下拉（保留当前选择）
        var categories = _items.Select(i => i.Template.Category).Distinct().OrderBy(c => c).ToList();
        categories.Insert(0, "全部分类");
        string sel = _cboCategory.Text;
        _cboCategory.Items.Clear();
        _cboCategory.Items.AddRange([.. categories]);
        _cboCategory.SelectedIndex = Math.Max(0, _cboCategory.Items.IndexOf(sel));

        ApplyFilter();
    }

    /// <summary>按分类与搜索关键字过滤列表。</summary>
    private void ApplyFilter()
    {
        string cat = _cboCategory.SelectedIndex > 0 ? _cboCategory.Text : "";
        string kw = _txtSearch.Text.Trim();

        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var it in _items)
        {
            if (cat.Length > 0 && it.Template.Category != cat) continue;
            if (kw.Length > 0
                && !it.Template.Name.Contains(kw, StringComparison.OrdinalIgnoreCase)
                && !it.File.Contains(kw, StringComparison.OrdinalIgnoreCase)) continue;

            var lv = new ListViewItem(it.Template.Name) { Tag = it.File };
            lv.SubItems.Add(it.Template.Category);
            lv.SubItems.Add(it.File);
            lv.SubItems.Add(it.Template.Elements.Count.ToString());
            if (it.File == AppConfig.Current.PrintTemplateFile)
                lv.BackColor = Color.FromArgb(224, 240, 255);   // 高亮当前启用模板
            _list.Items.Add(lv);
        }
        _list.EndUpdate();
    }

    /// <summary>预览用示例记录。</summary>
    private static WeighRecord Sample => new()
    {
        SerialNo = "PREVIEW-0001", ScaleNo = "01", VehicleNo = "冀A12345",
        Goods = "煤炭", SenderCompany = "CZR 供货商A", ReceiverCompany = "CZR 收货商B",
        Spec = "一级", BatchNo = "B2026",
        GrossKg = 34600, TareKg = 11600, NetKg = 23000, DeductKg = 200,
        FirstTime = DateTime.Now.AddHours(-1), SecondTime = DateTime.Now,
        Operator = AppSession.DisplayName, Status = "已完成",
    };

    // ==================== 操作 ====================

    private void ApplyEnable()
    {
        var it = Selected;
        if (it == null) { Warn("请先选择模板。"); return; }
        AppConfig.Current.PrintTemplateFile = it.File;
        AppConfig.Current.Save();
        Log.Info($"启用磅单模板：{it.File}");
        ApplyFilter();                            // 刷新启用高亮
        MessageBox.Show($"已启用模板：{it.Template.Name}\n后续磅单打印将按该模板版式输出。",
            "成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void PreviewTemplate()
    {
        var it = Selected;
        if (it == null) { Warn("请先选择模板。"); return; }
        using var doc = PoundTicketPrinter.BuildTemplateDocument(it.Template, Sample);
        using var preview = new PrintPreviewDialog { Document = doc, Width = 800, Height = 600 };
        preview.ShowDialog(this);
    }

    /// <summary>新建模板：打开空白设计器，保存时命名并落入 templates/ 目录。</summary>
    private void NewTemplate()
    {
        using var f = new TemplateDesignerForm("");   // 空文件名 = 新建模式
        f.ShowDialog(this);
        ReloadList();                                  // 设计器保存后刷新
    }

    private void OpenInDesigner()
    {
        var it = Selected;
        if (it == null) { Warn("请先选择模板。"); return; }
        using var f = new TemplateDesignerForm(it.File);
        f.ShowDialog(this);
        ReloadList();                             // 设计器保存后刷新
    }

    private void CopyTemplate()
    {
        var it = Selected;
        if (it == null) { Warn("请先选择模板。"); return; }
        // 生成不重复的文件名：xxx副本.json / xxx副本2.json ...
        string baseName = Path.GetFileNameWithoutExtension(it.File);
        string newName = $"{baseName}副本";
        for (int n = 2; PrintTemplate.ListNames().Contains(newName + ".json"); n++)
            newName = $"{baseName}副本{n}";
        try
        {
            var copy = PrintTemplate.Load(it.File);
            copy.Name = $"{it.Template.Name}副本";
            copy.Save(newName + ".json");
            ReloadList();
        }
        catch (Exception ex)
        {
            MessageBox.Show("复制失败：" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void RenameTemplate()
    {
        var it = Selected;
        if (it == null) { Warn("请先选择模板。"); return; }
        string? newName = InputBox.Show(this, "重命名模板", "新名称：", it.Template.Name);
        if (string.IsNullOrWhiteSpace(newName) || newName == it.Template.Name) return;

        string newFile = Path.GetFileName(newName.Trim()) + ".json";   // 仅取文件名部分防路径注入
        if (newFile == it.File) return;
        if (PrintTemplate.ListNames().Contains(newFile))
        { Warn("已存在同名模板文件。"); return; }
        try
        {
            File.Move(PrintTemplate.PathOf(it.File), PrintTemplate.PathOf(newFile));
            // 启用中模板被重命名时同步配置
            if (AppConfig.Current.PrintTemplateFile == it.File)
            {
                AppConfig.Current.PrintTemplateFile = newFile;
                AppConfig.Current.Save();
            }
            ReloadList();
        }
        catch (Exception ex)
        {
            MessageBox.Show("重命名失败：" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void DeleteTemplate()
    {
        var it = Selected;
        if (it == null) { Warn("请先选择模板。"); return; }
        if (MessageBox.Show($"确定删除模板「{it.Template.Name}」？\n该操作不可恢复。",
                "确认删除", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;
        try
        {
            // 启用中模板删除时同步清空配置，避免打印时模板加载失败
            if (AppConfig.Current.PrintTemplateFile == it.File)
            {
                AppConfig.Current.PrintTemplateFile = "";
                AppConfig.Current.Save();
            }
            File.Delete(PrintTemplate.PathOf(it.File));
            Log.Info($"删除磅单模板：{it.File}");
            ReloadList();
        }
        catch (Exception ex)
        {
            MessageBox.Show("删除失败：" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void RestoreBuiltins()
    {
        if (MessageBox.Show("将把 5 种内置模板恢复为系统原始版式（覆盖同名内置模板文件），\n用户自建模板不受影响。是否继续？",
                "恢复内置模板", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;
        try
        {
            foreach (var t in PrintTemplate.CreateBuiltins())
                t.Save(PrintTemplate.BuiltinFileName(t));
            ReloadList();
            MessageBox.Show("内置模板已恢复。", "完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show("恢复失败：" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static void Warn(string msg)
        => MessageBox.Show(msg, "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);

    // ==================== 预览面板 ====================

    /// <summary>模板预览面板：按纸张等比缩放渲染示例磅单（设计器画布同款渲染逻辑）。</summary>
    private sealed class PreviewPanel : Control
    {
        public PrintTemplate? Template;

        public PreviewPanel()
        {
            DoubleBuffered = true;
            BackColor = Color.FromArgb(96, 96, 96);
            Resize += (_, _) => Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            if (Template == null)
            {
                using var f = new Font("Microsoft YaHei UI", 10F);
                TextRenderer.DrawText(g, "选择左侧模板查看预览", f, ClientRectangle,
                    Color.White, BackColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                return;
            }

            // 纸张等比适配面板（纸张存储单位 0.01 英寸，统一换算毫米后按元素坐标渲染）
            var (mmW, mmH) = PoundTicketPrinter.PaperSizeMm(Template.PaperKey);
            const int border = 12;
            float pxPerMm = Math.Min((Width - border * 2) / mmW,
                                     (Height - border * 2) / mmH);
            if (pxPerMm <= 0) return;
            var rect = new RectangleF(border, border, mmW * pxPerMm, mmH * pxPerMm);

            using (var white = new SolidBrush(Color.White))
                g.FillRectangle(white, rect);
            using (var pen = new Pen(Color.Black, 1))
                g.DrawRectangle(pen, rect.X, rect.Y, rect.Width, rect.Height);

            g.TranslateTransform(rect.X, rect.Y);
            TemplateTicketRenderer.Render(Template, g, Sample, AppConfig.Current, pxPerMm);
            g.ResetTransform();
        }
    }
}

/// <summary>简单文本输入对话框（重命名用）。</summary>
internal static class InputBox
{
    /// <summary>显示单行输入对话框，返回输入内容（取消返回 null）。</summary>
    public static string? Show(IWin32Window owner, string title, string label, string initial)
    {
        string? result = null;
        using var f = new Form
        {
            Text = title,
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = false,
            Font = new Font("Microsoft YaHei UI", 9.5F),
            Size = new Size(380, 150),
        };
        var txt = new TextBox { Location = new Point(80, 18), Width = 270, Text = initial };
        txt.SelectAll();
        var btnOk = new Button
        { Text = "确定", DialogResult = DialogResult.OK, Location = new Point(190, 62), Size = new Size(75, 30) };
        var btnCancel = new Button
        { Text = "取消", DialogResult = DialogResult.Cancel, Location = new Point(275, 62), Size = new Size(75, 30) };
        btnOk.Click += (_, _) => { result = txt.Text.Trim(); f.Close(); };
        btnCancel.Click += (_, _) => f.Close();

        f.Controls.AddRange(
        [
            new Label { Text = label, AutoSize = true, Location = new Point(18, 21) },
            txt, btnOk, btnCancel,
        ]);
        f.AcceptButton = btnOk;
        f.CancelButton = btnCancel;
        f.ShowDialog(owner);
        return result;
    }
}
