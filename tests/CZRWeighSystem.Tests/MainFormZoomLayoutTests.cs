using CZRWeighSystem.Database;
using CZRWeighSystem.UI;
using System.Reflection;

namespace CZRWeighSystem.Tests;

/// <summary>
/// 放大视图与记录列表布局回归：
/// 1. 放大画面必须完整覆盖底部称重记录列表；
/// 2. 记录列表底边必须恰好贴合状态栏顶边（不超出客户区、也不留大片空白）；
/// 3. 以上在不同分辨率/窗体尺寸下均成立（此前 capturePanel 锚定 Bottom，
///    窗体缩放时按旧底边余量撑高/压低，导致小屏留空、大屏超出）。
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

    /// <summary>多种窗体尺寸（含接近最小值与常见分辨率）下列表底边与状态栏贴合、放大区覆盖列表。</summary>
    [Theory]
    [InlineData(1240, 780)]     // 最小窗口
    [InlineData(1366, 768)]     // 常见笔记本
    [InlineData(1600, 900)]
    [InlineData(1920, 1080)]    // 全高清
    [InlineData(2560, 1440)]    // 2K
    public void 多分辨率_记录列表底边贴合状态栏_放大区完整覆盖(int w, int h)
    {
        Db.Initialize();
        RunOnSta(() =>
        {
            using var form = new MainForm();
            form.ClientSize = new Size(w, h);
            form.PerformLayout();

            var capture = (GroupBox)Fld(form, "_capturePanel");
            var status = (StatusStrip)Fld(form, "_status");
            var zoomRegion = form.ZoomRegion();

            int statusTop = form.ClientSize.Height - status.Height;
            // 列表底边必须落在状态栏顶边附近（贴合，不超出也不大片留空）
            Assert.True(Math.Abs(capture.Bottom - statusTop) <= 2,
                $"[{w}x{h}] 列表底边({capture.Bottom})应贴合状态栏顶边({statusTop})");
            // 放大区底边必须 ≥ 列表底边（放大画面能完整盖住列表）
            Assert.True(zoomRegion.Bottom >= capture.Bottom - 1,
                $"[{w}x{h}] 放大区底边({zoomRegion.Bottom})应不小于列表底边({capture.Bottom})");
        });
    }
}
