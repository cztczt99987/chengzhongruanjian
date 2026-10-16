using CZRWeighSystem.Database;
using CZRWeighSystem.UI;
using System.Reflection;

namespace CZRWeighSystem.Tests;

/// <summary>
/// 放大视图布局回归：放大画面必须完整覆盖底部称重记录列表（此前 capturePanel 仅重设 Top、
/// 高度靠 Top|Bottom 锚定，窗体放大后列表底边会撑出客户区，导致放大画面盖不住列表）。
/// </summary>
[Collection("数据库顺序")]
public class MainFormZoomLayoutTests
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
    public void 放大视图_完整覆盖称重记录列表_不露出底部列表()
    {
        Db.Initialize();
        RunOnSta(() =>
        {
            using var form = new MainForm();
            form.ClientSize = new Size(1900, 1000);
            form.PerformLayout();

            var zoomRegion = form.ZoomRegion();
            var capture = (GroupBox)Fld(form, "_capturePanel");

            // 放大区域底边必须 ≥ 记录列表底边（放大画面能完整盖住列表）
            Assert.True(zoomRegion.Bottom >= capture.Bottom,
                $"放大区底边({zoomRegion.Bottom})应不小于记录列表底边({capture.Bottom})");
            // 记录列表底边不得超出客户区（状态栏之上）
            Assert.True(capture.Bottom <= form.ClientSize.Height,
                $"记录列表底边({capture.Bottom})不得超出客户区({form.ClientSize.Height})");
        });
    }
}
