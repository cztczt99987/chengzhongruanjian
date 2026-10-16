using CZRWeighSystem.Database;
using CZRWeighSystem.UI;
using System.Reflection;

namespace CZRWeighSystem.Tests;

[Collection("数据库顺序")]
public class ZoomBoundsDiag
{
    private static object Fld(MainForm form, string name) =>
        typeof(MainForm).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(form)!;

    private static void RunOnSta(Action act)
    {
        Exception? error = null;
        var t = new Thread(() => { try { act(); } catch (Exception ex) { error = ex; } });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        if (error != null) throw error;
    }

    [Fact]
    public void Diag_ZoomRegion_vs_Capture()
    {
        Db.Initialize();
        RunOnSta(() =>
        {
            using var form = new MainForm();
            form.WindowState = FormWindowState.Normal;
            form.ClientSize = new Size(1900, 1000);   // 模拟最大化后的客户区
            form.PerformLayout();
            var zoomRegion = form.ZoomRegion();
            var capture = (GroupBox)Fld(form, "_capturePanel");
            var zoom = (GroupBox)Fld(form, "_videoZoomPanel");
            var menu = (MenuStrip)Fld(form, "_menu");
            var topPanel = (Panel)Fld(form, "_topPanel");

            // 模拟触发放大（直接设可见并按 ZoomRegion 定位，等价于 ShowCameraZoom 的定位逻辑）
            zoom.Bounds = zoomRegion;
            zoom.Visible = true;

            string report =
                $"ClientSize={form.ClientSize.Width}x{form.ClientSize.Height}\n" +
                $"menu.Height={menu.Height}  topPanel.Height={topPanel.Height}\n" +
                $"ZoomRegion=Y:{zoomRegion.Y} H:{zoomRegion.Height} Bottom:{zoomRegion.Bottom}\n" +
                $"Capture=Y:{capture.Top} H:{capture.Height} Bottom:{capture.Bottom}\n" +
                $"ZoomPanel(after set)=Y:{zoom.Top} H:{zoom.Height} Bottom:{zoom.Bottom} Visible:{zoom.Visible}\n" +
                $"Zoom covers capture? {zoomRegion.Bottom >= capture.Bottom}";
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "zoom_diag.txt"), report);
        });
    }
}
