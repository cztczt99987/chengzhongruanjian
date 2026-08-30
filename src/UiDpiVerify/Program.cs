// UI DPI 缩放验证工具（临时工具，验证完成后可删除）
// 作用：真实实例化全部窗体，在 100% 与 125% 两块屏幕上创建句柄读取实际 DPI，
//       并用文字测量比对每个控件的文字宽度与控件宽度，输出溢出/越界报告；
//       150% 档位用线性推演补齐（AutoScaleMode.Dpi 下布局与文字同比例缩放）。
using System.Drawing;
using System.Runtime.InteropServices;
using CZRWeighSystem;
using CZRWeighSystem.Core;
using CZRWeighSystem.Database;
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
        new RecordEditDialog(new WeighRecord
        {
            SerialNo = "TEST-01-0001", VehicleNo = "A12345",
            GrossKg = 30000, TareKg = 10000, NetKg = 20000,
            Goods = "煤炭", Status = "已完成",
        }),
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
}
