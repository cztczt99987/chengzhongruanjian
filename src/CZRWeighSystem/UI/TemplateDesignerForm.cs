using CZRWeighSystem.Core;
using CZRWeighSystem.Report;
using System.Drawing.Drawing2D;

namespace CZRWeighSystem.UI;

/// <summary>
/// 磅单模板设计器（二期）：左侧元素列表、中间画布、右侧属性编辑。
/// 画布按毫米等比缩放，支持鼠标拖拽移动元素、右下角手柄调整宽高；
/// 工具栏新建/打开/保存模板、插入占位符、调整层级、打印预览。
/// </summary>
public class TemplateDesignerForm : Form
{
    private PrintTemplate _tpl = PrintTemplate.CreateDefault();
    private string _fileName = "";               // 当前模板文件名（空 = 未保存过）
    private bool _dirty;

    /// <summary>当前模板文件名（保存/打开后有效；供打印设置对话框回选）</summary>
    public string CurrentFile => _fileName;

    private readonly ListBox _lstElements = new();
    private readonly PropertyGrid _grid = new();
    private readonly ComboBox _cboPaper = new();
    private readonly ComboBox _cboInsert = new();
    private readonly Canvas _canvas;             // 中间画布

    /// <summary>预览用示例记录</summary>
    private static WeighRecord Sample => new()
    {
        SerialNo = "PREVIEW-0001", ScaleNo = "01", VehicleNo = "冀A12345",
        Goods = "煤炭", Company = "CZR 收货商B", Spec = "一级", BatchNo = "B2026",
        GrossKg = 34600, TareKg = 11600, NetKg = 23000,
        FirstTime = DateTime.Now.AddHours(-1), SecondTime = DateTime.Now,
        Operator = AppSession.DisplayName, Status = "已完成",
    };

    public TemplateDesignerForm()
    {
        Text = "磅单模板设计器";
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Microsoft YaHei UI", 9F);
        Size = new Size(1180, 760);
        MinimumSize = new Size(1000, 640);

        _canvas = new Canvas(this);

        BuildToolbar();
        BuildSide();

        // 全局键盘删除（画布或列表上按 Delete 删除选中元素）
        KeyPreview = true;
        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Delete && _canvas.Selected != null)
            {
                DeleteSelected();
                e.Handled = true;
            }
        };

        FormClosing += (_, e) =>
        {
            if (_dirty && MessageBox.Show("模板尚未保存，确定放弃修改并关闭？",
                    "提示", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                e.Cancel = true;
        };

        RefreshList();
    }

    // ==================== 布局 ====================

    private void BuildToolbar()
    {
        // ToolStrip 自适应宽度，避免手工排布在窄窗口/DPI 缩放下越界
        var top = new ToolStrip
        {
            Dock = DockStyle.Top,
            GripStyle = ToolStripGripStyle.Hidden,
            Padding = new Padding(4, 2, 4, 2),
        };

        _cboPaper.DropDownStyle = ComboBoxStyle.DropDownList;
        _cboPaper.Width = 170;
        foreach (var p in PoundTicketPrinter.Papers) _cboPaper.Items.Add(p.Name);
        _cboPaper.SelectedIndex = Math.Max(0, Array.FindIndex(
            PoundTicketPrinter.Papers, p => p.Key == _tpl.PaperKey));
        _cboPaper.SelectedIndexChanged += (_, _) =>
        {
            _tpl.PaperKey = PoundTicketPrinter.Papers[Math.Max(0, _cboPaper.SelectedIndex)].Key;
            _dirty = true;
            _canvas.NotifyTemplateChanged();
        };

        // 占位符插入：追加到选中文本元素（未选中或非文本则新建一个文本元素）
        foreach (var n in PrintTemplate.PlaceholderNames) _cboInsert.Items.Add("{" + n + "}");
        _cboInsert.SelectedIndex = 0;

        top.Items.AddRange(new ToolStripItem[]
        {
            new ToolStripButton("新建") { ToolTipText = "新建模板" },
            new ToolStripButton("打开") { ToolTipText = "打开模板文件" },
            new ToolStripButton("保存") { ToolTipText = "保存模板到 templates 目录" },
            new ToolStripSeparator(),
            new ToolStripLabel("纸张："), new ToolStripControlHost(_cboPaper),
            new ToolStripSeparator(),
            new ToolStripLabel("插入："), new ToolStripControlHost(_cboInsert),
            new ToolStripButton("插入到文本") { ToolTipText = "把占位符追加到选中文本元素" },
            new ToolStripSeparator(),
            new ToolStripButton("上移一层") { ToolTipText = "选中的元素上移一层" },
            new ToolStripButton("下移一层") { ToolTipText = "选中的元素下移一层" },
            new ToolStripButton("删除元素") { ToolTipText = "删除选中元素（Delete）" },
            new ToolStripSeparator(),
            new ToolStripButton("打印预览") { ToolTipText = "按当前模板预览打印效果" },
        });
        top.ItemClicked += (_, e) =>
        {
            switch (e.ClickedItem?.Text)
            {
                case "新建": NewTemplate(); break;
                case "打开": OpenTemplate(); break;
                case "保存": SaveTemplate(); break;
                case "插入到文本": InsertPlaceholder(); break;
                case "上移一层": Reorder(+1); break;
                case "下移一层": Reorder(-1); break;
                case "删除元素": DeleteSelected(); break;
                case "打印预览":
                    using (var doc = PoundTicketPrinter.BuildTemplateDocument(_tpl, Sample))
                    using (var preview = new PrintPreviewDialog
                           { Document = doc, Width = 800, Height = 600 })
                        preview.ShowDialog();
                    break;
            }
        };
        Controls.Add(top);

        // 状态栏：鼠标处毫米坐标提示
        var status = new StatusStrip();
        var lbl = new ToolStripStatusLabel("就绪");
        status.Items.Add(lbl);
        Controls.Add(status);
        _canvas.HoverChanged = ptMm => lbl.Text =
            $"鼠标：X={ptMm.X:0.#}mm  Y={ptMm.Y:0.#}mm　　拖拽移动元素；拖右下角手柄调整大小";
    }


    private void BuildSide()
    {
        _lstElements.Dock = DockStyle.Left;
        _lstElements.Width = 210;
        _lstElements.SelectedIndexChanged += (_, _) =>
        {
            if (_suppressSync) return;
            _canvas.Selected = _lstElements.SelectedIndex >= 0
                ? _tpl.Elements[_lstElements.SelectedIndex] : null;
            _grid.SelectedObject = _canvas.Selected;
            _canvas.Invalidate();
        };

        _grid.Dock = DockStyle.Right;
        _grid.Width = 260;
        _grid.PropertyValueChanged += (_, e) =>
        {
            _dirty = true;
            // 纸张在属性网格外修改；此处刷新列表描述与画布
            RefreshList();
            _canvas.Invalidate();
        };

        var leftHolder = new Panel { Dock = DockStyle.Left, Width = 210 };
        leftHolder.Controls.Add(_lstElements);

        var rightHolder = new Panel { Dock = DockStyle.Right, Width = 260 };
        rightHolder.Controls.Add(_grid);

        Controls.Add(_canvas);          // 中间（填充剩余）
        Controls.Add(leftHolder);
        Controls.Add(rightHolder);
    }

    // ==================== 模板文件 ====================

    private void NewTemplate()
    {
        if (!ConfirmDiscard()) return;
        _tpl = PrintTemplate.CreateDefault(
            PoundTicketPrinter.Papers[Math.Max(0, _cboPaper.SelectedIndex)].Key);
        _fileName = "";
        _dirty = false;
        _canvas.Selected = null;
        _grid.SelectedObject = null;
        RefreshList();
        _canvas.Invalidate();
    }

    private void OpenTemplate()
    {
        if (!ConfirmDiscard()) return;
        using var dlg = new OpenFileDialog
        {
            Title = "打开磅单模板",
            Filter = "磅单模板 (*.json)|*.json",
            InitialDirectory = PrintTemplate.TemplatesDir,
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            _tpl = PrintTemplate.Load(dlg.FileName);
            _fileName = Path.GetFileName(dlg.FileName);
            _cboPaper.SelectedIndex = Math.Max(0, Array.FindIndex(
                PoundTicketPrinter.Papers, p => p.Key == _tpl.PaperKey));
            _canvas.Selected = null;
            _grid.SelectedObject = null;
            RefreshList();
            _canvas.Invalidate();
            _dirty = false;
        }
        catch (Exception ex)
        {
            MessageBox.Show("模板加载失败：" + ex.Message, "错误",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void SaveTemplate()
    {
        if (_fileName.Length == 0)
        {
            using var dlg = new SaveFileDialog
            {
                Title = "保存磅单模板",
                Filter = "磅单模板 (*.json)|*.json",
                InitialDirectory = PrintTemplate.TemplatesDir,
                FileName = "我的磅单模板.json",
            };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            _fileName = Path.GetFileName(dlg.FileName);
        }
        try
        {
            _tpl.Save(_fileName);
            _dirty = false;
            MessageBox.Show($"已保存：{_fileName}\n可在“系统 → 打印设置”中选择启用该模板。",
                "成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show("保存失败：" + ex.Message, "错误",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private bool ConfirmDiscard()
        => !_dirty || MessageBox.Show("当前模板尚未保存，确定放弃修改？",
            "提示", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;

    // ==================== 元素操作 ====================

    private bool _suppressSync;      // 列表↔画布双向同步时防递归

    /// <summary>画布/属性变化后刷新列表与属性网格。</summary>
    public void SelectionChanged()
    {
        _suppressSync = true;
        int idx = _tpl.Elements.IndexOf(_canvas.Selected!);
        _lstElements.SelectedIndex = _canvas.Selected == null ? -1 : idx;
        _grid.SelectedObject = _canvas.Selected;
        _suppressSync = false;
        _canvas.Invalidate();
    }

    public void MarkDirty()
    {
        _dirty = true;
        RefreshList();
    }

    private void RefreshList()
    {
        _suppressSync = true;
        int sel = _lstElements.SelectedIndex;
        _lstElements.BeginUpdate();
        _lstElements.Items.Clear();
        for (int i = 0; i < _tpl.Elements.Count; i++)
            _lstElements.Items.Add($"{i + 1}. {_tpl.Elements[i].Desc}");
        if (sel >= 0 && sel < _lstElements.Items.Count) _lstElements.SelectedIndex = sel;
        _lstElements.EndUpdate();
        _suppressSync = false;
    }

    /// <summary>插入占位符到选中文本元素（或新建文本元素）。</summary>
    private void InsertPlaceholder()
    {
        var ph = _cboInsert.Text;
        if (_canvas.Selected is TextElement te)
        {
            te.Text += ph;
            MarkDirty();
            SelectionChanged();
        }
        else
        {
            var ne = new TextElement { Text = ph, X = 15, Y = 30, W = 60, H = 8 };
            _tpl.Elements.Add(ne);
            _canvas.Selected = ne;
            MarkDirty();
            SelectionChanged();
        }
    }

    /// <summary>调整选中元素层级（+1 上移一层 / -1 下移一层）。</summary>
    private void Reorder(int dir)
    {
        if (_canvas.Selected == null) return;
        int i = _tpl.Elements.IndexOf(_canvas.Selected);
        int j = i + dir;
        if (j < 0 || j >= _tpl.Elements.Count) return;
        (_tpl.Elements[i], _tpl.Elements[j]) = (_tpl.Elements[j], _tpl.Elements[i]);
        MarkDirty();
        SelectionChanged();
    }

    private void DeleteSelected()
    {
        if (_canvas.Selected == null) return;
        _tpl.Elements.Remove(_canvas.Selected);
        _canvas.Selected = null;
        _grid.SelectedObject = null;
        MarkDirty();
        RefreshList();
        _canvas.Invalidate();
    }

    // ==================== 画布 ====================

    /// <summary>
    /// 模板画布：按毫米→像素缩放绘制模板；支持拖拽移动元素与右下角手柄缩放。
    /// 坐标换算：mm = px / Scale；Scale 由画布宽度 / 纸张宽毫米自动适配。
    /// </summary>
    private sealed class Canvas : Control
    {
        private readonly TemplateDesignerForm _owner;
        public TemplateElement? Selected;

        /// <summary>鼠标移动回调（画布毫米坐标，状态栏提示）</summary>
        public Action<PointF>? HoverChanged;

        private const int Border = 16;                       // 画布内纸张留边（像素）
        private float _scale = 3f;                           // 每毫米像素数（布局时自适应）
        private bool _dragging;
        private bool _sizing;
        private PointF _dragStartMm;                         // 拖拽起始鼠标位置（毫米）
        private TemplateElement? _hit;
        private float _hitDx, _hitDy;                        // 命中点相对元素原点偏移

        public Canvas(TemplateDesignerForm owner)
        {
            _owner = owner;
            DoubleBuffered = true;
            BackColor = Color.FromArgb(96, 96, 96);
            Cursor = Cursors.Cross;
            Resize += (_, _) => FitScale();
        }

        /// <summary>画布宽度变化时重算缩放比例（页面完整显示）。</summary>
        private void FitScale()
        {
            int idx = Math.Max(0, Array.FindIndex(
                PoundTicketPrinter.Papers, p => p.Key == _owner._tpl.PaperKey));
            var paper = PoundTicketPrinter.Papers[idx];
            _scale = Math.Min((Width - Border * 2) / (float)paper.W * 100,
                              (Height - Border * 2) / (float)paper.H * 100);
            if (_scale <= 0) _scale = 1f;
            Invalidate();
        }

        private RectangleF PaperRectPx()
        {
            int idx = Math.Max(0, Array.FindIndex(
                PoundTicketPrinter.Papers, p => p.Key == _owner._tpl.PaperKey));
            var paper = PoundTicketPrinter.Papers[idx];
            return new RectangleF(Border, Border, paper.W / 100f * _scale, paper.H / 100f * _scale);
        }

        private PointF ToMm(Point p) => new(
            (p.X - PaperRectPx().X) / _scale * 100f / 100f * 1f,
            (p.Y - PaperRectPx().Y) / _scale);

        private PointF ToPx(float xMm, float yMm) => new(
            PaperRectPx().X + xMm / 100f * _scale,
            PaperRectPx().Y + yMm / 100f * _scale);

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            var paper = PaperRectPx();
            using (var white = new SolidBrush(Color.White))
                g.FillRectangle(white, paper);
            using (var pen = new Pen(Color.Black, 1))
                g.DrawRectangle(pen, paper.X, paper.Y, paper.Width, paper.Height);

            // 设计网格：每 10mm 一条浅灰线（仅画布内）
            using (var grid = new Pen(Color.FromArgb(230, 230, 230), 1))
            {
                for (int mm = 10; mm < 300; mm += 10)
                {
                    var p1 = ToPx(mm, 0);
                    var p2 = ToPx(mm, paper.Height / _scale);
                    if (p1.X < paper.Right) g.DrawLine(grid, p1.X, paper.Top, p1.X, paper.Bottom);
                }
                for (int mm = 10; mm < 300; mm += 10)
                {
                    var p1 = ToPx(0, mm);
                    var p2 = ToPx(paper.Width / _scale, mm);
                    if (p1.Y < paper.Bottom) g.DrawLine(grid, paper.Left, p1.Y, paper.Right, p1.Y);
                }
            }

            // 模板元素（示例数据渲染，占位符显示实际值效果）
            var r = TemplateDesignerForm.Sample;
            var cfg = AppConfig.Current;
            float pxPerMm = _scale / 100f;
            g.TranslateTransform(paper.X, paper.Y);
            TemplateTicketRenderer.Render(_owner._tpl, g, r, cfg, pxPerMm);
            g.ResetTransform();

            // 选中元素：虚线框 + 右下角尺寸手柄
            if (Selected != null)
            {
                var el = Selected;
                var rect = new RectangleF(
                    paper.X + el.X / 100f * _scale, paper.Y + el.Y / 100f * _scale,
                    Math.Max(6, el.W / 100f * _scale), Math.Max(6, el.H / 100f * _scale));
                using var dash = new Pen(Color.OrangeRed, 1) { DashStyle = DashStyle.Dash };
                g.DrawRectangle(dash, rect.X, rect.Y, rect.Width, rect.Height);
                var handle = HandleRect(rect);
                g.FillRectangle(Brushes.OrangeRed, handle);
            }
        }

        private static RectangleF HandleRect(RectangleF elRect) => new(
            elRect.Right - 6, elRect.Bottom - 6, 6, 6);

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            var mm = ToMm(e.Location);
            HoverChanged?.Invoke(mm);

            if (_dragging && _hit != null)
            {
                _hit.X = Math.Max(0, mm.X - _hitDx);
                _hit.Y = Math.Max(0, mm.Y - _hitDy);
                Invalidate();
                return;
            }
            if (_sizing && _hit != null)
            {
                _hit.W = Math.Max(2, mm.X - _hit.X);
                _hit.H = Math.Max(1, mm.Y - _hit.Y);
                Invalidate();
                return;
            }

            // 悬停改光标
            var hit = HitTest(e.Location, out bool onHandle);
            Cursor = onHandle ? Cursors.SizeNWSE : hit != null ? Cursors.SizeAll : Cursors.Cross;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            var hit = HitTest(e.Location, out bool onHandle);
            if (hit == null)
            {
                Selected = null;
                _owner.SelectionChanged();
                return;
            }
            Selected = hit;
            _owner.SelectionChanged();
            var mm = ToMm(e.Location);
            _hit = hit;
            _dragStartMm = mm;
            if (onHandle)
            {
                _sizing = true;
            }
            else
            {
                _dragging = true;
                _hitDx = mm.X - hit.X;
                _hitDy = mm.Y - hit.Y;
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (_dragging || _sizing)
            {
                _dragging = _sizing = false;
                if (_hit != null) _owner.MarkDirty();
            }
        }

        /// <summary>命中测试：从上层到下层找元素；onHandle=命中右下角尺寸手柄。</summary>
        private TemplateElement? HitTest(Point p, out bool onHandle)
        {
            onHandle = false;
            if (Selected != null)
            {
                var paper = PaperRectPx();
                var rect = new RectangleF(
                    paper.X + Selected.X / 100f * _scale, paper.Y + Selected.Y / 100f * _scale,
                    Math.Max(6, Selected.W / 100f * _scale), Math.Max(6, Selected.H / 100f * _scale));
                if (HandleRect(rect).Contains(p))
                {
                    onHandle = true;
                    return Selected;
                }
            }
            for (int i = _owner._tpl.Elements.Count - 1; i >= 0; i--)
            {
                var el = _owner._tpl.Elements[i];
                var rect = new RectangleF(
                    PaperRectPx().X + el.X / 100f * _scale, PaperRectPx().Y + el.Y / 100f * _scale,
                    Math.Max(6, el.W / 100f * _scale), Math.Max(6, el.H / 100f * _scale));
                if (rect.Contains(p)) return el;
            }
            return null;
        }

        public void NotifyTemplateChanged() => FitScale();
    }
}
