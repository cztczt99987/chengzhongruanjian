using CZRWeighSystem.Comm;
using CZRWeighSystem.Core;
using CZRWeighSystem.Database;
using CZRWeighSystem.UI;
using System.Reflection;

namespace CZRWeighSystem.Tests;

/// <summary>
/// 称重录入弹窗回归测试（仿老软件链式流程：确认 → 保存 → 打印 → 继续）：
/// 四步按顺序逐步解锁，确认时校验车号/稳定/超载并锁定读数；
/// 保存时车号待配对则自动二次磅结算（毛/皮按大小分配，兼容先皮后毛与先毛后皮）；
/// 打印直接出单、不再弹"是否打印"，失败原地重试；继续后清空表单连续录入下一辆。
/// 对话框通过 internal InfoBox/PrintAction 注入静默实现，避免测试阻塞。
/// 与 FirstWeighExpireTests 同集合串行执行，避免共享 SQLite 库互相干扰。
/// </summary>
[Collection("数据库顺序")]
public class WeighEntryDialogTests
{
    private readonly WeighRecordRepository _repo = new();

    /// <summary>InfoBox 静默实现记录的最后一条提示（断言提示内容用）。</summary>
    private static string? _lastInfo;

    public WeighEntryDialogTests() => Db.Initialize();   // 幂等建表

    /// <summary>读取弹窗私有字段（控件）。</summary>
    private static T Fld<T>(WeighEntryDialog dlg, string name) =>
        (T)typeof(WeighEntryDialog).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(dlg)!;

    /// <summary>调用弹窗私有方法。</summary>
    private static void Call(WeighEntryDialog dlg, string name) =>
        typeof(WeighEntryDialog).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(dlg, null);

    /// <summary>在 STA 线程上执行测试体（WinForms 控件只能在 STA 线程访问）。</summary>
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

    /// <summary>注入静默对话框：提示记录到 _lastInfo，打印直接返回成功（不真实出纸）。</summary>
    private static void SilenceDialogs(WeighEntryDialog dlg)
    {
        dlg.InfoBox = (msg, title, ok) => _lastInfo = msg;
        dlg.PrintAction = _ => true;
        PrintingIndicator.Suppressed = true;   // 打印时提示小窗不弹（测试环境）
    }

    /// <summary>
    /// UI 保存用独立磅号：流水号按"日期+磅号+计数"生成且删除会使计数回落，
    /// 默认磅号 01 会与 FirstWeighExpireTests 插删的记录串号（UNIQUE 冲突）。
    /// </summary>
    private static void UseTestScaleNo() => AppConfig.Current.ScaleNo = "91";

    /// <summary>向管理器的稳定判定器推送 N 次相同采样，使 IsStable=true、StableKg=kg。</summary>
    private static void PushStable(WeighManager manager, double kg)
    {
        var filter = (StabilityFilter)typeof(WeighManager)
            .GetField("_filter", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(manager)!;
        for (int i = 0;i < 3; i++) filter.Push(kg);   // 默认 StableCount=3
    }

    /// <summary>每次运行唯一车号：共享库会残留上次运行的记录，断言 Single 时不能重名。</summary>
    private static string NewVehicle(string prefix) => $"测{prefix}{Guid.NewGuid():N}"[..7];

    /// <summary>清空省份简称（选中首项"无省份"）：测试车号不带前缀，ComposeVehicleNo 原样返回。</summary>
    private static void NoPrefix(WeighEntryDialog dlg) =>
        Fld<ProvincePicker>(dlg, "_cboPrefix").SelectedIndex = 0;

    /// <summary>执行"确认 → 保存"两步（保存成功后停在打印步）。</summary>
    private static void ConfirmAndSave(WeighEntryDialog dlg)
    {
        Call(dlg, "Confirm");
        Assert.Equal(1, Fld<int>(dlg, "_step"));   // 确认成功 → 待保存
        Call(dlg, "DoSave");
        Assert.Equal(2, Fld<int>(dlg, "_step"));   // 保存成功 → 待打印
        Assert.NotNull(Fld<WeighRecord>(dlg, "_savedRecord"));
    }

    /// <summary>
    /// 票据表格不得压住下方司磅员框：行7 加"单价"后表格底(480)与司磅员框顶曾只留 8px 间隙，
    /// 用户高 DPI 环境缩放取整后表格底边框线盖住框顶。现间隙 24px，并要求 ≥16px 回归保护。
    /// </summary>
    [Fact]
    public void 布局_表格不压住司磅员框()
    {
        WeighManager manager = new();
        RunOnSta(() =>
        {
            using var dlg = new WeighEntryDialog(manager, isTare: true);
            var grid = Fld<TableLayoutPanel>(dlg, "_grid");
            var op = Fld<TextBox>(dlg, "_txtOperator");
            Assert.True(op.Top - grid.Bottom >= 16,
                $"司磅员框顶 {op.Top} 与表格底 {grid.Bottom} 间隙应 ≥16px（过小会被高 DPI 缩放取整吃掉）");
        });
        manager.Dispose();
    }

    /// <summary>
    /// 票据表格 7 行必须等高且完整渲染：表格高 356 扣除单元格边框后若容不下 7×50=350，
    /// TableLayoutPanel 会压缩靠后的行（用户反馈"单价行下方显示不全"）。行高取自 TLP 实际布局。
    /// </summary>
    [Fact]
    public void 布局_表格七行等高完整渲染()
    {
        WeighManager manager = new();
        RunOnSta(() =>
        {
            using var dlg = new WeighEntryDialog(manager, isTare: true);
            var grid = Fld<TableLayoutPanel>(dlg, "_grid");
            dlg.Show();   // 触发布局，GetRowHeights 才有值
            var heights = grid.GetRowHeights();
            Assert.Equal(7, heights.Length);
            // 每行实际高度不得低于设计行高 50 的 98%（TLP 空间不足时会压缩行）
            for (int i = 0; i < heights.Length; i++)
                Assert.True(heights[i] >= 49,
                    $"表格第 {i + 1} 行实际高度 {heights[i]}px 低于设计 50px（表格高度不足，行被压缩）");
        });
        manager.Dispose();
    }

    [Fact]
    public void 空车去皮_无待配对记录_保存为第一次称重()
    {
        string vehicle = NewVehicle("K");
        bool onSaved = false;
        WeighManager manager = new();
        RunOnSta(() =>
        {
            using var dlg = new WeighEntryDialog(manager, isTare: true);
            SilenceDialogs(dlg);
            UseTestScaleNo();
            dlg.OnSaved = _ => onSaved = true;
            NoPrefix(dlg);
            Fld<TextBox>(dlg, "_txtVehicle").Text = vehicle;
            PushStable(manager, 10000);
            ConfirmAndSave(dlg);
        });
        manager.Dispose();

        Assert.True(onSaved, "保存成功后未触发 OnSaved 回调");
        var record = _repo.Query(null, null, vehicle, "", "", "").Single();
        Assert.Equal("未完成", record.Status);
        Assert.Equal(10000, record.GrossKg);   // 一次磅重量暂存 gross_kg
    }

    [Fact]
    public void 固定字段_保存写入品种与ExtJson固定键()
    {
        string vehicle = NewVehicle("F");
        WeighManager manager = new();
        RunOnSta(() =>
        {
            using var dlg = new WeighEntryDialog(manager, isTare: true);
            SilenceDialogs(dlg);
            UseTestScaleNo();
            NoPrefix(dlg);
            Fld<TextBox>(dlg, "_txtVehicle").Text = vehicle;
            Fld<TextBox>(dlg, "_txtVariety").Text = " 玉米 ";
            Fld<TextBox>(dlg, "_txtOwner").Text = "张三";
            Fld<TextBox>(dlg, "_txtOwnerPhone").Text = "13800000000";
            Fld<TextBox>(dlg, "_txtDriverPhone").Text = "13911111111";
            Fld<TextBox>(dlg, "_txtCargoOwner").Text = "李四";
            Fld<TextBox>(dlg, "_txtUnitPrice").Text = " 235.5 ";
            PushStable(manager, 10000);
            ConfirmAndSave(dlg);
        });
        manager.Dispose();

        var record = _repo.Query(null, null, vehicle, "", "", "").Single();
        Assert.Equal("玉米", record.Goods);              // 品种 → Goods 列
        var ext = record.ParseExt();                     // 人名/电话/单价 → ext_json 固定键
        Assert.Equal("张三", ext["地主姓名"]);
        Assert.Equal("13800000000", ext["地主电话"]);
        Assert.Equal("13911111111", ext["司机电话"]);
        Assert.Equal("李四", ext["货主姓名"]);
        Assert.Equal("235.5", ext["单价"]);              // 单价 → ext_json 固定键（保存时去除首尾空白）
    }

    [Fact]
    public void 重车过磅_车号有待配对记录_自动二次磅结算()
    {
        string vehicle = NewVehicle("L");
        _repo.InsertFirstWeigh(new WeighRecord { ScaleNo = "02", VehicleNo = vehicle, GrossKg = 30000 });
        WeighManager manager = new();
        RunOnSta(() =>
        {
            using var dlg = new WeighEntryDialog(manager, isTare: false);
            SilenceDialogs(dlg);
            NoPrefix(dlg);
            Fld<TextBox>(dlg, "_txtVehicle").Text = vehicle;
            PushStable(manager, 50000);
            ConfirmAndSave(dlg);
        });
        manager.Dispose();

        var record = _repo.Query(null, null, vehicle, "", "", "").Single();
        Assert.Equal("已完成", record.Status);
        Assert.Equal(50000, record.GrossKg);   // 毛重 = 大值
        Assert.Equal(30000, record.TareKg);    // 皮重 = 小值
        Assert.Equal(20000, record.NetKg);
    }

    [Fact]
    public void 空车去皮_先毛后皮_结算净重一致()
    {
        string vehicle = NewVehicle("M");
        _repo.InsertFirstWeigh(new WeighRecord { ScaleNo = "03", VehicleNo = vehicle, GrossKg = 50000 });
        WeighManager manager = new();
        RunOnSta(() =>
        {
            using var dlg = new WeighEntryDialog(manager, isTare: true);
            SilenceDialogs(dlg);
            NoPrefix(dlg);
            Fld<TextBox>(dlg, "_txtVehicle").Text = vehicle;
            PushStable(manager, 30000);
            ConfirmAndSave(dlg);
        });
        manager.Dispose();

        var record = _repo.Query(null, null, vehicle, "", "", "").Single();
        Assert.Equal("已完成", record.Status);
        Assert.Equal(50000, record.GrossKg);
        Assert.Equal(30000, record.TareKg);
        Assert.Equal(20000, record.NetKg);
    }

    /// <summary>
    /// 用户反馈：空车去皮"确认→保存"后车下磅归零，票面却显示"毛重=120、皮重=0、净重=120"——
    /// 实时值（归零）填进皮重行、暂存 gross 列的一次磅值填进对侧毛重行，造成误读。
    /// 修复后保存成功即固定展示已存结果：本次皮重进皮重行，毛重/净重为 "--"。
    /// </summary>
    [Fact]
    public void 空车去皮_保存后票面固定显示本次皮重_不跟随实时重量()
    {
        string vehicle = NewVehicle("G");
        WeighManager manager = new();
        RunOnSta(() =>
        {
            using var dlg = new WeighEntryDialog(manager, isTare: true);
            SilenceDialogs(dlg);
            UseTestScaleNo();
            NoPrefix(dlg);
            Fld<TextBox>(dlg, "_txtVehicle").Text = vehicle;
            PushStable(manager, 120);
            ConfirmAndSave(dlg);

            // 车辆下磅（实时/稳定归零）后刷新票面
            PushStable(manager, 0);
            Call(dlg, "UpdateWeight");

            Assert.Equal(AppConfig.Current.FormatWeight(120), Fld<Label>(dlg, "_lblTareCell").Text);
            Assert.Equal("--", Fld<Label>(dlg, "_lblGrossCell").Text);
            Assert.Equal("--", Fld<Label>(dlg, "_lblNetCell").Text);
            Assert.Contains("一次磅已保存", Fld<Label>(dlg, "_lblPairHint").Text);
            Assert.Contains(Fld<WeighRecord>(dlg, "_savedRecord").SerialNo,
                Fld<Label>(dlg, "_lblSerialNo").Text);
        });
        manager.Dispose();
    }

    /// <summary>
    /// 二次磅结算完成后票面固定展示结算结果（毛/皮/净为最终值）：
    /// 配对记录已变为"已完成"，若仍走实时预览会清空票面并误显"新车辆"提示。
    /// </summary>
    [Fact]
    public void 二次磅结算后_票面固定显示结算毛皮净()
    {
        string vehicle = NewVehicle("H");
        _repo.InsertFirstWeigh(new WeighRecord { ScaleNo = "93", VehicleNo = vehicle, GrossKg = 30000 });
        WeighManager manager = new();
        RunOnSta(() =>
        {
            using var dlg = new WeighEntryDialog(manager, isTare: false);
            SilenceDialogs(dlg);
            NoPrefix(dlg);
            Fld<TextBox>(dlg, "_txtVehicle").Text = vehicle;
            PushStable(manager, 50000);
            ConfirmAndSave(dlg);

            PushStable(manager, 0);
            Call(dlg, "UpdateWeight");

            Assert.Equal(AppConfig.Current.FormatWeight(50000), Fld<Label>(dlg, "_lblGrossCell").Text);
            Assert.Equal(AppConfig.Current.FormatWeight(30000), Fld<Label>(dlg, "_lblTareCell").Text);
            Assert.Equal(AppConfig.Current.FormatWeight(20000), Fld<Label>(dlg, "_lblNetCell").Text);
            Assert.Contains("已结算完成", Fld<Label>(dlg, "_lblPairHint").Text);
            Assert.Contains(Fld<WeighRecord>(dlg, "_savedRecord").SerialNo,
                Fld<Label>(dlg, "_lblSerialNo").Text);
        });
        manager.Dispose();
    }

    /// <summary>
    /// 用户反馈：确认锁定 0.2 吨后车辆下磅归零，票面皮重跟着变成 0。
    /// "确认"即锁定读数：票面应固定展示锁定值，直到保存（切换为已存结果）或切换/继续。
    /// </summary>
    [Fact]
    public void 确认锁定读数后_票面固定显示锁定值_不随归零变化()
    {
        string vehicle = NewVehicle("I");
        WeighManager manager = new();
        RunOnSta(() =>
        {
            using var dlg = new WeighEntryDialog(manager, isTare: true);
            SilenceDialogs(dlg);
            UseTestScaleNo();
            NoPrefix(dlg);
            Fld<TextBox>(dlg, "_txtVehicle").Text = vehicle;
            PushStable(manager, 200);   // 0.2 吨
            Call(dlg, "Confirm");
            Assert.Equal(1, Fld<int>(dlg, "_step"));

            // 车下磅归零后刷新票面：应保持确认时的锁定值
            PushStable(manager, 0);
            Call(dlg, "UpdateWeight");

            Assert.Equal(AppConfig.Current.FormatWeight(200), Fld<Label>(dlg, "_lblTareCell").Text);
            Assert.Equal("--", Fld<Label>(dlg, "_lblGrossCell").Text);
            Assert.Equal("--", Fld<Label>(dlg, "_lblNetCell").Text);
        });
        manager.Dispose();
    }

    /// <summary>
    /// 确认后车辆下磅归零再点保存：仍应按确认时锁定的读数入库
    /// （旧实现保存时取实时稳定重量，归零后被"当前重量为 0"拦截，无法保存）。
    /// </summary>
    [Fact]
    public void 空车去皮_确认后归零再保存_按锁定值入库()
    {
        string vehicle = NewVehicle("J");
        WeighManager manager = new();
        RunOnSta(() =>
        {
            using var dlg = new WeighEntryDialog(manager, isTare: true);
            SilenceDialogs(dlg);
            UseTestScaleNo();
            NoPrefix(dlg);
            Fld<TextBox>(dlg, "_txtVehicle").Text = vehicle;
            PushStable(manager, 200);
            Call(dlg, "Confirm");
            PushStable(manager, 0);   // 车下磅归零
            Call(dlg, "DoSave");

            Assert.Equal(2, Fld<int>(dlg, "_step"));
        });
        manager.Dispose();

        var record = _repo.Query(null, null, vehicle, "", "", "").Single();
        Assert.Equal("未完成", record.Status);
        Assert.Equal(200, record.GrossKg);   // 入库为确认锁定的 0.2 吨，而非归零后的 0
    }

    /// <summary>二次磅同样按确认锁定值结算：确认后归零再保存仍得出正确毛/皮/净。</summary>
    [Fact]
    public void 二次磅_确认后归零再保存_按锁定值结算()
    {
        string vehicle = NewVehicle("K");
        _repo.InsertFirstWeigh(new WeighRecord { ScaleNo = "94", VehicleNo = vehicle, GrossKg = 30000 });
        WeighManager manager = new();
        RunOnSta(() =>
        {
            using var dlg = new WeighEntryDialog(manager, isTare: false);
            SilenceDialogs(dlg);
            NoPrefix(dlg);
            Fld<TextBox>(dlg, "_txtVehicle").Text = vehicle;
            PushStable(manager, 50000);
            Call(dlg, "Confirm");
            PushStable(manager, 0);   // 车下磅归零
            Call(dlg, "DoSave");

            Assert.Equal(2, Fld<int>(dlg, "_step"));
        });
        manager.Dispose();

        var record = _repo.Query(null, null, vehicle, "", "", "").Single();
        Assert.Equal("已完成", record.Status);
        Assert.Equal(50000, record.GrossKg);
        Assert.Equal(30000, record.TareKg);
        Assert.Equal(20000, record.NetKg);
    }

    [Fact]
    public void 未录车号时确认_提示请选择车号且不保存()
    {
        _lastInfo = null;
        WeighManager manager = new();
        RunOnSta(() =>
        {
            using var dlg = new WeighEntryDialog(manager, isTare: true);
            SilenceDialogs(dlg);
            NoPrefix(dlg);
            Fld<TextBox>(dlg, "_txtVehicle").Text = "";   // 清空"默认字母"预填，模拟未录车号
            PushStable(manager, 10000);
            Call(dlg, "Confirm");

            Assert.Equal(0, Fld<int>(dlg, "_step"));       // 校验失败，停在确认步
        });
        manager.Dispose();

        Assert.Contains("请选择车号", _lastInfo);
    }

    [Fact]
    public void 重量未稳定时确认_提示未稳定且不保存()
    {
        string vehicle = NewVehicle("N");
        _lastInfo = null;
        WeighManager manager = new();
        RunOnSta(() =>
        {
            using var dlg = new WeighEntryDialog(manager, isTare: false);
            SilenceDialogs(dlg);
            NoPrefix(dlg);
            Fld<TextBox>(dlg, "_txtVehicle").Text = vehicle;
            Call(dlg, "Confirm");   // 未推送稳定采样 → IsStableForSave=false

            Assert.Equal(0, Fld<int>(dlg, "_step"));
        });
        manager.Dispose();

        Assert.Contains("未稳定", _lastInfo);
        Assert.Empty(_repo.Query(null, null, vehicle, "", "", ""));
    }

    [Fact]
    public void 录入待配对车号_显示配对提示且链式按钮初始仅确认可用()
    {
        string vehicle = NewVehicle("O");
        _repo.InsertFirstWeigh(new WeighRecord { ScaleNo = "04", VehicleNo = vehicle, GrossKg = 10000 });
        WeighManager manager = new();
        RunOnSta(() =>
        {
            using var dlg = new WeighEntryDialog(manager, isTare: false);
            // 车号变化触发 TextChanged → UpdateWeight 刷新配对信息
            NoPrefix(dlg);
            Fld<TextBox>(dlg, "_txtVehicle").Text = vehicle;

            Assert.Contains("未完成的二次磅", Fld<Label>(dlg, "_lblPairHint").Text);
            // 重车过磅模式不显示"上次重量/上次时间"区块（该功能仅空车去皮弹窗使用）
            Assert.False(dlg.LastTareInfoVisible);
            Assert.False(Fld<Label>(dlg, "_lblLastWeight").Visible);
            Assert.False(Fld<Label>(dlg, "_lblLastTime").Visible);

            // 链式按钮初始：确认=当前白，保存/打印/继续=未到达灰
            var btnLocked = (Color)typeof(WeighEntryDialog)
                .GetField("BtnLocked", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
            var btnCurrent = (Color)typeof(WeighEntryDialog)
                .GetField("BtnCurrent", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
            Assert.Equal(btnCurrent, Fld<Button>(dlg, "_btnConfirm").BackColor);
            Assert.Equal(btnLocked, Fld<Button>(dlg, "_btnSave").BackColor);
            Assert.Equal(btnLocked, Fld<Button>(dlg, "_btnPrint").BackColor);
            Assert.Equal(btnLocked, Fld<Button>(dlg, "_btnContinue").BackColor);
        });
        manager.Dispose();
    }

    [Fact]
    public void 弹窗标题按皮毛模式区分()
    {
        WeighManager manager = new();
        RunOnSta(() =>
        {
            using var tare = new WeighEntryDialog(manager, isTare: true);
            using var gross = new WeighEntryDialog(manager, isTare: false);
            Assert.StartsWith("空车去皮", tare.Text);
            Assert.StartsWith("重车过磅", gross.Text);
        });
        manager.Dispose();
    }

    [Fact]
    public void 切换按钮_皮毛模式互换并刷新标题与右列()
    {
        var cfg = CZRWeighSystem.AppConfig.Current;
        string oldHeader = cfg.SlipHeader;
        try
        {
            cfg.SlipHeader = "过磅单";
            WeighManager manager = new();
            RunOnSta(() =>
            {
                using var dlg = new WeighEntryDialog(manager, isTare: true);

                Call(dlg, "ToggleMode");
                Assert.StartsWith("重车过磅", dlg.Text);
                Assert.Contains("毛重过磅单", Fld<Label>(dlg, "_lblTicketType").Text);
                Assert.Equal("本次毛重", Fld<Label>(dlg, "_lblPanelTitle").Text);

                Call(dlg, "ToggleMode");
                Assert.StartsWith("空车去皮", dlg.Text);
                Assert.Contains("皮重过磅单", Fld<Label>(dlg, "_lblTicketType").Text);
                Assert.Equal("本次皮重", Fld<Label>(dlg, "_lblPanelTitle").Text);
            });
            manager.Dispose();
        }
        finally
        {
            cfg.SlipHeader = oldHeader;
        }
    }

    [Fact]
    public void 当天保存过的车牌_输入完整后自动回填上次信息并提供建议()
    {
        string plate = "黑T" + Guid.NewGuid().ToString("N")[..4];   // 黑T + 4 位编号，唯一
        _repo.InsertFirstWeigh(new WeighRecord
        {
            ScaleNo = "85",
            VehicleNo = plate,
            Goods = "玉米",
            ExtJson = WeighRecord.ExtToJson(new Dictionary<string, string>
            {
                ["地主姓名"] = "张三",
                ["地主电话"] = "13800000000",
                ["货主姓名"] = "李四",
                ["单价"] = "235.5",
            }),
        });

        WeighManager manager = new();
        RunOnSta(() =>
        {
            using var dlg = new WeighEntryDialog(manager, isTare: true);
            var cbo = Fld<ProvincePicker>(dlg, "_cboPrefix");
            cbo.SelectedIndex = ProvincePicker.Items.IndexOf("黑");   // 前缀与已存车牌一致

            // 编号输入一半时：建议列表应包含该编号（去掉省份部分）
            Fld<TextBox>(dlg, "_txtVehicle").Text = plate[1..3];    // 输入前 2 位
            var src = Fld<TextBox>(dlg, "_txtVehicle").AutoCompleteCustomSource;
            Assert.Contains(plate[1..], src.Cast<string>());

            // 输入完整编号 → 组合车号命中当天记录 → 自动回填上次填写的信息
            Fld<TextBox>(dlg, "_txtVehicle").Text = plate[1..];
            Assert.Equal("玉米", Fld<TextBox>(dlg, "_txtVariety").Text);
            Assert.Equal("张三", Fld<TextBox>(dlg, "_txtOwner").Text);
            Assert.Equal("13800000000", Fld<TextBox>(dlg, "_txtOwnerPhone").Text);
            Assert.Equal("李四", Fld<TextBox>(dlg, "_txtCargoOwner").Text);
            Assert.Equal("235.5", Fld<TextBox>(dlg, "_txtUnitPrice").Text);   // 单价一并回填
        });
        manager.Dispose();
    }

    [Fact]
    public void 自动回填后_换无历史车号_自动字段全部清空()
    {
        string plate = "黑T" + Guid.NewGuid().ToString("N")[..4];
        _repo.InsertFirstWeigh(new WeighRecord
        {
            ScaleNo = "86",
            VehicleNo = plate,
            Goods = "玉米",
            ExtJson = WeighRecord.ExtToJson(new Dictionary<string, string>
            {
                ["地主姓名"] = "张三",
                ["地主电话"] = "13800000000",
                ["司机电话"] = "13911111111",
                ["货主姓名"] = "李四",
                ["单价"] = "235.5",
            }),
        });

        WeighManager manager = new();
        RunOnSta(() =>
        {
            using var dlg = new WeighEntryDialog(manager, isTare: true);
            var cbo = Fld<ProvincePicker>(dlg, "_cboPrefix");
            cbo.SelectedIndex = ProvincePicker.Items.IndexOf("黑");

            // 输入完整历史车号 → 回填
            Fld<TextBox>(dlg, "_txtVehicle").Text = plate[1..];
            Assert.Equal("玉米", Fld<TextBox>(dlg, "_txtVariety").Text);

            // 换成当天无记录的车号 → 上一辆车自动填入的信息必须全部清空（用户反馈的残留问题）
            Fld<TextBox>(dlg, "_txtVehicle").Text = "X" + Guid.NewGuid().ToString("N")[..5];
            Assert.Equal("", Fld<TextBox>(dlg, "_txtVariety").Text);
            Assert.Equal("", Fld<TextBox>(dlg, "_txtOwner").Text);
            Assert.Equal("", Fld<TextBox>(dlg, "_txtOwnerPhone").Text);
            Assert.Equal("", Fld<TextBox>(dlg, "_txtDriverPhone").Text);
            Assert.Equal("", Fld<TextBox>(dlg, "_txtCargoOwner").Text);
            Assert.Equal("", Fld<TextBox>(dlg, "_txtUnitPrice").Text);
        });
        manager.Dispose();
    }

    [Fact]
    public void 无历史车号下手工填写_再换车号_手填内容保留()
    {
        WeighManager manager = new();
        RunOnSta(() =>
        {
            using var dlg = new WeighEntryDialog(manager, isTare: true);
            SilenceDialogs(dlg);
            NoPrefix(dlg);
            Fld<TextBox>(dlg, "_txtVehicle").Text = NewVehicle("V1");   // 新车号，无回填
            Fld<TextBox>(dlg, "_txtVariety").Text = "小麦";              // 用户手工填写
            Fld<TextBox>(dlg, "_txtOwner").Text = "王五";

            // 再换成另一个无历史车号：手填内容不应被清空（不是上一辆车的自动信息）
            Fld<TextBox>(dlg, "_txtVehicle").Text = NewVehicle("V2");
            Assert.Equal("小麦", Fld<TextBox>(dlg, "_txtVariety").Text);
            Assert.Equal("王五", Fld<TextBox>(dlg, "_txtOwner").Text);
        });
        manager.Dispose();
    }

    [Fact]
    public void 自动回填后手动改过的字段_换车号时保留_其余自动字段清空()
    {
        string plate = "黑T" + Guid.NewGuid().ToString("N")[..4];
        _repo.InsertFirstWeigh(new WeighRecord
        {
            ScaleNo = "87",
            VehicleNo = plate,
            Goods = "玉米",
            ExtJson = WeighRecord.ExtToJson(new Dictionary<string, string>
            {
                ["地主姓名"] = "张三",
                ["货主姓名"] = "李四",
            }),
        });

        WeighManager manager = new();
        RunOnSta(() =>
        {
            using var dlg = new WeighEntryDialog(manager, isTare: true);
            var cbo = Fld<ProvincePicker>(dlg, "_cboPrefix");
            cbo.SelectedIndex = ProvincePicker.Items.IndexOf("黑");
            Fld<TextBox>(dlg, "_txtVehicle").Text = plate[1..];
            Assert.Equal("张三", Fld<TextBox>(dlg, "_txtOwner").Text);

            // 用户把自动回填的地主姓名手工改成另一个人
            Fld<TextBox>(dlg, "_txtOwner").Text = "赵六";

            // 换成无历史车号：手改的地主姓名保留，其他纯自动字段清空
            Fld<TextBox>(dlg, "_txtVehicle").Text = "Y" + Guid.NewGuid().ToString("N")[..5];
            Assert.Equal("赵六", Fld<TextBox>(dlg, "_txtOwner").Text);
            Assert.Equal("", Fld<TextBox>(dlg, "_txtVariety").Text);
            Assert.Equal("", Fld<TextBox>(dlg, "_txtCargoOwner").Text);
        });
        manager.Dispose();
    }

    [Fact]
    public void 默认字母_编号框预填配置字母_带预填车号或关闭开关时不预填()
    {
        // 临时改配置并在结束时还原，避免污染其他用例
        var cfg = AppConfig.Current;
        var (useBak, letterBak) = (cfg.UseProvinceLetter, cfg.DefaultLetter);
        try
        {
            cfg.UseProvinceLetter = true;
            cfg.DefaultLetter = "D";

            WeighManager manager = new();
            RunOnSta(() =>
            {
                // 开关开启：编号框预填 D，光标在字母后供续输
                using var dlg = new WeighEntryDialog(manager, isTare: true);
                var txt = Fld<TextBox>(dlg, "_txtVehicle");
                Assert.Equal("D", txt.Text);
                Assert.Equal(txt.TextLength, txt.SelectionStart);

                // 带预填车号进入：预填优先，不再叠字母
                using var dlg2 = new WeighEntryDialog(manager, isTare: false, prefillVehicle: "冀A12345");
                Assert.Equal("A12345", Fld<TextBox>(dlg2, "_txtVehicle").Text);

                // 开关关闭：编号框留空
                cfg.UseProvinceLetter = false;
                using var dlg3 = new WeighEntryDialog(manager, isTare: true);
                Assert.Equal("", Fld<TextBox>(dlg3, "_txtVehicle").Text);
            });
            manager.Dispose();
        }
        finally
        {
            (cfg.UseProvinceLetter, cfg.DefaultLetter) = (useBak, letterBak);
        }
    }

    [Fact]
    public void 切换按钮_背景色随皮毛模式变化()
    {
        int tareBack = ((Color)typeof(WeighEntryDialog)
            .GetField("TareBack", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!).ToArgb();
        int grossBack = ((Color)typeof(WeighEntryDialog)
            .GetField("GrossBack", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!).ToArgb();

        WeighManager manager = new();
        RunOnSta(() =>
        {
            using var dlg = new WeighEntryDialog(manager, isTare: true);
            Assert.Equal(tareBack, dlg.BackColor.ToArgb());          // 皮重=蓝

            Call(dlg, "ToggleMode");
            Assert.Equal(grossBack, dlg.BackColor.ToArgb());         // 毛重=绿

            Call(dlg, "ToggleMode");
            Assert.Equal(tareBack, dlg.BackColor.ToArgb());
        });
        manager.Dispose();
    }

    [Fact]
    public void 车牌省份前缀_默认取设置_组合与预填拆分正确()
    {
        WeighManager manager = new();
        RunOnSta(() =>
        {
            // 默认省份 = 综合设置 DefaultProvince（默认"黑"），下拉应选中它
            using var dlg = new WeighEntryDialog(manager, isTare: true);
            Assert.Equal(AppConfig.Current.DefaultProvince,
                Fld<ProvincePicker>(dlg, "_cboPrefix").SelectedItem);

            // 完整车号 = 省份 + 手输编号
            Fld<TextBox>(dlg, "_txtVehicle").Text = "A12345";
            Assert.Equal(AppConfig.Current.DefaultProvince + "A12345", dlg.ComposeVehicleNo());

            // 编号已含省份开头（手输整牌）时不重复拼接
            string full = AppConfig.Current.DefaultProvince + "B6789";
            Fld<TextBox>(dlg, "_txtVehicle").Text = full;
            Assert.Equal(full, dlg.ComposeVehicleNo());

            // 预填完整车牌：省份拆进下拉、编号进输入框
            using var dlg2 = new WeighEntryDialog(manager, isTare: false, prefillVehicle: "冀A12345");
            Assert.Equal("冀", Fld<ProvincePicker>(dlg2, "_cboPrefix").SelectedItem);
            Assert.Equal("A12345", Fld<TextBox>(dlg2, "_txtVehicle").Text);
            Assert.Equal("冀A12345", dlg2.ComposeVehicleNo());
        });
        manager.Dispose();
    }

    // ---------- 链式流程专项 ----------

    [Fact]
    public void 未到当前步_点击后续按钮均无效()
    {
        WeighManager manager = new();
        RunOnSta(() =>
        {
            using var dlg = new WeighEntryDialog(manager, isTare: true);
            SilenceDialogs(dlg);
            NoPrefix(dlg);

            // 初始阶段：保存/打印/继续都不是当前步，点击后阶段不变
            Call(dlg, "DoSave");
            Call(dlg, "DoPrint");
            Call(dlg, "DoContinue");
            Assert.Equal(0, Fld<int>(dlg, "_step"));
        });
        manager.Dispose();
    }

    [Fact]
    public void 完整链路_确认保存打印继续_表单清空且窗口不关闭()
    {
        string vehicle = NewVehicle("P");
        WeighManager manager = new();
        RunOnSta(() =>
        {
            using var dlg = new WeighEntryDialog(manager, isTare: false);
            SilenceDialogs(dlg);
            UseTestScaleNo();
            NoPrefix(dlg);
            Fld<TextBox>(dlg, "_txtVehicle").Text = vehicle;
            Fld<TextBox>(dlg, "_txtVariety").Text = "大豆";
            PushStable(manager, 40000);

            ConfirmAndSave(dlg);

            // 打印成功 → 待继续
            Call(dlg, "DoPrint");
            Assert.Equal(3, Fld<int>(dlg, "_step"));

            // 继续 → 表单清空、回到确认步（窗口仍开着，DialogResult 未设置）
            Call(dlg, "DoContinue");
            Assert.Equal(0, Fld<int>(dlg, "_step"));
            Assert.Equal("", Fld<TextBox>(dlg, "_txtVehicle").Text);
            Assert.Equal("", Fld<TextBox>(dlg, "_txtVariety").Text);
            Assert.Null(Fld<WeighRecord?>(dlg, "_savedRecord"));
            Assert.NotEqual(DialogResult.OK, dlg.DialogResult);
        });
        manager.Dispose();
    }

    [Fact]
    public void 打印失败_停在打印步可重试_成功后解锁继续()
    {
        string vehicle = NewVehicle("Q");
        WeighManager manager = new();
        RunOnSta(() =>
        {
            using var dlg = new WeighEntryDialog(manager, isTare: false);
            SilenceDialogs(dlg);
            UseTestScaleNo();
            NoPrefix(dlg);
            Fld<TextBox>(dlg, "_txtVehicle").Text = vehicle;
            PushStable(manager, 40000);
            ConfirmAndSave(dlg);

            // 打印失败 → 停留打印步
            dlg.PrintAction = _ => false;
            Call(dlg, "DoPrint");
            Assert.Equal(2, Fld<int>(dlg, "_step"));

            // 排除故障后重试 → 解锁继续
            dlg.PrintAction = _ => true;
            Call(dlg, "DoPrint");
            Assert.Equal(3, Fld<int>(dlg, "_step"));
        });
        manager.Dispose();
    }

    [Fact]
    public void 地主电话格式非法_确认被拦截()
    {
        string vehicle = NewVehicle("R");
        _lastInfo = null;
        WeighManager manager = new();
        RunOnSta(() =>
        {
            using var dlg = new WeighEntryDialog(manager, isTare: true);
            SilenceDialogs(dlg);
            UseTestScaleNo();
            NoPrefix(dlg);
            Fld<TextBox>(dlg, "_txtVehicle").Text = vehicle;
            Fld<TextBox>(dlg, "_txtOwnerPhone").Text = "12345";   // 非法手机号
            PushStable(manager, 10000);

            Call(dlg, "Confirm");
            Assert.Equal(0, Fld<int>(dlg, "_step"));
        });
        manager.Dispose();

        Assert.Contains("地主电话格式不正确", _lastInfo);
    }

    // ---------- 省份选择器（多列紧凑网格，替代超长单列下拉） ----------

    [Fact]
    public void 省份选择器_选项数_选中项与事件语义()
    {
        RunOnSta(() =>
        {
            using var picker = new ProvincePicker();
            Assert.Equal(32, ProvincePicker.Items.Count);   // 无省份 + 31 省
            Assert.Equal(0, picker.SelectedIndex);
            Assert.Equal("", picker.SelectedItem);

            int fired = 0;
            picker.SelectedIndexChanged += (_, _) => fired++;
            picker.SelectedIndex = ProvincePicker.Items.IndexOf("黑");
            Assert.Equal("黑", picker.SelectedItem);
            Assert.Equal("黑", picker.Text);
            Assert.Equal(1, fired);

            picker.SelectedIndex = picker.SelectedIndex;    // 重复赋值不重复触发
            Assert.Equal(1, fired);

            picker.SelectedIndex = 0;                       // 回到"无省份"
            Assert.Equal("", picker.SelectedItem);
            Assert.Throws<ArgumentOutOfRangeException>(() => picker.SelectedIndex = 99);
        });
    }

    [Fact]
    public void 省份选择器_关闭后可再次打开_不重入已释放对象()
    {
        RunOnSta(() =>
        {
            // 回归两类线上故障：
            // 1) ObjectDisposedException：Closed 中立即 Dispose，与菜单内部消息链路重入
            // 2) 选完省份后再次点击不弹：同一实例/同一消息周期内重开被菜单过滤器抑制
            using var host = new Form();
            var picker = new ProvincePicker();
            host.Controls.Add(picker);
            host.CreateControl();   // 建句柄但不显示窗口（不泵消息：延迟释放回调不执行，便于断言"不立即释放"）

            var flags = BindingFlags.NonPublic | BindingFlags.Instance;
            ToolStripDropDown? Drop() =>
                (ToolStripDropDown?)typeof(ProvincePicker)
                    .GetField("_drop", flags)!.GetValue(picker);

            picker.CreateDropDown();                     // 第一次打开（新建实例）
            var d1 = Drop();
            Assert.NotNull(d1);
            Assert.True(d1!.Visible);
            Assert.False(d1.IsDisposed);

            d1.Close();                                  // 模拟选省/外部点击关闭
            Assert.Null(Drop());                         // Closed 同步摘引用
            Assert.False(d1.IsDisposed);                 // 但不立即释放（重入保护）

            picker.CreateDropDown();                     // 再次打开：新建实例、正常显示
            var d2 = Drop();
            Assert.NotNull(d2);
            Assert.NotSame(d1, d2);
            Assert.True(d2!.Visible);
            Assert.False(d2.IsDisposed);

            // 点省份单元格：选中变化、弹窗关闭摘引用，之后仍可第三次打开
            var grid = (Panel)typeof(ProvincePicker)
                .GetField("_grid", flags)!.GetValue(picker)!;
            int ji = ProvincePicker.Items.IndexOf("冀");
            ((Button)grid.Controls[ji]).PerformClick();
            Assert.Equal("冀", picker.SelectedItem);
            Assert.Null(Drop());

            picker.CreateDropDown();
            Assert.True(Drop()!.Visible);
        });
    }

    /// <summary>
    /// 省份框文字区宽度回归：目标电脑上"黑"字被右缘裁掉（字体渲染比本机偏宽），
    /// 根因是文字可用宽（客户区减左右内边距）原仅约 25px（右侧给 ▼ 箭头留了 20px）。
    /// 现要求文字区 ≥ 2 倍单字宽：目标机字体偏宽/文本缩放时单字仍完整可见，且箭头不侵入文字区。
    /// </summary>
    [Fact]
    public void 车牌行_省份框文字区至少容纳两倍单字宽_不被右边缘裁切()
    {
        WeighManager manager = new();
        RunOnSta(() =>
        {
            using var dlg = new WeighEntryDialog(manager, isTare: true);
            var picker = Fld<ProvincePicker>(dlg, "_cboPrefix");
            var txt = Fld<TextBox>(dlg, "_txtVehicle");

            // 单字渲染宽（省份均为单字，"黑"即代表；NoPadding 取实际墨迹宽）
            int glyph = TextRenderer.MeasureText("黑", picker.Font,
                new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding).Width;
            int textZone = picker.ClientSize.Width - picker.Padding.Horizontal;
            Assert.True(textZone >= glyph * 2,
                $"省份框文字可用宽 {textZone}px 应 ≥ 2 倍单字宽 {glyph * 2}px（过窄时'黑'等字被右缘裁切）");

            // 自绘 ▼ 箭头（x=Width-13 起 8px 宽）不得侵入文字区，且完整落在框内
            int arrowLeft = picker.Width - 13;
            int textZoneRight = picker.ClientSize.Width - picker.Padding.Right;
            Assert.True(arrowLeft >= textZoneRight,
                $"箭头左缘 {arrowLeft} 不得侵入文字区（文字区右缘 {textZoneRight}）");
            Assert.True(arrowLeft + 8 <= picker.Width - 2,
                $"箭头右缘 {arrowLeft + 8} 应落在框内（控件宽 {picker.Width}）");

            // 编号框不遮盖省份框，且不越出车牌面板（输入列 264，面板左右 Margin 各 2 → 可用 260）
            Assert.True(txt.Left > picker.Right,
                $"编号框左缘 {txt.Left} 应大于省份框右缘 {picker.Right}（不得遮盖）");
            Assert.True(txt.Right <= 260, $"编号框右缘 {txt.Right} 应 ≤ 260（面板可用宽）");
        });
        manager.Dispose();
    }

    [Fact]
    public void 省份选择器_鼠标按下时延迟到消息周期外打开()
    {
        RunOnSta(() =>
        {
            // 真实点击路径：在 MouseDown 处理中同步 Show 会被同笔 MouseUp
            // 判为"外部点击"立即关闭（第二次点击起弹不出）；修复后排队到消息周期外才建窗
            using var host = new Form { ShowInTaskbar = false };
            var picker = new ProvincePicker();
            host.Controls.Add(picker);
            host.Show();
            picker.Focus();
            try
            {
                var flags = BindingFlags.NonPublic | BindingFlags.Instance;
                ToolStripDropDown? Drop() =>
                    (ToolStripDropDown?)typeof(ProvincePicker)
                        .GetField("_drop", flags)!.GetValue(picker);

                var onMouseDown = typeof(Control)
                    .GetMethod("OnMouseDown", BindingFlags.NonPublic | BindingFlags.Instance)!;
                onMouseDown.Invoke(picker, new object[]
                {
                    new MouseEventArgs(MouseButtons.Left, 1, 0, 0, 0),
                });
                Assert.Null(Drop());                     // 当前点击消息周期内尚未开窗
                Application.DoEvents();
                Assert.True(Drop()!.Visible);            // 泵出排队回调后稳定显示
            }
            finally
            {
                host.Close();
            }
        });
    }

    // ---------- 空车去皮：上次皮重显示 + 差值预警（仅空车去皮弹窗） ----------

    /// <summary>造一条已完成记录（先皮 10000kg 后毛 30000kg → 皮重=10000kg），返回该记录。</summary>
    private WeighRecord SeedCompletedWithTare(string vehicle, double tareKg, double grossKg)
    {
        UseTestScaleNo();
        var first = _repo.InsertFirstWeigh(new WeighRecord
        { ScaleNo = "91", VehicleNo = vehicle, GrossKg = tareKg });
        return _repo.CompleteSecondWeigh(first.Id, grossKg, null, "管理员");
    }

    [Fact]
    public void 空车去皮_有历史已完成皮重_显示上次重量时间_重车模式隐藏()
    {
        string vehicle = NewVehicle("T");
        var done = SeedCompletedWithTare(vehicle, 10000, 30000);
        WeighManager manager = new();
        RunOnSta(() =>
        {
            using var dlg = new WeighEntryDialog(manager, isTare: true);
            SilenceDialogs(dlg);
            NoPrefix(dlg);
            Fld<TextBox>(dlg, "_txtVehicle").Text = vehicle;

            // 空车去皮模式：上次重量=历史皮重，上次时间=一次磅（皮重）时间
            var lblW = Fld<Label>(dlg, "_lblLastWeight");
            var lblT = Fld<Label>(dlg, "_lblLastTime");
            Assert.True(dlg.LastTareInfoVisible);
            Assert.Equal(AppConfig.Current.FormatWeight(10000), lblW.Text);
            Assert.Equal(done.FirstTime!.Value.ToString("yyyy-MM-dd HH:mm"), lblT.Text);

            // 切到重车过磅：整组隐藏，重车表单不展示该功能
            Call(dlg, "ToggleMode");
            Assert.False(dlg.LastTareInfoVisible);
            Assert.False(lblW.Visible);
            Assert.False(lblT.Visible);
        });
        manager.Dispose();
    }

    [Fact]
    public void 空车去皮_无历史皮重_上次显示横杠()
    {
        string vehicle = NewVehicle("U");
        WeighManager manager = new();
        RunOnSta(() =>
        {
            using var dlg = new WeighEntryDialog(manager, isTare: true);
            SilenceDialogs(dlg);
            NoPrefix(dlg);
            Fld<TextBox>(dlg, "_txtVehicle").Text = vehicle;

            Assert.Equal("--", Fld<Label>(dlg, "_lblLastWeight").Text);
            Assert.Equal("--", Fld<Label>(dlg, "_lblLastTime").Text);
        });
        manager.Dispose();
    }

    [Fact]
    public void 空车去皮_皮重差超阈值_确认弹预警但不阻止保存()
    {
        string vehicle = NewVehicle("V");
        SeedCompletedWithTare(vehicle, 10000, 30000);   // 上次皮重 10 吨
        var cfg = AppConfig.Current;
        double oldThreshold = cfg.TareDiffWarnTon;
        WeighManager manager = new();
        try
        {
            cfg.TareDiffWarnTon = 0.5;   // 相差 0.5 吨以上预警
            RunOnSta(() =>
            {
                using var dlg = new WeighEntryDialog(manager, isTare: true);
                SilenceDialogs(dlg);
                UseTestScaleNo();
                NoPrefix(dlg);
                Fld<TextBox>(dlg, "_txtVehicle").Text = vehicle;
                PushStable(manager, 11000);   // 本次皮重 11 吨，相差 1 吨 > 0.5
                _lastInfo = null;

                Call(dlg, "Confirm");

                Assert.Contains("两次空车去皮称重的差值过大", _lastInfo);
                Assert.Contains("相差", _lastInfo);
                Assert.Equal(1, Fld<int>(dlg, "_step"));   // 仅预警：仍进入待保存步
            });
        }
        finally
        {
            cfg.TareDiffWarnTon = oldThreshold;
            manager.Dispose();
        }
    }

    [Fact]
    public void 空车去皮_差值未超阈值或预警关闭_确认不提示()
    {
        var cfg = AppConfig.Current;
        double oldThreshold = cfg.TareDiffWarnTon;
        WeighManager manager = new();
        try
        {
            // 场景1：阈值 2 吨，实际差 1 吨 → 不预警
            string v1 = NewVehicle("W");
            SeedCompletedWithTare(v1, 10000, 30000);
            cfg.TareDiffWarnTon = 2;
            RunOnSta(() =>
            {
                using var dlg = new WeighEntryDialog(manager, isTare: true);
                SilenceDialogs(dlg);
                UseTestScaleNo();
                NoPrefix(dlg);
                Fld<TextBox>(dlg, "_txtVehicle").Text = v1;
                PushStable(manager, 11000);
                _lastInfo = null;

                Call(dlg, "Confirm");
                Assert.Null(_lastInfo);
                Assert.Equal(1, Fld<int>(dlg, "_step"));
            });

            // 场景2：阈值 0（关闭），相差 1 吨 → 不预警
            string v2 = NewVehicle("X");
            SeedCompletedWithTare(v2, 10000, 30000);
            cfg.TareDiffWarnTon = 0;
            RunOnSta(() =>
            {
                using var dlg = new WeighEntryDialog(manager, isTare: true);
                SilenceDialogs(dlg);
                UseTestScaleNo();
                NoPrefix(dlg);
                Fld<TextBox>(dlg, "_txtVehicle").Text = v2;
                PushStable(manager, 11000);
                _lastInfo = null;

                Call(dlg, "Confirm");
                Assert.Null(_lastInfo);
            });
        }
        finally
        {
            cfg.TareDiffWarnTon = oldThreshold;
            manager.Dispose();
        }
    }

    [Fact]
    public void 重车过磅_即使有历史皮重且超差_确认也不弹去皮预警()
    {
        string vehicle = NewVehicle("Y");
        SeedCompletedWithTare(vehicle, 10000, 30000);
        var cfg = AppConfig.Current;
        double oldThreshold = cfg.TareDiffWarnTon;
        WeighManager manager = new();
        try
        {
            cfg.TareDiffWarnTon = 0.5;
            // 再造一条该车牌的未完成一次磅，使重车过磅可确认配对
            _repo.InsertFirstWeigh(new WeighRecord
            { ScaleNo = "91", VehicleNo = vehicle, GrossKg = 12000 });
            RunOnSta(() =>
            {
                using var dlg = new WeighEntryDialog(manager, isTare: false);
                SilenceDialogs(dlg);
                UseTestScaleNo();
                NoPrefix(dlg);
                Fld<TextBox>(dlg, "_txtVehicle").Text = vehicle;
                PushStable(manager, 30000);
                _lastInfo = null;

                Call(dlg, "Confirm");
                Assert.Null(_lastInfo);   // 重车表单不做去皮差值预警
            });
        }
        finally
        {
            cfg.TareDiffWarnTon = oldThreshold;
            manager.Dispose();
        }
    }

    [Fact]
    public void 仓储_取车号最近一条已完成皮重()
    {
        string vehicle = NewVehicle("Z");
        var older = SeedCompletedWithTare(vehicle, 9000, 29000);
        var newer = SeedCompletedWithTare(vehicle, 9500, 29500);

        var hit = _repo.GetLastCompletedTare(vehicle);
        Assert.NotNull(hit);
        Assert.Equal(newer.Id, hit!.Id);              // 最近一条
        Assert.Equal(9500, hit.TareKg);

        Assert.Null(_repo.GetLastCompletedTare(NewVehicle("Q")));  // 无历史
        Assert.NotNull(_repo.GetLastCompletedTare(vehicle)!.FirstTime);
        _ = older;
    }

    // ---------- 车牌字母触摸面板（双击编号框弹出 A-Z + 挂头尾） ----------

    /// <summary>按 Name 查找面板上的按钮（字符按钮 Name=char_字符，关闭 Name=btnClose）。</summary>
    private static Button KeypadBtn(PlateKeypadForm keypad, string name) =>
        keypad.Controls.OfType<Button>().Single(b => b.Name == name);

    /// <summary>
    /// 触发按钮 Click：触摸按钮设了 Selectable=false（点击不抢焦点），
    /// PerformClick 内部的 CanSelect 检查会因此短路；直接反射调用 OnClick，
    /// 与真实鼠标点击走同一条事件链路。
    /// </summary>
    private static void ClickButton(Button btn) =>
        typeof(Button).GetMethod("OnClick", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(btn, [EventArgs.Empty]);

    [Fact]
    public void 双击车牌编号框_弹出字母面板_含26字母与挂头尾及关闭()
    {
        WeighManager manager = new();
        RunOnSta(() =>
        {
            using var dlg = new WeighEntryDialog(manager, isTare: false);
            NoPrefix(dlg);

            dlg.ShowKeypad();
            var keypad = dlg.CurrentKeypad;
            Assert.NotNull(keypad);

            // A~Z 共 26 个字符按钮
            for (char c = 'A'; c <= 'Z'; c++)
                Assert.Equal(c.ToString(), KeypadBtn(keypad!, "char_" + c).Text);

            // 挂/头/尾特殊项 + 红 ✕ 关闭
            foreach (string sp in new[] { "挂", "头", "尾" })
                Assert.NotNull(keypad!.Controls.OfType<Button>()
                    .FirstOrDefault(b => b.Name == "char_" + sp));
            Assert.NotNull(keypad!.Controls.OfType<Button>()
                .FirstOrDefault(b => b.Name == "btnClose"));

            keypad.Close();
            Assert.Null(dlg.CurrentKeypad);
        });
        manager.Dispose();
    }

    [Fact]
    public void 点面板字符_连续追加到编号框_面板保持打开()
    {
        WeighManager manager = new();
        RunOnSta(() =>
        {
            using var dlg = new WeighEntryDialog(manager, isTare: false);
            NoPrefix(dlg);
            Fld<TextBox>(dlg, "_txtVehicle").Text = "";

            dlg.ShowKeypad();
            var keypad = dlg.CurrentKeypad!;

            ClickButton(KeypadBtn(keypad, "char_D"));
            Assert.Equal("D", Fld<TextBox>(dlg, "_txtVehicle").Text);
            ClickButton(KeypadBtn(keypad, "char_挂"));
            Assert.Equal("D挂", Fld<TextBox>(dlg, "_txtVehicle").Text);

            // 点选不关闭面板，可连续输入
            Assert.Same(keypad, dlg.CurrentKeypad);
            keypad.Close();
        });
        manager.Dispose();
    }

    [Fact]
    public void 有选中段时点选字符_替换选中_否则在光标处插入()
    {
        WeighManager manager = new();
        RunOnSta(() =>
        {
            using var dlg = new WeighEntryDialog(manager, isTare: true);
            var txt = Fld<TextBox>(dlg, "_txtVehicle");

            txt.Text = "ABC";
            txt.SelectionStart = 1;
            txt.SelectionLength = 1;   // 选中 B
            dlg.InsertPlateChar("X");
            Assert.Equal("AXC", txt.Text);
            Assert.Equal(2, txt.SelectionStart);

            // 无选中：在光标处插入
            txt.SelectionStart = 2;
            txt.SelectionLength = 0;
            dlg.InsertPlateChar("Y");
            Assert.Equal("AXYC", txt.Text);
        });
        manager.Dispose();
    }

    [Fact]
    public void 面板已打开时再次调用_只置顶不重复开窗()
    {
        WeighManager manager = new();
        RunOnSta(() =>
        {
            using var dlg = new WeighEntryDialog(manager, isTare: true);

            dlg.ShowKeypad();
            var first = dlg.CurrentKeypad!;
            dlg.ShowKeypad();
            Assert.Same(first, dlg.CurrentKeypad);   // 同一窗口，未重复创建

            first.Close();
            Assert.Null(dlg.CurrentKeypad);
        });
        manager.Dispose();
    }
}
