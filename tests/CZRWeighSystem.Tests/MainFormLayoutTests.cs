using CZRWeighSystem.Core;
using CZRWeighSystem.Database;
using CZRWeighSystem.UI;
using System.Reflection;

namespace CZRWeighSystem.Tests;

/// <summary>
/// 首页布局回归测试（2026-09-30 五次调整）：
/// 1. 软件启动默认全屏（最大化）；
/// 2. 顶部白色工具栏：左快捷方式（记录查询/报表统计/视频监控/基础资料）｜
///    称重条（空车去皮｜大屏重量｜重车过磅）整组横向居中｜右快捷方式（计算器/退出），最小窗口宽度不重叠；
///    空/重车按钮带自卸车图标（空厢举升/满载货堆）；稳定/通讯指示横排于大屏重量下方并成组居中；
/// 3. 今日称重统计面板悬浮于摄像头 3 顶部（贴画面区右上角），点击可竖直收起（仅剩标题条）/展开；
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
        string TareButtonText, string GrossButtonText,
        bool HasVehicleInput, FormWindowState WindowState,
        Rectangle VideoBounds, int VideoTileCount, int VideoPictureTop,
        Rectangle StatsBounds,
        Rectangle CaptureBounds, int CaptureColumnCount,
        string FirstColumnHeader, string LastColumnHeader,
        string PageSizeDefaultText, int GridRowCount,
        int TopBarCenterDelta);

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
        var stats = (Panel)Fld(form, "_statsPanel");
        var capture = (GroupBox)Fld(form, "_capturePanel");
        var grid = (DataGridView)Fld(form, "_captureGrid");   // 不按 Controls 序号取：条数下拉框会被置前
        var pageSize = (ComboBox)Fld(form, "_cboPageSize");
        // 视频区第一个子控件为 TableLayoutPanel（3 槽位）；未启用视频时为提示 Label
        int tileCount = video.Controls[0] is TableLayoutPanel table ? table.Controls.Count : 1;
        var btnTare = (Button)Fld(form, "_btnTare");
        // 顶部整组 = 空车按钮｜大屏｜重车按钮（稳定/通讯已移到大屏下方，不再计入整组宽度）
        int groupCenter = (btnTare.Left + ((Button)Fld(form, "_btnGross")).Right) / 2;
        return new LayoutSnapshot(
            btnTare.Text,
            ((Button)Fld(form, "_btnGross")).Text,
            FldOrNull(form, "_txtVehicle") != null,
            form.WindowState,
            video.Bounds, tileCount, video.Top + video.DisplayRectangle.Top + 2, stats.Bounds, capture.Bounds,
            grid.Columns.Count,
            grid.Columns[0].HeaderText, grid.Columns[grid.Columns.Count - 1].HeaderText,
            pageSize.Text, grid.Rows.Count,
            form.ClientSize.Width / 2 - groupCenter);
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
    public void 顶部_空车去皮与重车过磅按钮带自卸车图标且无彩色背景()
    {
        Db.Initialize();
        RunOnSta(() =>
        {
            using var form = new MainForm();
            var tare = (Button)Fld(form, "_btnTare");
            var gross = (Button)Fld(form, "_btnGross");

            Assert.NotNull(tare.Image);   // 空厢举升图标
            Assert.NotNull(gross.Image);  // 满载货堆图标
            // 用户提供图片：横向自卸车；两图等宽且共用画布 → 缩放后尺寸一致（按钮内文字对齐）
            Assert.True(tare.Image!.Width > tare.Image.Height, "空车图标应为横向自卸车图");
            Assert.Equal(tare.Image.Size, gross.Image!.Size);
            // 用户要求去掉红/绿背景色 → 白底扁平按钮
            Assert.Equal(Color.White, tare.BackColor);
            Assert.Equal(Color.White, gross.BackColor);
        });
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
            form.WindowState = FormWindowState.Normal;

            // 默认尺寸下应居中
            var d1 = Snapshot(form).TopBarCenterDelta;
            Assert.True(Math.Abs(d1) <= 2, $"默认尺寸下顶部称重条未居中：偏差 {d1}px");

            // 模拟窗口变化（Size 变更触发 Resize → 重新居中）
            form.Size = new Size(1920, 1000);
            var d2 = Snapshot(form).TopBarCenterDelta;
            Assert.True(Math.Abs(d2) <= 2, $"窗口变化后顶部称重条未居中：偏差 {d2}px");
        });
    }

    [Fact]
    public void 顶部_稳定与通讯指示位于大屏重量下方_不再占用右侧空间()
    {
        Db.Initialize();
        RunOnSta(() =>
        {
            using var form = new MainForm();
            var weights = (Label)Fld(form, "_lblWeight");
            var stable = (Label)Fld(form, "_lblStable");
            var comm = (Label)Fld(form, "_lblComm");
            var video = (GroupBox)Fld(form, "_videoPanel");

            // 移到大屏下方：两指示顶边均在大屏重量底边之下
            Assert.True(stable.Top >= weights.Bottom,
                $"稳定指示(顶={stable.Top})应在大屏重量(底={weights.Bottom})下方");
            Assert.True(comm.Top >= weights.Bottom,
                $"通讯指示(顶={comm.Top})应在大屏重量(底={weights.Bottom})下方");
            // 成组居中于大屏，且完全落在横向范围内（不再占用大屏右侧空间）
            int rowCenter = (stable.Left + comm.Right) / 2;
            int weightCenter = weights.Left + weights.Width / 2;
            Assert.True(Math.Abs(weightCenter - rowCenter) <= 2,
                $"稳定/通讯未居中于大屏：偏差 {weightCenter - rowCenter}px");
            Assert.True(stable.Left >= weights.Left && comm.Right <= weights.Right,
                $"稳定/通讯({stable.Left}~{comm.Right})超出大屏范围({weights.Left}~{weights.Right})");
            // 指示行完整落在视频区上方，紧凑衔接不与画面区重叠（指示位于白色工具栏面板内，坐标需换算）
            var topPanel = (Panel)Fld(form, "_topPanel");
            Assert.True(topPanel.Top + stable.Bottom <= video.Top && topPanel.Top + comm.Bottom <= video.Top,
                $"稳定/通讯指示与视频区(顶={video.Top})重叠");
        });
    }

    [Fact]
    public void 顶部快捷方式_六个按钮分列称重区两侧_均带图标且无换班()
    {
        Db.Initialize();
        RunOnSta(() =>
        {
            using var form = new MainForm();
            var panel = (Panel)Fld(form, "_topPanel");
            var btns = (System.Collections.IList)Fld(form, "_shortcutButtons");
            var tare = (Button)Fld(form, "_btnTare");
            var gross = (Button)Fld(form, "_btnGross");

            // 顺序：左 4（记录查询/报表统计/视频监控/基础资料）+ 右 2（计算器/退出）；明确不含"换班"
            var expected = new[] { "记录查询", "报表统计", "视频监控", "基础资料", "计算器", "退出" };
            Assert.Equal(expected.Length, btns.Count);
            for (int i = 0; i < expected.Length; i++)
            {
                var b = (Button)btns[i]!;
                Assert.Equal(expected[i], b.Text);
                Assert.Same(panel, b.Parent);
                Assert.NotNull(b.Image);
            }
            Assert.DoesNotContain(btns.Cast<Button>(), b => b.Text.Contains("换班"));

            // 左 4 个位于空车按钮左侧；右 2 个位于重车按钮右侧
            for (int i = 0; i < 4; i++)
                Assert.True(((Button)btns[i]!).Right <= tare.Left, $"左侧快捷按钮 {((Button)btns[i]!).Text} 与空车按钮重叠");
            for (int i = 4; i < 6; i++)
                Assert.True(((Button)btns[i]!).Left >= gross.Right, $"右侧快捷按钮 {((Button)btns[i]!).Text} 与重车按钮重叠");
        });
    }

    [Fact]
    public void 顶部工具栏_最小窗口宽度下三组不重叠且不超界()
    {
        Db.Initialize();
        RunOnSta(() =>
        {
            using var form = new MainForm();
            form.WindowState = FormWindowState.Normal;
            // MinimumSize 为 1240 外框，客户区约 1224
            form.ClientSize = new Size(1224, 700);
            Application.DoEvents();

            var panel = (Panel)Fld(form, "_topPanel");
            var btns = (System.Collections.IList)Fld(form, "_shortcutButtons");
            var tare = (Button)Fld(form, "_btnTare");
            var gross = (Button)Fld(form, "_btnGross");

            Assert.True(((Button)btns[3]!).Right <= tare.Left, "最小宽度下左快捷组与空车按钮重叠");
            Assert.True(gross.Right <= ((Button)btns[4]!).Left, "最小宽度下重车按钮与右快捷组重叠");
            foreach (Button b in btns)
                Assert.True(b.Right <= panel.ClientSize.Width, $"快捷按钮 {b.Text} 右缘超出工具栏");
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
    public void 统计面板_悬浮于摄像头3顶部()
    {
        var s = BuildAndMeasure();
        // 顶边与摄像头画面齐平（画面槽位顶边，而非压在画面区标题栏上）、贴画面区右缘，整体落在第 3 个槽位范围内
        Assert.Equal(s.VideoPictureTop, s.StatsBounds.Top);
        Assert.True(s.StatsBounds.Bottom <= s.VideoBounds.Bottom,
            $"统计面板(底边={s.StatsBounds.Bottom})应完整落在画面区内(底边={s.VideoBounds.Bottom})");
        Assert.True(s.StatsBounds.Right <= s.VideoBounds.Right + 2,
            $"统计面板右缘({s.StatsBounds.Right})超出画面区右缘({s.VideoBounds.Right})");
        int tile3Left = s.VideoBounds.Left + s.VideoBounds.Width * 2 / 3;
        Assert.True(s.StatsBounds.Left >= tile3Left - 2,
            $"统计面板左缘({s.StatsBounds.Left})未落在摄像头 3 槽位内(槽位左缘={tile3Left})");
    }

    [Fact]
    public void 统计面板_点击竖直收起展开()
    {
        Db.Initialize();
        RunOnSta(() =>
        {
            using var form = new MainForm();
            var stats = (Panel)Fld(form, "_statsPanel");
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
    public void 底部记录列表_十六列与弹窗字段一致_含金额()
    {
        var s = BuildAndMeasure();
        // 字段与「空车去皮/重车过磅」录入弹窗一致：流水号/日期/车牌/品种/地主姓名/电话/
        // 司机电话/货主姓名/毛/皮/净/金额/毛重时间/皮重时间/司磅员/状态
        Assert.Equal(16, s.CaptureColumnCount);
        Assert.Equal("流水号", s.FirstColumnHeader);
        Assert.Equal("状态", s.LastColumnHeader);   // 金额等列插在皮重时间与状态之间
    }

    [Fact]
    public void 单位单选框_快速切换kg与吨_配置立即生效并刷新列表()
    {
        Db.Initialize();
        var repo = new WeighRecordRepository();
        repo.InsertFirstWeigh(new WeighRecord
        { ScaleNo = "92", VehicleNo = "单位001", FirstTime = DateTime.Now, Status = "未完成", GrossKg = 1000 });

        RunOnSta(() =>
        {
            using var form = new MainForm();
            var rdoKg = (RadioButton)Fld(form, "_rdoKg");
            var rdoTon = (RadioButton)Fld(form, "_rdoTon");
            var grid = (DataGridView)Fld(form, "_captureGrid");
            var cfg = AppConfig.Current;
            string old = cfg.Unit;
            try
            {
                rdoKg.Checked = true;   // 确保从 kg 开始（同值不触发切换）
                Assert.Equal("kg", cfg.Unit);
                // 单位只在表头显示，单元格为不带单位的纯数字
                Assert.Equal("毛重(kg)", grid.Columns["Gross"]!.HeaderText);
                Assert.Equal("1000", (string)grid.Rows[0].Cells["Gross"].Value!);

                // 切吨：配置立即写回，列表按新单位显示（1000kg → 1），表头变"毛重(吨)"
                rdoTon.Checked = true;
                Assert.Equal("t", cfg.Unit);
                Assert.True(rdoKg.Checked == false, "kg/吨 单选应互斥");
                Assert.Equal("毛重(吨)", grid.Columns["Gross"]!.HeaderText);
                Assert.Equal("1", (string)grid.Rows[0].Cells["Gross"].Value!);

                rdoKg.Checked = true;   // 切回 kg
                Assert.Equal("kg", cfg.Unit);
            }
            finally
            {
                // 强制还原默认单位 kg 并落盘：测试环境固定 kg，避免单位残留影响其他硬编码 "kg" 的测试
                cfg.Unit = "kg";
                cfg.Save();
            }
        });
    }

    [Fact]
    public void 条数选择框_完整显示在表格上方_不被表格遮挡()
    {
        Db.Initialize();
        RunOnSta(() =>
        {
            using var form = new MainForm();
            var panel = (GroupBox)Fld(form, "_capturePanel");
            var grid = (DataGridView)Fld(form, "_captureGrid");
            var combo = (ComboBox)Fld(form, "_cboPageSize");
            var label = panel.Controls.OfType<Label>().Single(l => l.Text == "显示条数：");

            // 历史问题：表格先加入处于 z 序前层，与下拉框纵向重叠 → 界面上看不到下拉框
            Assert.True(combo.Bottom <= grid.Top,
                $"条数下拉框(底边={combo.Bottom})与表格(顶边={grid.Top})重叠，会被表格压住");
            Assert.True(label.Bottom <= grid.Top,
                $"条数标签(底边={label.Bottom})与表格(顶边={grid.Top})重叠，会被表格压住");
            // 完整落在面板可视区内，且在下拉框右缘之内不超界
            Assert.True(combo.Right <= panel.ClientSize.Width,
                $"条数下拉框右缘({combo.Right})超出面板可视宽度({panel.ClientSize.Width})");
            Assert.True(combo.Left >= label.Right, "条数标签应位于下拉框左侧且不重叠");
            Assert.True(combo.DropDownWidth >= combo.Width, "下拉列表宽度不应小于下拉框本体宽度");
        });
    }

    [Fact]
    public void 底部记录列表_表头与内容单行完整展示_窄窗出横向滚动()
    {
        Db.Initialize();
        var repo = new WeighRecordRepository();
        repo.InsertFirstWeigh(new WeighRecord
        {
            ScaleNo = "93", VehicleNo = "黑A12345", FirstTime = DateTime.Now, Status = "未完成",
            ExtJson = WeighRecord.ExtToJson(new Dictionary<string, string>
            {
                ["地主姓名"] = "张三三",
                ["地主电话"] = "13800001234",
            }),
        });

        RunOnSta(() =>
        {
            using var form = new MainForm();
            var grid = (DataGridView)Fld(form, "_captureGrid");

            // 每列宽度必须容纳表头文字（单行完整展示，不换行不截断），且表头禁止换行
            foreach (DataGridViewColumn col in grid.Columns)
            {
                int headerW = System.Windows.Forms.TextRenderer.MeasureText(col.HeaderText, grid.Font).Width + 8;
                Assert.True(col.Width >= headerW,
                    $"列[{col.HeaderText}]宽 {col.Width} 小于表头文字宽 {headerW}，表头会被截断");
                Assert.NotEqual(DataGridViewTriState.True, col.HeaderCell.Style.WrapMode);
            }

            // 窄窗口：列总宽超出表格可视宽度 → 出现横向滚动条，字段不隐藏
            form.WindowState = FormWindowState.Normal;
            form.ClientSize = new Size(900, 700);
            Application.DoEvents();
            int totalW = grid.Columns.Cast<DataGridViewColumn>().Sum(c => c.Width);
            Assert.True(totalW > grid.ClientSize.Width,
                $"列总宽({totalW})应超出窄表格可视宽度({grid.ClientSize.Width})以触发横向滚动条");
            Assert.True((grid.ScrollBars & ScrollBars.Horizontal) != 0,
                "表格应启用横向滚动条");
        });
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
