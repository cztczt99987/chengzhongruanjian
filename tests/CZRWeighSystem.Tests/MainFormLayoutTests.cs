using CZRWeighSystem.Core;
using CZRWeighSystem.Database;
using CZRWeighSystem.UI;
using System.Reflection;

namespace CZRWeighSystem.Tests;

/// <summary>
/// 首页布局回归测试（2026-09-30 首页二次改版）：
/// 顶部为「空车去皮 / 重车过磅」手动选择按钮 + 大屏实时重量显示（信息录入区已移除，改由弹窗承载）；
/// 中部固定 3 个摄像头画面槽位（最多 3 路）；最右侧右上角为今日称重统计面板（点击可收起/展开，视频区宽度联动）；
/// 底部为称重记录列表（显示条数 20/50/70/100 可选）；各区域不得互相遮挡。
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
        Rectangle VideoBounds, int VideoTileCount,
        Rectangle StatsBounds, Rectangle CaptureBounds,
        int CaptureColumnCount, string FirstColumnHeader, string LastColumnHeader,
        int PageSizeItemCount, string PageSizeDefaultText);

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
            var video = (GroupBox)Fld(form, "_videoPanel");
            var stats = (GroupBox)Fld(form, "_statsPanel");
            var capture = (GroupBox)Fld(form, "_capturePanel");
            var grid = (DataGridView)capture.Controls[0]!;
            var pageSize = (ComboBox)Fld(form, "_cboPageSize");
            // 视频区第一个子控件为 TableLayoutPanel（3 槽位）；未启用视频时为提示 Label
            int tileCount = video.Controls[0] is TableLayoutPanel table ? table.Controls.Count : 1;
            snap = new LayoutSnapshot(
                ((Button)Fld(form, "_btnTare")).Text,
                ((Button)Fld(form, "_btnGross")).Text,
                FldOrNull(form, "_txtVehicle") != null,
                video.Bounds, tileCount, stats.Bounds, capture.Bounds,
                grid.Columns.Count,
                grid.Columns[0].HeaderText, grid.Columns[grid.Columns.Count - 1].HeaderText,
                pageSize.Items.Count, pageSize.Text);
        });
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
    public void 统计面板_位于视频区右侧右上角且顶对齐()
    {
        var s = BuildAndMeasure();
        Assert.True(s.StatsBounds.Left >= s.VideoBounds.Right,
            $"统计面板({s.StatsBounds.Left})与视频区(右边={s.VideoBounds.Right})重叠");
        Assert.Equal(s.VideoBounds.Top, s.StatsBounds.Top);
    }

    [Fact]
    public void 统计面板_点击可收起展开且视频区宽度联动()
    {
        Db.Initialize();
        RunOnSta(() =>
        {
            using var form = new MainForm();
            var stats = (GroupBox)Fld(form, "_statsPanel");
            var video = (GroupBox)Fld(form, "_videoPanel");
            int expandedWidth = stats.Width;
            int videoWidthExpanded = video.Width;

            form.ToggleStatsPanel();   // 收起
            Assert.True(stats.Width < 50, $"收起后面板宽度应变为窄条：{stats.Width}");
            Assert.True(video.Width > videoWidthExpanded, "收起统计面板后视频区应变宽");

            form.ToggleStatsPanel();   // 展开
            Assert.Equal(expandedWidth, stats.Width);
            Assert.Equal(videoWidthExpanded, video.Width);
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
    public void 显示条数_提供20与50与70与100四档且默认100()
    {
        var s = BuildAndMeasure();
        Assert.Equal(4, s.PageSizeItemCount);
        Assert.Equal("100", s.PageSizeDefaultText);
    }

    [Fact]
    public void 显示条数_切换为20时列表只显示20条()
    {
        var repo = new WeighRecordRepository();
        for (int i = 0; i < 25; i++)
        {
            repo.InsertFirstWeigh(new WeighRecord
            {
                ScaleNo = "92", VehicleNo = $"测页{i:D3}",
                FirstTime = DateTime.Now,
                Status = "未完成",
            });
        }

        Db.Initialize();
        RunOnSta(() =>
        {
            using var form = new MainForm();
            var capture = (GroupBox)Fld(form, "_capturePanel");
            var grid = (DataGridView)capture.Controls[0]!;
            var pageSize = (ComboBox)Fld(form, "_cboPageSize");

            pageSize.SelectedIndex = 0;   // 20 条 → 触发 SelectedIndexChanged 刷新
            Assert.Equal(20, grid.Rows.Count);
        });
    }
}
