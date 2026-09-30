using CZRWeighSystem.Core;
using CZRWeighSystem.Database;
using CZRWeighSystem.UI;
using System.Reflection;

namespace CZRWeighSystem.Tests;

/// <summary>
/// 首页布局回归测试（2026-09-30 三次调整）：
/// 1. 软件启动默认全屏（最大化）；
/// 2. 顶部称重条（空车去皮｜大屏重量｜重车过磅）整组横向居中，窗口缩放后仍居中；
/// 3. 今日称重统计面板悬浮于视频区右上角，点击可收起/展开（视频区通栏不被挤压）；
/// 4. 底部称重记录列表不再显示条数选择框（固定最近 100 条）。
/// 注意：控件数据必须在窗体释放前的 STA 线程内采集（释放后子控件集合会被清空），
/// STA 线程内的断言异常必须捕获回抛，否则会以未处理异常拖垮测试宿主。
/// 与其他 MainForm 测试同集合串行执行：构造主界面会访问 SQLite 库，
/// 避免与 FirstWeighExpireTests 的删库重建并发冲突。
/// </summary>
[Collection("数据库顺序")]
public class MainFormLayoutTests
{
    private sealed record LayoutSnapshot(
        string TareButtonText, string GrossButtonText, bool HasVehicleInput,
        FormWindowState WindowState,
        Rectangle VideoBounds, int VideoTileCount,
        Rectangle StatsBounds, Rectangle CaptureBounds,
        int CaptureColumnCount, string FirstColumnHeader, string LastColumnHeader,
        bool HasPageSizeCombo, int TopBarCenterDelta);

    private static object? FldOrNull(MainForm form, string name) =>
        typeof(MainForm).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(form);

    private static object Fld(MainForm form, string name) => FldOrNull(form, name)!;

    /// <summary>在 STA 线程上执行测试体；线程内异常捕获后回抛（避免未处理异常拖垮测试宿主）。</summary>
    private static void RunOnSta(Action act)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { act(); }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error != null) throw error;
    }

    private static LayoutSnapshot BuildAndMeasure()
    {
        Db.Initialize();   // 幂等建表（主界面构造会读取记录）
        LayoutSnapshot snap = null!;
        RunOnSta(() =>
        {
            using var form = new MainForm();
            snap = Snapshot(form);
        });
        return snap;
    }

    private static LayoutSnapshot Snapshot(MainForm form)
    {
        var video = (GroupBox)Fld(form, "_videoPanel");
        var stats = (GroupBox)Fld(form, "_statsPanel");
        var capture = (GroupBox)Fld(form, "_capturePanel");
        var grid = (DataGridView)capture.Controls[0]!;
        // 视频区第一个子控件为 TableLayoutPanel（3 槽位）；未启用视频时为提示 Label
        int tileCount = video.Controls[0] is TableLayoutPanel table ? table.Controls.Count : 1;
        var btnTare = (Button)Fld(form, "_btnTare");
        var lblComm = (Label)Fld(form, "_lblComm");
        int groupCenter = (btnTare.Left + lblComm.Right) / 2;
        return new LayoutSnapshot(
            ((Button)Fld(form, "_btnTare")).Text,
            ((Button)Fld(form, "_btnGross")).Text,
            FldOrNull(form, "_txtVehicle") != null,
            form.WindowState,
            video.Bounds, tileCount, stats.Bounds, capture.Bounds,
            grid.Columns.Count,
            grid.Columns[0].HeaderText, grid.Columns[grid.Columns.Count - 1].HeaderText,
            FldOrNull(form, "_cboPageSize") != null,
            form.ClientSize.Width / 2 - groupCenter);
    }

    [Fact]
    public void 顶部_空车去皮与重车过磅手动按钮文字正确()
    {
        var s = BuildAndMeasure();
        Assert.Equal("空车去皮", s.TareButtonText);
        Assert.Equal("重车过磅", s.GrossButtonText);
    }

    [Fact]
    public void 软件启动_默认全屏最大化()
    {
        Assert.Equal(FormWindowState.Maximized, BuildAndMeasure().WindowState);
    }

    [Fact]
    public void 顶部称重条_整组横向居中_窗口变化后仍居中()
    {
        Db.Initialize();
        RunOnSta(() =>
        {
            using var form = new MainForm();
            // 默认尺寸下应居中
            Assert.True(Math.Abs(Snapshot(form).TopBarCenterDelta) <= 2,
                $"默认尺寸下顶部称重条未居中：偏差 {Snapshot(form).TopBarCenterDelta}px");

            // 模拟窗口变化（Size 变更触发 Resize → 重新居中）
            form.Size = new Size(1600, 900);
            var s = Snapshot(form);
            Assert.True(Math.Abs(s.TopBarCenterDelta) <= 2,
                $"窗口变化后顶部称重条未居中：偏差 {s.TopBarCenterDelta}px");
        });
    }

    [Fact]
    public void 首页_信息录入区已移除_改由弹窗承载()
    {
        Assert.False(BuildAndMeasure().HasVehicleInput, "首页不应再有车号输入框（已移入称重录入弹窗）");
    }

    [Fact]
    public void 中部视频区_固定三个画面槽位_最多三路()
    {
        // 测试环境未配置摄像头源 → 3 个槽位均显示未配置占位，但槽位数量固定为 3
        Assert.Equal(3, BuildAndMeasure().VideoTileCount);
    }

    [Fact]
    public void 统计面板_悬浮于视频区右上角()
    {
        var s = BuildAndMeasure();
        // 悬浮：面板整体落在视频区范围内（覆盖右上角），顶部与视频区顶对齐
        Assert.True(s.StatsBounds.Right <= s.VideoBounds.Right + 2,
            $"统计面板右缘({s.StatsBounds.Right})超出视频区右缘({s.VideoBounds.Right})");
        Assert.True(s.StatsBounds.Left >= s.VideoBounds.Left,
            $"统计面板左缘({s.StatsBounds.Left})不应超出视频区左缘({s.VideoBounds.Left})");
        Assert.Equal(s.VideoBounds.Top, s.StatsBounds.Top);
    }

    [Fact]
    public void 统计面板_点击可收起展开_视频区宽度不受影响()
    {
        Db.Initialize();
        RunOnSta(() =>
        {
            using var form = new MainForm();
            var stats = (GroupBox)Fld(form, "_statsPanel");
            var video = (GroupBox)Fld(form, "_videoPanel");
            int expandedWidth = stats.Width;
            int videoWidth = video.Width;

            form.ToggleStatsPanel();   // 收起
            Assert.True(stats.Width < 50, $"收起后面板宽度应变为窄条：{stats.Width}");
            Assert.Equal(videoWidth, video.Width);   // 视频区通栏，收起不影响其宽度

            form.ToggleStatsPanel();   // 展开
            Assert.Equal(expandedWidth, stats.Width);
        });
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

    [Fact]
    public void 底部记录列表_不再显示条数选择框()
    {
        Assert.False(BuildAndMeasure().HasPageSizeCombo, "列表条数选择框应已移除（固定最近 100 条）");
    }
}
