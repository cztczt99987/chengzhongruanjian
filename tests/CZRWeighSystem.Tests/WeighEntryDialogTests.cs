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
            Assert.Equal(AppConfig.Current.FormatWeight(10000), Fld<Label>(dlg, "_lblLastWeight").Text);

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
}
