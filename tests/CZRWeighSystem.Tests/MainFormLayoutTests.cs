using CZRWeighSystem.Database;
using CZRWeighSystem.UI;
using System.Reflection;

namespace CZRWeighSystem.Tests;

/// <summary>
/// 首页布局回归测试（2026-09-30 首页改版）：
/// 顶部为「空车去皮 / 重车过磅」手动选择按钮 + 大屏实时重量显示；
/// 中部固定 3 个摄像头画面槽位（最多 3 路）；右侧为今日称重统计面板；
/// 底部为称重记录列表；各区域不得互相遮挡，原有菜单/状态栏不受影响。
/// 注意：控件数据必须在窗体释放前的 STA 线程内采集（释放后子控件集合会被清空）。
/// 与其他 MainForm 测试同集合串行执行：构造主界面会访问 SQLite 库，
/// 避免与 FirstWeighExpireTests 的删库重建并发冲突。
/// </summary>
[Collection("数据库顺序")]
public class MainFormLayoutTests
{
    private sealed record LayoutSnapshot(
        string TareButtonText, string GrossButtonText,
        Rectangle VideoBounds, int VideoTileCount,
        Rectangle StatsBounds, Rectangle CaptureBounds,
        int CaptureColumnCount, string FirstColumnHeader, string LastColumnHeader);

    private static object Fld(MainForm form, string name) =>
        typeof(MainForm).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(form)!;

    private static LayoutSnapshot BuildAndMeasure()
    {
        Db.Initialize();   // 幂等建表（主界面构造会读取自定义字段与记录）
        LayoutSnapshot snap = null!;
        var thread = new Thread(() =>
        {
            using var form = new MainForm();
            var video = (GroupBox)Fld(form, "_videoPanel");
            var stats = (GroupBox)Fld(form, "_statsPanel");
            var capture = (GroupBox)Fld(form, "_capturePanel");
            var grid = (DataGridView)capture.Controls[0]!;
            // 视频区第一个子控件为 TableLayoutPanel（3 槽位）；未启用视频时为提示 Label
            int tileCount = video.Controls[0] is TableLayoutPanel table ? table.Controls.Count : 1;
            snap = new LayoutSnapshot(
                ((Button)Fld(form, "_btnTare")).Text,
                ((Button)Fld(form, "_btnGross")).Text,
                video.Bounds, tileCount, stats.Bounds, capture.Bounds,
                grid.Columns.Count,
                grid.Columns[0].HeaderText, grid.Columns[grid.Columns.Count - 1].HeaderText);
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        return snap;
    }

    [Fact]
    public void 顶部_空车去皮与重车过磅手动按钮文字正确()
    {
        var s = BuildAndMeasure();
        Assert.Equal("空车去皮", s.TareButtonText);
        Assert.Equal("重车过磅", s.GrossButtonText);
    }

    [Fact]
    public void 中部视频区_固定三个画面槽位_最多三路()
    {
        // 测试环境未配置摄像头源 → 3 个槽位均显示未配置占位，但槽位数量固定为 3
        Assert.Equal(3, BuildAndMeasure().VideoTileCount);
    }

    [Fact]
    public void 中部视频区_与右侧统计面板不重叠()
    {
        var s = BuildAndMeasure();
        Assert.True(s.StatsBounds.Left >= s.VideoBounds.Right,
            $"统计面板({s.StatsBounds.Left})与视频区(右边={s.VideoBounds.Right})重叠");
        Assert.True(s.StatsBounds.Top < s.VideoBounds.Top, "统计面板应位于右上角（顶部高于视频区）");
    }

    [Fact]
    public void 底部记录列表_位于视频区下方且不重叠()
    {
        var s = BuildAndMeasure();
        Assert.True(s.VideoBounds.Bottom <= s.CaptureBounds.Top,
            $"视频区(底边={s.VideoBounds.Bottom})与记录列表(顶边={s.CaptureBounds.Top})重叠");
        Assert.True(s.CaptureBounds.Height >= 120, $"记录列表高度异常：{s.CaptureBounds.Height}");
    }

    [Fact]
    public void 底部记录列表_十列完整保留()
    {
        var s = BuildAndMeasure();
        Assert.Equal(10, s.CaptureColumnCount);
        Assert.Equal("流水号", s.FirstColumnHeader);
        Assert.Equal("状态", s.LastColumnHeader);
    }
}
