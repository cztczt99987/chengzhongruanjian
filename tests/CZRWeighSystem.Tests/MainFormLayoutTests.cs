using CZRWeighSystem.Core;
using CZRWeighSystem.Database;
using CZRWeighSystem.UI;
using System.Reflection;

namespace CZRWeighSystem.Tests;

/// <summary>
/// 首页布局回归测试（2026-09-30 四次调整）：
/// 1. 软件启动默认全屏（最大化）；
/// 2. 顶部称重条（空车去皮｜大屏重量｜重车过磅）宽窗口下整组居中，窄窗口时向左避让右上角统计面板；
/// 3. 今日称重统计面板悬浮于窗口右上角，点击可竖直收起（仅剩标题条）/展开，箭头 ▲/▼ 切换；
/// 4. 底部称重记录列表条数选择框：20/50/70/100 可选，默认 20 条。
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
        Rectangle StatsBounds,
        Rectangle CaptureBounds, int CaptureColumnCount,
        string FirstColumnHeader, string LastColumnHeader,
        string PageSizeDefaultText, int GridRowCount,
        int TopBarCenterDelta, bool TopBarAvoidsStats);

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

    private static LayoutSnapshot Snapshot(MainForm form)
    {
        var video = (GroupBox)Fld(form, "_videoPanel");
        var stats = (GroupBox)Fld(form, "_statsPanel");
        var capture = (GroupBox)Fld(form, "_capturePanel");
        var grid = (DataGridView)capture.Controls[0]!;
        var pageSize = (ComboBox)Fld(form, "_cboPageSize");
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
            pageSize.Text, grid.Rows.Count,
            form.ClientSize.Width / 2 - groupCenter,
            lblComm.Right <= stats.Left - 14);
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
    public void 顶部称重条_宽窗口整组居中_窄窗口避让统计面板()
    {
        Db.Initialize();
        RunOnSta(() =>
        {
            using var form = new MainForm();
            form.WindowState = FormWindowState.Normal;

            // 宽窗口（空间充足）→ 整组横向居中
            form.Size = new Size(1920, 1000);
            var wide = Snapshot(form);
            Assert.True(Math.Abs(wide.TopBarCenterDelta) <= 2,
                $"宽窗口下顶部称重条未居中：偏差 {wide.TopBarCenterDelta}px");

            // 窄窗口（空间不足）→ 左移避让统计面板，不得遮挡
            form.Size = new Size(1380, 880);
            var narrow = Snapshot(form);
            Assert.True(narrow.TopBarAvoidsStats,
                $"窄窗口下顶部称重条右缘({narrow.TopBarCenterDelta})未避让统计面板");
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
    public void 统计面板_悬浮于窗口右上角()
    {
        var s = BuildAndMeasure();
        // 位于窗口顶部（画面区上方）、贴右缘，并向下延伸压在画面区右上角上
        Assert.True(s.StatsBounds.Top < s.VideoBounds.Top, "统计面板应位于画面区上方（窗口顶部）");
        Assert.True(s.StatsBounds.Right >= s.VideoBounds.Right - 2,
            $"统计面板({s.StatsBounds.Right})未贴齐右缘(视频区右缘={s.VideoBounds.Right})");
        Assert.True(s.StatsBounds.Bottom > s.VideoBounds.Top,
            $"统计面板底边({s.StatsBounds.Bottom})应下探覆盖画面区顶部({s.VideoBounds.Top})");
    }

    [Fact]
    public void 统计面板_点击竖直收起展开()
    {
        Db.Initialize();
        RunOnSta(() =>
        {
            using var form = new MainForm();
            var stats = (GroupBox)Fld(form, "_statsPanel");
            var arrow = (Label)Fld(form, "_lblStatsArrow");
            int expandedHeight = stats.Height;
            int expandedWidth = stats.Width;

            form.ToggleStatsPanel();   // 收起：竖直伸缩，仅剩标题条
            Assert.True(stats.Height < 40, $"收起后面板应只剩标题条高度：{stats.Height}");
            Assert.Equal(expandedWidth, stats.Width);   // 宽度不变（竖直伸缩）
            Assert.Equal("▼", arrow.Text);

            form.ToggleStatsPanel();   // 展开
            Assert.Equal(expandedHeight, stats.Height);
            Assert.Equal("▲", arrow.Text);
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
    public void 条数选择框_提供20与50与70与100四档且默认20条()
    {
        Db.Initialize();   // 先确保表存在，再插入（并行测试可能刚重建过库）
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

        RunOnSta(() =>
        {
            using var form = new MainForm();
            var s = Snapshot(form);
            Assert.Equal("20", s.PageSizeDefaultText);
            Assert.Equal(20, s.GridRowCount);   // 默认只显示 20 条

            // 切换档位 → 列表条数跟随变化
            var pageSize = (ComboBox)Fld(form, "_cboPageSize");
            pageSize.SelectedIndex = 3;   // 100 条
            var s100 = Snapshot(form);
            Assert.Equal("100", s100.PageSizeDefaultText);
            Assert.True(s100.GridRowCount >= 25, $"切换 100 条后应显示更多记录：{s100.GridRowCount}");
        });
    }
}
