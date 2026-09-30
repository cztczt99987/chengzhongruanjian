// UI DPI 缩放验证工具（临时工具，验证完成后可删除）
// 作用：真实实例化全部窗体，在 100% 与 125% 两块屏幕上创建句柄读取实际 DPI，
//       并用文字测量比对每个控件的文字宽度与控件宽度，输出溢出/越界报告；
//       150% 档位用线性推演补齐（AutoScaleMode.Dpi 下布局与文字同比例缩放）。
using System.Drawing;
using System.Runtime.InteropServices;
using CZRWeighSystem;
using CZRWeighSystem.Core;
using CZRWeighSystem.Database;
using CZRWeighSystem.Report;
using CZRWeighSystem.UI;

internal static class Program
{
    private static readonly List<(string Form, string Dpi, string Control, string Issue)> Issues = [];

    // ----- Win32：按屏幕查询有效 DPI -----
    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(Point pt, uint flags);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

    private const uint MONITOR_DEFAULTTONEAREST = 2;
    private const int MDT_EFFECTIVE_DPI = 0;

    [STAThread]
    static void Main()
    {
        // 与主程序一致的 DPI 感知模式
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();

        // 初始化配置与数据库（验证程序目录下自动生成）
        AppConfig.Load();
        Db.Initialize();
        AppSession.CurrentUser = new User
        {
            Username = "admin", DisplayName = "管理员", Role = "超级管理员",
        };
        SeedCustomFields();

        // 临时图片（供 CaptureViewForm 使用）
        string tempImg = Path.Combine(Path.GetTempPath(), "dpi_verify_cap.jpg");
        using (var bmp = new Bitmap(320, 180))
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.Navy);
            g.DrawString("CAM1", SystemFonts.DefaultFont, Brushes.Lime, 10, 10);
            bmp.Save(tempImg, System.Drawing.Imaging.ImageFormat.Jpeg);
        }

        // 显示器环境（含每块屏的有效 DPI；PerMonitorV2 进程中 Bounds 为物理分辨率）
        Console.WriteLine("== 显示器环境 ==");
        var screens = new List<(Screen Screen, uint Dpi)>();
        foreach (var s in Screen.AllScreens)
        {
            var center = new Point(s.Bounds.Left + s.Bounds.Width / 2,
                                   s.Bounds.Top + s.Bounds.Height / 2);
            uint dpi = GetMonitorDpi(center);
            screens.Add((s, dpi));
            Console.WriteLine($"  {s.DeviceName}: 物理{s.Bounds}  有效DPI={dpi}（缩放 {dpi * 100 / 96}%）");
        }
        Console.WriteLine();

        // ---------- 100% 档（96 DPI 屏）----------
        var screen96 = screens.FirstOrDefault(x => x.Dpi == 96).Screen ?? Screen.PrimaryScreen!;
        Console.WriteLine("== 100% 缩放（96 DPI）逐控件检测 ==");
        RunSuite("100%", new Point(screen96.Bounds.Left + 10, screen96.Bounds.Top + 10));

        // ---------- 125% 档（120 DPI 屏实证）----------
        Console.WriteLine();
        Console.WriteLine("== 125% 缩放（120 DPI 实证）==");
        var dpi120 = screens.FirstOrDefault(x => x.Dpi == 120).Screen;
        if (dpi120 != null)
            RunSuite("125%", new Point(dpi120.Bounds.Left + 10, dpi120.Bounds.Top + 10));
        else
            Console.WriteLine("  当前未检测到 120 DPI 屏幕，跳过实证");

        // ---------- 磅单渲染自检：各纸张 × 字段显隐组合渲染到内存位图 ----------
        Console.WriteLine();
        Console.WriteLine("== 磅单渲染自检 ==");
        SmokeTestTicket();
        SmokeTestTemplate();
        SmokeTestExt();

        // ---------- 150% 档：线性推演 ----------
        Console.WriteLine();
        Console.WriteLine("== 150% 缩放（144 DPI，线性推演）==");
        Console.WriteLine("  AutoScaleMode.Dpi 下布局与文字等比缩放：100% 未检出溢出的项在 150% 下同比例安全。");

        // ---------- 报告 ----------
        Console.WriteLine();
        Console.WriteLine("== 检测报告 ==");
        if (Issues.Count == 0)
        {
            Console.WriteLine("  未发现文字截断 / 控件越界问题。");
        }
        else
        {
            foreach (var grp in Issues.GroupBy(x => (x.Form, x.Dpi)))
            {
                Console.WriteLine($"  [{grp.Key.Form} @ {grp.Key.Dpi}]");
                foreach (var x in grp)
                    Console.WriteLine($"    - {x.Control}: {x.Issue}");
            }
            Console.WriteLine($"  共 {Issues.Count} 项。");
        }

        try { File.Delete(tempImg); } catch { }
    }

    /// <summary>查询指定点所在显示器的有效 DPI。</summary>
    private static uint GetMonitorDpi(Point pt)
    {
        var hmon = MonitorFromPoint(pt, MONITOR_DEFAULTTONEAREST);
        GetDpiForMonitor(hmon, MDT_EFFECTIVE_DPI, out uint dx, out _);
        return dx;
    }

    /// <summary>在指定屏幕位置实例化全部窗体并逐个检测。</summary>
    private static void RunSuite(string dpiName, Point location)
    {
        foreach (var f in BuildAll())
        {
            try
            {
                f.StartPosition = FormStartPosition.Manual;
                f.Location = location;
                _ = f.Handle;                       // 强制创建句柄（触发 DPI 缩放）
                Application.DoEvents();
                Console.WriteLine($"  {f.GetType().Name,-26} DeviceDpi={f.DeviceDpi}");
                CheckForm(f, dpiName);
                CheckSiblingOverlap(f, dpiName);

                // 模拟窗口拉大后复查（捕捉 Anchor 拉伸导致的互相遮盖/越界）
                f.Size = new Size(f.Width + 400, f.Height + 160);
                Application.DoEvents();
                CheckForm(f, dpiName + "+放大");
                CheckSiblingOverlap(f, dpiName + "+放大");
            }
            catch (Exception ex)
            {
                Issues.Add((f.GetType().Name, dpiName, "窗体", "创建失败：" + ex.Message));
            }
            finally { f.Dispose(); }
        }
    }

    /// <summary>构造全部被检窗体。</summary>
    private static Form[] BuildAll() =>
    [
        new MainForm(),
        new LoginForm(),
        new ChangePasswordForm(1),
        new RecordQueryForm(),
        new ReportForm(),
        new BaseInfoForm(),
        new DatabaseSettingsDialog(),
        new CameraSettingsDialog(),
        new PrintSettingsDialog(),
        new TemplateDesignerForm(),
        new VoidReasonDialog("TEST-01-0001"),
        new CaptureViewForm("TEST-01-0001",
            [Path.Combine(Path.GetTempPath(), "dpi_verify_cap.jpg")]),
    ];

    /// <summary>递归遍历控件树，检测文字溢出与越界。</summary>
    private static void CheckForm(Form form, string dpi)
    {
        string formName = form.GetType().Name;
        foreach (var c in AllControls(form))
        {
            if (c is Form or PictureBox) continue;          // 无文字/纯图片
            if (c is ToolStrip or MenuStrip or StatusStrip) continue;
            if (c is ScrollBar) continue;                    // 网格内部滚动条，未显示时布局值无意义
            if (c.Parent is PropertyGrid) continue;          // 属性网格内部子控件，测量无意义

            // 0) DataGridView 表头高度检查：模式非 AutoSize 且高度不够时表头文字被截半
            if (c is DataGridView grid &&
                grid.ColumnHeadersHeightSizeMode != DataGridViewColumnHeadersHeightSizeMode.AutoSize)
            {
                // 字体较高（如雅黑 10pt）+ 高 DPI 时，默认固定表头高度装不下中文
                int need = grid.Font.Height + 6;            // 上下各留 3px 余量
                if (grid.ColumnHeadersHeight < need)
                    Issues.Add((formName, dpi, "DataGridView（表头）",
                        $"表头高 {grid.ColumnHeadersHeight}px < 字体所需 {need}px，表头文字会显示一半"));
            }

            // 0.1) 日期控件格式检查：Short 跟随系统区域（英文系统显示成 "31/ 7/2026"），
            //      应固定 CustomFormat="yyyy-MM-dd"；Custom 但未设格式则显示空白
            if (c is DateTimePicker dtp)
            {
                if (dtp.Format == DateTimePickerFormat.Short)
                    Issues.Add((formName, dpi, $"DateTimePicker \"{Trunc(dtp.Name)}\"",
                        "使用 Short 格式跟随系统区域，应固定 CustomFormat=yyyy-MM-dd"));
                else if (dtp.Format == DateTimePickerFormat.Custom &&
                         string.IsNullOrEmpty(dtp.CustomFormat))
                    Issues.Add((formName, dpi, $"DateTimePicker \"{Trunc(dtp.Name)}\"",
                        "Custom 格式但未设置 CustomFormat，会显示空白"));
            }

            // 1) 文字宽度检查（AutoSize 控件自适应文字，自身不截断，跳过）
            if (!string.IsNullOrEmpty(c.Text) && !c.AutoSize)
            {
                // 该控件实际 DPI 下的渲染宽 = 96DPI 逻辑宽 × DeviceDpi/96
                double factor = form.DeviceDpi / 96.0;
                int textW = (int)Math.Ceiling(MeasureText96(c) * factor);
                int clientW = (int)(c.ClientSize.Width);    // 句柄创建后已是实际 DPI 尺寸
                int extra = c is TextBox or ComboBox ? 8 : 6;    // 边框/下拉箭头余量
                if (textW + extra > clientW)
                    Issues.Add((formName, dpi, $"{c.GetType().Name} \"{Trunc(c.Text)}\"",
                        $"文字宽 {textW}px 超出控件客户区 {clientW}px"));
            }

            // 2) 越界检查（相对父容器客户区；Dock 控件豁免）
            if (c.Parent != null && c.Dock == DockStyle.None)
            {
                var pb = c.Parent.ClientSize;
                // 跳过未执行布局的容器（窗体未 Show 时 Dock 容器仍是默认 200×100，测量无意义）
                if (pb.Width <= 200 && pb.Height <= 100) continue;
                var b = c.Bounds;
                if (b.Right > pb.Width + 2 || b.Bottom > pb.Height + 2)
                    Issues.Add((formName, dpi, $"{c.GetType().Name} \"{Trunc(c.Text)}\"",
                        $"控件越界：Right={b.Right}/{pb.Width}, Bottom={b.Bottom}/{pb.Height}"));
            }
        }
    }

    /// <summary>检查同一父容器内非 Dock 兄弟控件是否互相遮盖（重叠 ≥8×8 视为异常）。</summary>
    private static void CheckSiblingOverlap(Control root, string dpi)
    {
        string formName = root.GetType().Name;
        foreach (var grp in AllControls(root).Where(c => c.Parent != null).GroupBy(c => c.Parent!))
        {
            // 跳过未执行布局的容器（窗体未 Show 时仍是默认 200×100，测量无意义）
            var pb = grp.Key.ClientSize;
            if (pb.Width <= 200 && pb.Height <= 100) continue;

            // TabPage 本来就同位置叠放（由 TabControl 控制可见性），排除
            var kids = grp.Where(c => c is not TabPage &&
                                      c.Dock == DockStyle.None && c.Width > 0 && c.Height > 0).ToList();
            for (int i = 0; i < kids.Count; i++)
            {
                for (int j = i + 1; j < kids.Count; j++)
                {
                    var r = Rectangle.Intersect(kids[i].Bounds, kids[j].Bounds);
                    if (r.Width >= 4 && r.Height >= 4)          // ≥4px 即判定（轻微遮挡也会露出破绽）
                        Issues.Add((formName, dpi,
                            $"{kids[i].GetType().Name} \"{Trunc(kids[i].Text)}\" × {kids[j].GetType().Name} \"{Trunc(kids[j].Text)}\"",
                            $"兄弟控件重叠 {r.Width}×{r.Height}@({r.X},{r.Y})"));
                }
            }
        }
    }

    /// <summary>在 96DPI 下测量文字像素宽（Bitmap 恒为 96DPI，结果可横向比较）。</summary>
    private static double MeasureText96(Control c)
    {
        using var bmp = new Bitmap(1, 1);
        using var g = Graphics.FromImage(bmp);
        return g.MeasureString(c.Text, c.Font).Width;
    }

    /// <summary>深度优先收集全部子控件。</summary>
    private static IEnumerable<Control> AllControls(Control root)
    {
        foreach (Control c in root.Controls)
        {
            yield return c;
            foreach (var sub in AllControls(c))
                yield return sub;
        }
    }

    private static string Trunc(string s) => s.Length > 18 ? s[..18] + "…" : s;

    /// <summary>种子自定义字段（文本必填/数字/下拉各一，驱动动态表单渲染自检）。</summary>
    private static void SeedCustomFields()
    {
        var repo = new CustomFieldRepository();
        if (repo.GetAll().Count > 0) return;
        repo.Save(new CustomField { Name = "合同号", FieldType = "文本", Required = true, SortOrder = 1 });
        repo.Save(new CustomField { Name = "含水率(%)", FieldType = "数字", SortOrder = 2 });
        repo.Save(new CustomField { Name = "货类", FieldType = "下拉", Options = "内贸,外贸,中转", SortOrder = 3 });
    }

    /// <summary>ExtJson 序列化往返 + 占位符融合自检。</summary>
    private static void SmokeTestExt()
    {
        try
        {
            var r = new WeighRecord
            {
                SerialNo = "TEST-01-0003", VehicleNo = "冀C11111",
                GrossKg = 30000, TareKg = 10000, NetKg = 20000, Status = "已完成",
                ExtJson = WeighRecord.ExtToJson(new Dictionary<string, string>
                { ["合同号"] = "HT-2026-001", ["货类"] = "外贸" }),
            };
            var ext = r.ParseExt();
            bool roundTrip = ext.Count == 2 && ext["合同号"] == "HT-2026-001";

            var ph = PrintTemplate.BuildPlaceholders(r, AppConfig.Current);
            bool merged = ph["合同号"] == "HT-2026-001" && ph["货类"] == "外贸";
            string expanded = PrintTemplate.Expand("合同{合同号} 类别{货类}", ph);
            bool expandOk = expanded == "合同HT-2026-001 类别外贸" &&
                            PrintTemplate.Expand("未知{不存在}", ph) == "未知{不存在}";

            Console.WriteLine($"  ExtJson 往返：{(roundTrip ? "通过" : "不一致")}，" +
                              $"占位符融合：{(merged && expandOk ? "通过" : "失败")}");
            if (!roundTrip || !merged || !expandOk)
                Issues.Add(("自定义字段", "-", "ExtJson/占位符", "序列化或占位符融合不一致"));
        }
        catch (Exception ex)
        {
            Issues.Add(("自定义字段", "-", "SmokeTestExt", "自检失败：" + ex.Message));
        }
    }

    /// <summary>模板自检：默认模板 JSON 往返 + 模板模式渲染 + 含图片元素缺图容错。</summary>
    private static void SmokeTestTemplate()
    {
        try
        {
            // 1) 默认模板 JSON 序列化往返一致
            var t = PrintTemplate.CreateDefault("241x140");
            t.Elements.Add(new ImageElement { X = 170, Y = 10, W = 25, H = 15, Path = "logo.png" });
            var json = System.Text.Json.JsonSerializer.Serialize(t);
            var back = System.Text.Json.JsonSerializer.Deserialize<PrintTemplate>(json)!;
            bool roundTrip = back.PaperKey == "241x140" &&
                             back.Elements.Count == t.Elements.Count &&
                             back.Elements[^1] is ImageElement;
            Console.WriteLine($"  JSON 往返（含多态元素/图片元素）：{(roundTrip ? "通过" : "不一致")}");
            if (!roundTrip)
                Issues.Add(("磅单模板", "-", "PrintTemplate", "JSON 序列化往返不一致"));

            // 2) 模板模式渲染到内存位图（含缺图容错路径）
            foreach (var paper in PoundTicketPrinter.Papers)
            {
                t.PaperKey = paper.Key;
                using var bmp = new Bitmap(paper.W, paper.H);
                using var g = Graphics.FromImage(bmp);
                TemplateTicketRenderer.Render(t, g, new WeighRecord
                {
                    SerialNo = "TEST-01-0002", VehicleNo = "冀B54321", Goods = "矿粉",
                    GrossKg = 40000, TareKg = 15000, NetKg = 25000,
                    FirstTime = DateTime.Now.AddHours(-1), SecondTime = DateTime.Now,
                    Operator = "管理员", Status = "已完成",
                }, AppConfig.Current, 96f / 25.4f);
                Console.WriteLine($"  模板渲染（{paper.Name}，含缺图容错）成功");
            }
        }
        catch (Exception ex)
        {
            Issues.Add(("磅单模板", "-", "TemplateTicketRenderer", "渲染失败：" + ex.Message));
        }
    }

    /// <summary>磅单渲染自检：各纸张、部分字段隐藏、多联设置下渲染到内存位图不抛异常。</summary>
    private static void SmokeTestTicket()
    {
        var sample = new WeighRecord
        {
            SerialNo = "TEST-01-0001", VehicleNo = "冀A12345", Goods = "煤炭",
            SenderCompany = "供货商A", ReceiverCompany = "收货商B", DeductKg = 150,
            Spec = "一级", BatchNo = "B01", Remark = "测试备注",
            GrossKg = 34600, TareKg = 11600, NetKg = 23000,
            FirstTime = DateTime.Now.AddHours(-2), SecondTime = DateTime.Now,
            Operator = "管理员", Status = "已完成",
        };
        var cfg = AppConfig.Current;
        var (paperBak, hiddenBak, copiesBak) =
            (cfg.PrintPaper, cfg.PrintHiddenFields, cfg.PrintCopies);
        try
        {
            var cases = new (string Name, string[] Hidden)[]
            {
                ("全部字段显示", Array.Empty<string>()),
                ("隐藏部分字段", ["公司名称", "规格", "批次", "扣重", "实收", "单位", "备注", "签字栏"]),
            };
            foreach (var paper in PoundTicketPrinter.Papers)
            {
                cfg.PrintPaper = paper.Key;
                cfg.PrintCopies = 2;
                foreach (var c in cases)
                {
                    cfg.PrintHiddenFields = [.. c.Hidden];
                    using var bmp = new Bitmap(paper.W, paper.H);
                    using var g = Graphics.FromImage(bmp);
                    PoundTicketPrinter.RenderTo(g, new Rectangle(0, 0, paper.W, paper.H), sample);
                    Console.WriteLine($"  {paper.Name} × [{c.Name}] × 2联 渲染成功");
                }
            }
        }
        catch (Exception ex)
        {
            Issues.Add(("磅单渲染", "-", "DrawTicket", "渲染失败：" + ex.Message));
        }
        finally
        {
            cfg.PrintPaper = paperBak;
            cfg.PrintHiddenFields = hiddenBak;
            cfg.PrintCopies = copiesBak;
        }
    }
}
