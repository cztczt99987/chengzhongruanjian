using CZRWeighSystem.Core;
using CZRWeighSystem.Database;
using CZRWeighSystem.UI;
using CZRWeighSystem.Utils;
using CZRWeighSystem.Video;
using System.Reflection;

namespace CZRWeighSystem.Tests;

/// <summary>
/// 首页布局回归测试（2026-09-30 五次调整）：
/// 1. 软件启动默认全屏（最大化）；
/// 2. 顶部白色工具栏：左快捷方式（记录查询/报表统计/视频监控/基础资料）｜
///    称重条（空车去皮｜大屏重量｜重车过磅）整组横向居中｜右快捷方式（计算器/退出），最小窗口宽度不重叠；
///    空/重车按钮带自卸车图标（空厢举升/满载货堆）；稳定/通讯指示位于重车过磅按钮右侧（工具栏垂直居中），顶部不再为其留一整行；
/// 3. 今日称重统计面板悬浮于摄像头 3 顶部（贴画面区右上角），点击可竖直收起（仅剩标题条）/展开；
/// 4. 底部称重记录列表条数选择框：20/50/70/100/今日全部 可选，默认 20 条；
/// 5. 中部视频区已加大（底部记录列表相应压低），画面槽位无边距、无黑底（等比填满）。
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
        // 顶部整组 = 空车按钮｜大屏｜重车按钮（稳定/通讯已移到重车右侧侧边，不计入整组宽度）
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
    public void 顶部_稳定与通讯指示移到重车过磅右侧_不再占大屏下方整行()
    {
        Db.Initialize();
        RunOnSta(() =>
        {
            using var form = new MainForm();
            var weights = (Label)Fld(form, "_lblWeight");
            var stable = (Label)Fld(form, "_lblStable");
            var comm = (Label)Fld(form, "_lblComm");
            var gross = (Button)Fld(form, "_btnGross");
            var video = (GroupBox)Fld(form, "_videoPanel");
            var topPanel = (Panel)Fld(form, "_topPanel");

            // 移到重车过磅按钮右侧：两指示左缘均在重车按钮右缘之后
            Assert.True(stable.Left >= gross.Right,
                $"稳定指示(左={stable.Left})应在重车过磅(右={gross.Right})右侧");
            Assert.True(comm.Left >= gross.Right,
                $"通讯指示(左={comm.Left})应在重车过磅(右={gross.Right})右侧");
            // 工具栏垂直居中（指示顶边不低于按钮区、底边不超出工具栏）
            Assert.True(stable.Top >= 0 && stable.Bottom <= topPanel.Height,
                $"稳定指示(顶={stable.Top} 底={stable.Bottom})应落在工具栏(高={topPanel.Height})内");
            // 不再占用大屏下方整行：指示顶边不在大屏重量底边之下
            Assert.True(stable.Top < weights.Bottom,
                $"稳定指示(顶={stable.Top})不应在大屏重量(底={weights.Bottom})下方（应放侧边）");
            // 与视频区不重叠（指示位于工具栏面板内，坐标需换算到窗体）
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
    public void 底部记录列表_十八列与弹窗字段一致_含单价与货主电话()
    {
        var s = BuildAndMeasure();
        // 字段与「空车去皮/重车过磅」录入弹窗一致：流水号/日期/车牌/品种/地主姓名/电话/
        // 司机电话/货主姓名/货主电话/单价/毛/皮/净/金额/毛重时间/皮重时间/司磅员/状态
        Assert.Equal(18, s.CaptureColumnCount);
        Assert.Equal("流水号", s.FirstColumnHeader);
        Assert.Equal("状态", s.LastColumnHeader);   // 单价/金额等列插在皮重时间与状态之间
    }

    /// <summary>
    /// 首页称重记录列表必须显示"单价"与"货主电话"列且取记录 ext_json 固定键
    /// （用户反馈：录入弹窗新增字段后，首页列表与记录查询都没有这些字段）。
    /// </summary>
    [Fact]
    public void 底部记录列表_显示单价与货主电话列_取自记录扩展字段()
    {
        Db.Initialize();
        var repo = new WeighRecordRepository();
        string vehicle = "单" + Guid.NewGuid().ToString("N")[..6];
        repo.InsertFirstWeigh(new WeighRecord
        {
            ScaleNo = "93", VehicleNo = vehicle, FirstTime = DateTime.Now, Status = "未完成",
            ExtJson = WeighRecord.ExtToJson(new Dictionary<string, string>
            {
                ["单价"] = "12.5", ["货主电话"] = "13700000000",
            }),
        });

        RunOnSta(() =>
        {
            using var form = new MainForm();
            var grid = (DataGridView)Fld(form, "_captureGrid");
            Assert.Equal("单价", grid.Columns["UnitPrice"]!.HeaderText);
            Assert.Equal("货主电话", grid.Columns["CargoOwnerPhone"]!.HeaderText);
            var row = grid.Rows.Cast<DataGridViewRow>()
                .First(r => (string)r.Cells["VehicleNo"].Value! == vehicle);
            Assert.Equal("12.5", (string)row.Cells["UnitPrice"].Value!);
            Assert.Equal("13700000000", (string)row.Cells["CargoOwnerPhone"].Value!);
        });
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

    /// <summary>
    /// 记录列表条带新增「刷新」按钮：位于称重记录面板内、随面板右上角锚定，
    /// 点击后按当前条数档位重新查询数据库刷新下方列表（列表被清空后点刷新可恢复）。
    /// </summary>
    [Fact]
    public void 记录列表_新增刷新按钮_点击重新加载列表()
    {
        Db.Initialize();
        var repo = new WeighRecordRepository();
        string vehicle = "刷" + Guid.NewGuid().ToString("N")[..6];
        repo.InsertFirstWeigh(new WeighRecord
        {
            ScaleNo = "95", VehicleNo = vehicle, FirstTime = DateTime.Now, Status = "未完成",
        });

        RunOnSta(() =>
        {
            using var form = new MainForm();
            var panel = (GroupBox)Fld(form, "_capturePanel");
            var grid = (DataGridView)Fld(form, "_captureGrid");
            var btn = (Button)Fld(form, "_btnRefresh");

            Assert.Same(panel, btn.Parent);                                  // 位于称重记录面板内
            Assert.Equal("刷 新", btn.Text);
            Assert.True(btn.Anchor.HasFlag(AnchorStyles.Top) && btn.Anchor.HasFlag(AnchorStyles.Right),
                "刷新按钮应随面板右上角锚定");
            Assert.True(btn.Bottom <= grid.Top,
                $"刷新按钮(底边={btn.Bottom})与表格(顶边={grid.Top})重叠，会被表格压住");

            static bool Has(DataGridView g, string v) => g.Rows.Cast<DataGridViewRow>()
                .Any(r => (string)r.Cells["VehicleNo"].Value! == v);
            Assert.True(Has(grid, vehicle), "构造后列表应已加载该记录");

            grid.Rows.Clear();                                               // 模拟列表被清空/数据已过期
            Assert.Empty(grid.Rows);
            // 窗体未显示时 PerformClick 不触发（CanSelect=false）→ 直接触发 Click 事件
            typeof(Control).GetMethod("OnClick", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(btn, [EventArgs.Empty]);
            Assert.True(Has(grid, vehicle), "点击刷新后应从数据库重新加载记录列表");
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

    /// <summary>直接改写记录的 first_time（测试非今日记录场景用）。</summary>
    private static void SetFirstTime(long id, DateTime time)
    {
        using var conn = Db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE t_weigh_record SET first_time=@t WHERE id=@id";
        cmd.Add("@t", time.ToString("yyyy-MM-dd HH:mm:ss"));
        cmd.Add("@id", id);
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// 新增"今日全部"档位：选中后显示今日完整记录集（不限条数），且不混入非今日记录。
    /// </summary>
    [Fact]
    public void 条数选择框_新增今日全部_选中显示今日全部记录()
    {
        Db.Initialize();
        var repo = new WeighRecordRepository();
        for (int i = 0; i < 25; i++)
        {
            repo.InsertFirstWeigh(new WeighRecord
            {
                ScaleNo = "93", VehicleNo = $"全{i:D3}{Guid.NewGuid().ToString("N")[..4]}",
            });
        }
        // 一条改到 3 天前：不应出现在"今日全部"（按天过滤）
        var oldVehicle = "旧" + Guid.NewGuid().ToString("N")[..6];
        var oldRec = repo.InsertFirstWeigh(new WeighRecord { ScaleNo = "93", VehicleNo = oldVehicle });
        SetFirstTime(oldRec.Id, DateTime.Today.AddDays(-3));

        // 期望值取自仓储的"今日"查询（构造窗体前后数据库不再变化）
        var expected = repo.Query(DateTime.Today, DateTime.Today, "", "", "", "").Count;

        RunOnSta(() =>
        {
            using var form = new MainForm();
            var combo = (ComboBox)Fld(form, "_cboPageSize");
            var grid = (DataGridView)Fld(form, "_captureGrid");

            Assert.Contains(MainForm.TodayAllPageSizeText, combo.Items.Cast<string>());

            combo.SelectedIndex = combo.Items.IndexOf(MainForm.TodayAllPageSizeText);
            Assert.Equal(expected, grid.Rows.Count);   // 今日全部 = 今日完整集合，不限条数
            Assert.True(grid.Rows.Count > 20, $"今日全部应突破 20 条上限：{grid.Rows.Count}");

            var vehicles = grid.Rows.Cast<DataGridViewRow>()
                .Select(r => (string)r.Cells["VehicleNo"].Value!).ToList();
            Assert.DoesNotContain(oldVehicle, vehicles);   // 3 天前的记录不混入今日
        });
    }

    /// <summary>首页大屏重量：用户要求去掉黑底与四周黑框（背景无色、无边框）。</summary>
    [Fact]
    public void 大屏重量_背景无色且无边框_不再黑底黑框()
    {
        Db.Initialize();
        RunOnSta(() =>
        {
            using var form = new MainForm();
            var lbl = (Label)Fld(form, "_lblWeight");
            Assert.Equal(Color.Transparent, lbl.BackColor);
            Assert.NotEqual(Color.Black, lbl.BackColor);
            Assert.Equal(BorderStyle.None, lbl.BorderStyle);   // 四周边框线也要去掉
        });
    }

    /// <summary>
    /// 中部视频区加大（画面更大），底部记录列表相应压低变矮；两者不重叠。
    /// </summary>
    [Fact]
    public void 中部视频区_已加大_底部记录列表变矮不重叠()
    {
        var s = BuildAndMeasure();
        Assert.True(s.VideoBounds.Height > 380, $"视频区应加大，当前高度 {s.VideoBounds.Height}");
        Assert.True(s.VideoBounds.Bottom <= s.CaptureBounds.Top,
            $"视频区(底={s.VideoBounds.Bottom})与记录列表(顶={s.CaptureBounds.Top})重叠");
        Assert.True(s.VideoBounds.Height > s.CaptureBounds.Height,
            $"视频区应高于被压低的记录列表：视频 {s.VideoBounds.Height} / 记录 {s.CaptureBounds.Height}");
    }

    /// <summary>画面槽位去黑框：无边距（不再有黑色间隔）且浅底（不再用黑底）。</summary>
    [Fact]
    public void 首页视频槽位_无黑框_浅底无边距()
    {
        Db.Initialize();
        RunOnSta(() =>
        {
            using var form = new MainForm();
            var video = (GroupBox)Fld(form, "_videoPanel");
            var table = Assert.IsType<TableLayoutPanel>(video.Controls[0]);
            Assert.Equal(MainForm.MaxVideoTiles, table.Controls.Count);
            foreach (Control tile in table.Controls)
            {
                Assert.Equal(new Padding(0), tile.Margin);
                Assert.NotEqual(Color.Black, tile.BackColor);
            }
        });
    }

    /// <summary>
    /// 画面等比缩放填满槽位（居中裁剪）：不留黑边、不拉伸变形；参数非法时返回空矩形。
    /// </summary>
    [Fact]
    public void 画面填满槽位_覆盖且保持比例_居中裁剪()
    {
        var r = MainForm.FitCover(1920, 1080, 400, 300);
        Assert.True(r.Width >= 400 && r.Height >= 300, $"填满槽位不得留边：{r}");
        Assert.True(Math.Abs(r.Width / (double)r.Height - 1920.0 / 1080.0) < 0.02,
            $"缩放应保持原比例不变形：{r.Width}x{r.Height}");
        Assert.Equal((400 - r.Width) / 2, r.X);   // 居中裁剪
        Assert.Equal((300 - r.Height) / 2, r.Y);
        Assert.Equal(Rectangle.Empty, MainForm.FitCover(0, 100, 400, 300));
    }

    /// <summary>
    /// 全屏窗口按聚焦索引只显示该路，索引为空/非法时回退显示全部。
    /// </summary>
    [Fact]
    public void 全屏窗口_按索引只显示该路_非法索引回退全部()
    {
        var all = new List<ICameraSource>
        {
            new SimulatedCameraSource("CAM1"),
            new SimulatedCameraSource("CAM2"),
            new SimulatedCameraSource("CAM3"),
        };
        try
        {
            Assert.Equal(3, VideoMonitorForm.SelectSources(all, null).Count);

            var one = VideoMonitorForm.SelectSources(all, 1);
            Assert.Single(one);
            Assert.Equal("CAM2", one[0].Name);

            Assert.Equal(3, VideoMonitorForm.SelectSources(all, 9).Count);    // 越界 → 全部
            Assert.Equal(3, VideoMonitorForm.SelectSources(all, -1).Count);
        }
        finally
        {
            foreach (var s in all) s.Dispose();
        }
    }

    /// <summary>测试注入摄像头源（不启动取流）：直接写入 CameraManager 的私有源列表。</summary>
    private static void InjectSources(MainForm form, params string[] names)
    {
        var cameras = (CameraManager)Fld(form, "_cameras");
        var list = (List<ICameraSource>)typeof(CameraManager)
            .GetField("_sources", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(cameras)!;
        foreach (var n in names) list.Add(new SimulatedCameraSource(n));
    }

    /// <summary>
    /// 首页单击某路画面 = 在首页内联放大（不弹窗）：放大面板铺满"视频监控区 + 今日汇总行 + 称重记录"
    /// 整块区域（用户截图红框），再次单击同一路还原；未配置的路（索引越界）不改动当前放大态。
    /// </summary>
    [Fact]
    public void 首页单击画面_内联放大_覆盖视频区与记录列表_再次点击还原()
    {
        Db.Initialize();
        RunOnSta(() =>
        {
            using var form = new MainForm();
            InjectSources(form, "CAM1", "CAM2", "CAM3");

            var video = (GroupBox)Fld(form, "_videoPanel");
            var capture = (GroupBox)Fld(form, "_capturePanel");
            var zoom = (GroupBox)Fld(form, "_videoZoomPanel");

            Assert.Equal(-1, (int)Fld(form, "_enlargedCameraIndex"));   // 初始：3 路平铺

            // 覆盖范围：与视频区同左缘同宽，顶边=视频区顶边，底边=记录列表底边（含今日汇总行）
            var region = form.ZoomRegion();
            Assert.Equal(video.Left, region.Left);
            Assert.Equal(video.Right, region.Right);
            Assert.Equal(video.Top, region.Top);
            Assert.Equal(capture.Bottom, region.Bottom);

            form.ToggleCameraZoom(1);                                   // 放大第 2 路
            Assert.Equal(1, (int)Fld(form, "_enlargedCameraIndex"));
            Assert.Equal(region, zoom.Bounds);                          // 铺满红框范围
            Assert.Contains("CAM2", zoom.Text);

            form.ToggleCameraZoom(1);                                   // 再次点击同一路 → 还原
            Assert.Equal(-1, (int)Fld(form, "_enlargedCameraIndex"));

            form.ToggleCameraZoom(1);                                   // 再放大
            Assert.Equal(1, (int)Fld(form, "_enlargedCameraIndex"));
            form.ToggleCameraZoom(9);                                   // 越界索引（未配置的路）：不改动
            Assert.Equal(1, (int)Fld(form, "_enlargedCameraIndex"));
            form.ToggleCameraZoom(-1);
            Assert.Equal(1, (int)Fld(form, "_enlargedCameraIndex"));
        });
    }

    /// <summary>
    /// 记录行右键菜单（对应参考版式）：三项齐全、各带图标，并挂在称重记录列表上。
    /// </summary>
    [Fact]
    public void 首页记录列表_右键菜单_提供当日记录打印预览过磅视图()
    {
        Db.Initialize();
        RunOnSta(() =>
        {
            using var form = new MainForm();
            var menu = (ContextMenuStrip)Fld(form, "_captureMenu");
            var grid = (DataGridView)Fld(form, "_captureGrid");

            Assert.Same(menu, grid.ContextMenuStrip);
            var items = menu.Items.Cast<ToolStripItem>().ToList();
            Assert.Equal(new[] { "当日记录", "打印预览", "过磅视图" }, items.Select(i => i.Text));
            Assert.All(items, i => Assert.NotNull(i.Image));   // 参考图中三项均带图标
        });
    }

    /// <summary>
    /// 右键「当日记录」：以该行日期打开记录查询页，日期范围锁定当天且清空其它条件，结果只含该日记录。
    /// </summary>
    [Fact]
    public void 首页右键当日记录_记录查询页只看该日记录()
    {
        Db.Initialize();
        var repo = new WeighRecordRepository();
        var day = DateTime.Today.AddDays(-7);   // 取非今天，便于区分"当天"与"近 30 天"默认范围
        string dayVehicle = "当" + Guid.NewGuid().ToString("N")[..6];
        string otherVehicle = "次" + Guid.NewGuid().ToString("N")[..6];
        var dayRec = repo.InsertFirstWeigh(new WeighRecord { ScaleNo = "94", VehicleNo = dayVehicle });
        SetFirstTime(dayRec.Id, day);
        var otherRec = repo.InsertFirstWeigh(new WeighRecord { ScaleNo = "94", VehicleNo = otherVehicle });
        SetFirstTime(otherRec.Id, day.AddDays(1));

        RunOnSta(() =>
        {
            using var f = new RecordQueryForm(day);
            object Fld(string name) => typeof(RecordQueryForm)
                .GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(f)!;

            Assert.Equal(day, ((DateTimePicker)Fld("_dtpFrom")).Value.Date);
            Assert.Equal(day, ((DateTimePicker)Fld("_dtpTo")).Value.Date);

            // 窗体未显示时不会触发 Load，手动执行查询以校验结果集
            typeof(RecordQueryForm)
                .GetMethod("DoQuery", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(f, null);
            var grid = (DataGridView)Fld("_grid");
            var vehicles = grid.Rows.Cast<DataGridViewRow>()
                .Select(r => (string)r.Cells["VehicleNo"].Value!).ToList();
            Assert.Contains(dayVehicle, vehicles);
            Assert.DoesNotContain(otherVehicle, vehicles);
        });
    }

    /// <summary>半屏放大画面表格中的画面块名称角标（黑屏块返回 null）。</summary>
    private static List<string?> ZoomTileNames(MainForm form)
    {
        var grid = (TableLayoutPanel)Fld(form, "_zoomGrid")!;
        return grid.Controls.OfType<PictureBox>().Select(p =>
            p.Controls.OfType<Label>().Any(l => l.Dock == DockStyle.Fill)   // 黑屏块的说明标签为 Fill 居中
                ? null
                : p.Controls.OfType<Label>().First().Text.Trim()).ToList();
    }

    /// <summary>取半屏放大画面第 tileIndex 块右键菜单中指定文字的项。</summary>
    private static ToolStripMenuItem ZoomMenuItem(MainForm form, int tileIndex, string text)
    {
        var grid = (TableLayoutPanel)Fld(form, "_zoomGrid")!;
        var pic = (PictureBox)grid.Controls.OfType<PictureBox>().ElementAt(tileIndex);
        return Assert.IsType<ToolStripMenuItem>(
            pic.ContextMenuStrip!.Items.Cast<ToolStripItem>().First(i => i.Text == text));
    }

    /// <summary>
    /// 首页放大为内联面板（不再全屏弹窗）：放大视图与视频区同锚定、默认隐藏；放大后画面右键菜单
    /// 四项（取消半屏/双路展示/全屏放大/手动拍照）；菜单入口的多路预览仍为普通带边框窗口。
    /// </summary>
    [Fact]
    public void 首页放大_为内联面板_右键菜单四项()
    {
        Db.Initialize();
        RunOnSta(() =>
        {
            using var form = new MainForm();
            InjectSources(form, "CAM1", "CAM2");
            var zoom = (GroupBox)Fld(form, "_videoZoomPanel");
            Assert.StartsWith("视频监控", zoom.Text);
            Assert.True(zoom.Anchor.HasFlag(AnchorStyles.Top | AnchorStyles.Bottom
                | AnchorStyles.Left | AnchorStyles.Right), "放大视图应随窗口伸缩");

            form.ToggleCameraZoom(0);
            var grid = (TableLayoutPanel)Fld(form, "_zoomGrid")!;
            var pic = Assert.Single(grid.Controls.OfType<PictureBox>());
            Assert.Equal(new[] { "取消半屏", "双路展示", "全屏放大", "手动拍照" },
                pic.ContextMenuStrip!.Items.Cast<ToolStripItem>().Select(i => i.Text));
            Assert.All(pic.ContextMenuStrip.Items.Cast<ToolStripItem>(), i => Assert.NotNull(i.Image));
        });

        using var multi = new VideoMonitorForm(new CameraManager());
        Assert.NotEqual(FormBorderStyle.None, multi.FormBorderStyle);      // 菜单多路预览：带边框普通窗口
        Assert.NotEqual(FormWindowState.Maximized, multi.WindowState);
        Assert.False(multi.KeyPreview);
    }

    /// <summary>
    /// 半屏放大右键「双路展示」：当前路 + 下一路左右并排（CAM1+CAM2），菜单项变「单路展示」，
    /// 再点切回单路（不用先进全屏——2026-10-14 用户要求）。
    /// </summary>
    [Fact]
    public void 半屏放大_双路展示_当前路与下一路并排_可切回()
    {
        Db.Initialize();
        RunOnSta(() =>
        {
            using var form = new MainForm();
            InjectSources(form, "CAM1", "CAM2", "CAM3");

            form.ToggleCameraZoom(0);
            Assert.False(form.IsZoomDual);
            Assert.Equal(new[] { "CAM1" }, ZoomTileNames(form));

            ZoomMenuItem(form, 0, "双路展示").PerformClick();
            Assert.True(form.IsZoomDual);
            Assert.Equal(new[] { "CAM1", "CAM2" }, ZoomTileNames(form));
            Assert.Equal("单路展示", ZoomMenuItem(form, 0, "单路展示").Text);
            Assert.Equal("单路展示", ZoomMenuItem(form, 1, "单路展示").Text);

            ZoomMenuItem(form, 0, "单路展示").PerformClick();
            Assert.False(form.IsZoomDual);
            Assert.Equal(new[] { "CAM1" }, ZoomTileNames(form));
        });
    }

    /// <summary>
    /// 只配置一路摄像头时，半屏放大仍可「双路展示」：第二块黑屏占位并提示未配置，
    /// 黑屏块的「手动拍照」置灰；黑屏块也能右键「单路展示 / 取消半屏」。
    /// </summary>
    [Fact]
    public void 半屏放大_仅一路摄像头_双路展示第二块黑屏_拍照置灰()
    {
        Db.Initialize();
        RunOnSta(() =>
        {
            using var form = new MainForm();
            InjectSources(form, "CAM1");

            form.ToggleCameraZoom(0);
            ZoomMenuItem(form, 0, "双路展示").PerformClick();
            Assert.True(form.IsZoomDual);

            var names = ZoomTileNames(form);
            Assert.Equal(2, names.Count);
            Assert.Equal("CAM1", names[0]);
            Assert.Null(names[1]);   // 第二块黑屏（无名称角标）

            Assert.False(ZoomMenuItem(form, 1, "手动拍照").Enabled, "黑屏块的手动拍照应置灰");
            Assert.True(ZoomMenuItem(form, 1, "单路展示").Enabled);
            Assert.True(ZoomMenuItem(form, 1, "取消半屏").Enabled);

            ZoomMenuItem(form, 1, "单路展示").PerformClick();
            Assert.False(form.IsZoomDual);
        });
    }

    /// <summary>
    /// 右键「全屏放大」打开的单路全屏窗：无边框最大化、Esc/单击退出、画面右键菜单
    /// （退出全屏/双路展示/手动拍照），左上角有退出提示。
    /// </summary>
    [Fact]
    public void 全屏放大_无边框单路画面_右键菜单三项()
    {
        bool oldEnabled = AppConfig.Current.CameraEnabled;
        AppConfig.Current.CameraEnabled = true;   // 窗体据此决定显示画面还是"未启用"提示
        var cameras = new CameraManager();
        var src = new SimulatedCameraSource("CAM2");
        try
        {
            ((List<ICameraSource>)typeof(CameraManager)
                .GetField("_sources", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(cameras)!).Add(src);

            RunOnSta(() =>
            {
                using var fs = new VideoMonitorForm(cameras, 0, fullscreen: true);
                Assert.Equal(FormBorderStyle.None, fs.FormBorderStyle);
                Assert.Equal(FormWindowState.Maximized, fs.WindowState);
                Assert.True(fs.KeyPreview);
                Assert.Equal("视频监控 - CAM2", fs.Text);
                Assert.Contains(fs.Controls.OfType<Label>(), l => l.Text.Contains("退出全屏"));

                // 唯一画面槽位的右键菜单：退出全屏 / 双路展示 / 手动拍照（按类型找画面表：提示标签 BringToFront 后在最前）
                var table = Assert.Single(fs.Controls.OfType<TableLayoutPanel>());
                var pic = Assert.IsType<PictureBox>(Assert.Single(table.Controls));
                var menu = pic.ContextMenuStrip;
                Assert.NotNull(menu);
                Assert.Equal(new[] { "退出全屏", "双路展示", "手动拍照" },
                    menu!.Items.Cast<ToolStripItem>().Select(i => i.Text));
                Assert.All(menu.Items.Cast<ToolStripItem>(), i => Assert.NotNull(i.Image));
            });
        }
        finally
        {
            AppConfig.Current.CameraEnabled = oldEnabled;
            src.Dispose();
            cameras.Dispose();
        }
    }

    /// <summary>构造注入 N 路模拟摄像头的 CameraManager（不启动取流）。</summary>
    private static CameraManager BuildCameraManager(int count)
    {
        var cameras = new CameraManager();
        var list = (List<ICameraSource>)typeof(CameraManager)
            .GetField("_sources", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(cameras)!;
        for (int i = 1; i <= count; i++) list.Add(new SimulatedCameraSource($"CAM{i}"));
        return cameras;
    }

    /// <summary>全屏窗内读取当前各画面槽位的摄像头名称角标（黑屏占位块返回 null，顺序即槽位顺序）。</summary>
    private static List<string?> TileNames(VideoMonitorForm fs)
    {
        var table = Assert.Single(fs.Controls.OfType<TableLayoutPanel>());
        return table.Controls.OfType<PictureBox>().Select(p =>
            p.Controls.OfType<Label>().Any(l => l.Dock == DockStyle.Fill)   // 黑屏块说明标签为 Fill 居中
                ? null
                : p.Controls.OfType<Label>().First().Text.Trim()).ToList();
    }

    /// <summary>取第 tileIndex 个画面槽位右键菜单中指定文字的项。</summary>
    private static ToolStripMenuItem MenuItem(VideoMonitorForm fs, int tileIndex, string text)
    {
        var table = Assert.Single(fs.Controls.OfType<TableLayoutPanel>());
        var pic = (PictureBox)table.Controls.OfType<PictureBox>().ElementAt(tileIndex);
        return Assert.IsType<ToolStripMenuItem>(pic.ContextMenuStrip!.Items.Cast<ToolStripItem>().First(i => i.Text == text));
    }

    /// <summary>
    /// 全屏「双路展示」：当前路 + 下一路左右并排（CAM1+CAM2），菜单项变为「单路展示」；
    /// 再点切回单路 CAM1；双路下对第二块画面「手动拍照」只拍 CAM2（文件名 cam2）。
    /// </summary>
    [Fact]
    public void 全屏_双路展示_当前路与下一路并排_可切回_拍照只拍右键所在路()
    {
        bool oldEnabled = AppConfig.Current.CameraEnabled;
        AppConfig.Current.CameraEnabled = true;
        var cameras = BuildCameraManager(3);
        string manualDir = Path.Combine(AppContext.BaseDirectory, "manualCaptures");
        try
        {
            RunOnSta(() =>
            {
                using var fs = new VideoMonitorForm(cameras, 0, fullscreen: true);
                Assert.Equal(new string?[] { "CAM1" }, TileNames(fs));
                Assert.False(fs.IsDual);

                MenuItem(fs, 0, "双路展示").PerformClick();   // 切双路
                Assert.True(fs.IsDual);
                Assert.Equal(new string?[] { "CAM1", "CAM2" }, TileNames(fs));
                Assert.Contains("双路", fs.Text);
                Assert.Equal("单路展示", MenuItem(fs, 0, "单路展示").Text);
                Assert.Equal("单路展示", MenuItem(fs, 1, "单路展示").Text);

                // 在第二块画面上手动拍照 → 只抓拍 CAM2（cam2 序号）
                MenuItem(fs, 1, "手动拍照").PerformClick();
                var files = Directory.GetFiles(Path.Combine(manualDir, DateTime.Now.ToString("yyyyMMdd")), "*_cam2.jpg");
                Assert.NotEmpty(files);

                MenuItem(fs, 0, "单路展示").PerformClick();   // 切回单路
                Assert.False(fs.IsDual);
                Assert.Equal(new string?[] { "CAM1" }, TileNames(fs));
            });
        }
        finally
        {
            AppConfig.Current.CameraEnabled = oldEnabled;
            cameras.Dispose();
            if (Directory.Exists(manualDir)) Directory.Delete(manualDir, recursive: true);
        }
    }

    /// <summary>最后一路双路展示时没有"下一路"：第二块黑屏占位（不回绕）：CAM3 + 黑屏。</summary>
    [Fact]
    public void 全屏_双路展示_最后一路第二块黑屏占位()
    {
        bool oldEnabled = AppConfig.Current.CameraEnabled;
        AppConfig.Current.CameraEnabled = true;
        var cameras = BuildCameraManager(3);
        try
        {
            RunOnSta(() =>
            {
                using var fs = new VideoMonitorForm(cameras, 2, fullscreen: true);
                MenuItem(fs, 0, "双路展示").PerformClick();
                Assert.Equal(new string?[] { "CAM3", null }, TileNames(fs));
                Assert.Contains("未配置", fs.Text);
                // 黑屏块的「手动拍照」置灰，但可切回单路 / 退出全屏
                Assert.False(MenuItem(fs, 1, "手动拍照").Enabled);
                Assert.True(MenuItem(fs, 1, "单路展示").Enabled);
                Assert.True(MenuItem(fs, 1, "退出全屏").Enabled);
            });
        }
        finally
        {
            AppConfig.Current.CameraEnabled = oldEnabled;
            cameras.Dispose();
        }
    }

    /// <summary>只配置一路摄像头时，「双路展示」仍可点击：第二块黑屏占位（用户要求不足两路也能双路）。</summary>
    [Fact]
    public void 全屏_仅一路摄像头_双路展示可点_第二块黑屏()
    {
        bool oldEnabled = AppConfig.Current.CameraEnabled;
        AppConfig.Current.CameraEnabled = true;
        var cameras = BuildCameraManager(1);
        try
        {
            RunOnSta(() =>
            {
                using var fs = new VideoMonitorForm(cameras, 0, fullscreen: true);
                var item = MenuItem(fs, 0, "双路展示");
                Assert.True(item.Enabled, "不足两路也允许双路展示（第二块黑屏）");
                item.PerformClick();
                Assert.True(fs.IsDual);
                Assert.Equal(new string?[] { "CAM1", null }, TileNames(fs));
            });
        }
        finally
        {
            AppConfig.Current.CameraEnabled = oldEnabled;
            cameras.Dispose();
        }
    }

    /// <summary>半屏双路状态下进全屏：构造传入 dual=true，全屏窗打开即双路（含黑屏占位）。</summary>
    [Fact]
    public void 全屏放大_携带半屏双路状态_打开即双路()
    {
        bool oldEnabled = AppConfig.Current.CameraEnabled;
        AppConfig.Current.CameraEnabled = true;
        var cameras = BuildCameraManager(3);
        try
        {
            RunOnSta(() =>
            {
                using var fs = new VideoMonitorForm(cameras, 1, fullscreen: true, dual: true);
                Assert.True(fs.IsDual);
                Assert.Equal(new string?[] { "CAM2", "CAM3" }, TileNames(fs));
            });
        }
        finally
        {
            AppConfig.Current.CameraEnabled = oldEnabled;
            cameras.Dispose();
        }
    }

    /// <summary>
    /// 放大/全屏开启后的短时间内忽略退出点击：首页双击画面时第二击会落在刚铺开的大画面上，
    /// 若响应会"放大又立刻还原"（保护窗 400ms）。
    /// </summary>
    [Fact]
    public void 放大还原点击保护窗_开启后短时间内不响应()
    {
        var opened = new DateTime(2026, 10, 14, 12, 0, 0, DateTimeKind.Utc);
        Assert.False(MainForm.ShouldRestoreZoom(opened, opened.AddMilliseconds(50), MainForm.ZoomClickGuard));
        Assert.True(MainForm.ShouldRestoreZoom(opened, opened + MainForm.ZoomClickGuard, MainForm.ZoomClickGuard));
        Assert.True(MainForm.ShouldRestoreZoom(opened, opened.AddSeconds(2), MainForm.ZoomClickGuard));
        Assert.False(VideoMonitorForm.ShouldExitFullscreen(opened, opened.AddMilliseconds(50), VideoMonitorForm.ExitClickGuard));
        Assert.True(VideoMonitorForm.ShouldExitFullscreen(opened, opened + VideoMonitorForm.ExitClickGuard, VideoMonitorForm.ExitClickGuard));
    }
}
