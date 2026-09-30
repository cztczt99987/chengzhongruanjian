using CZRWeighSystem.Comm;
using CZRWeighSystem.Core;
using CZRWeighSystem.Database;
using CZRWeighSystem.UI;
using System.Reflection;

namespace CZRWeighSystem.Tests;

/// <summary>
/// 称重录入弹窗回归测试（2026-09-30 首页二次改版：信息录入区移入弹窗）：
/// 空车去皮/重车过磅弹出窗中保存——车号待配对时自动二次磅结算（毛/皮按大小分配，
/// 兼容先皮后毛与先毛后皮）；未录车号/重量未稳定时不保存；
/// 录入待配对车号显示配对提示并切换"保存并结算"按钮。
/// 对话框通过 internal ConfirmBox/InfoBox 注入静默实现，避免测试阻塞。
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

    /// <summary>注入静默对话框：「确认操作」标题自动确认（打印询问返回 false），提示记录到 _lastInfo。</summary>
    private static void SilenceDialogs(WeighEntryDialog dlg)
    {
        dlg.ConfirmBox = (msg, title) => title == "确认操作";
        dlg.InfoBox = (msg, title, ok) => _lastInfo = msg;
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
        for (int i = 0; i < 3; i++) filter.Push(kg);   // 默认 StableCount=3
    }

    /// <summary>每次运行唯一车号：共享库会残留上次运行的记录，断言 Single 时不能重名。</summary>
    private static string NewVehicle(string prefix) => $"测{prefix}{Guid.NewGuid():N}"[..7];

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
            Fld<TextBox>(dlg, "_txtVehicle").Text = vehicle;
            PushStable(manager, 10000);
            Call(dlg, "Save");

            Assert.Equal(DialogResult.OK, dlg.DialogResult);
        });
        manager.Dispose();

        Assert.True(onSaved, "保存成功后未触发 OnSaved 回调");
        var record = _repo.Query(null, null, vehicle, "", "", "").Single();
        Assert.Equal("未完成", record.Status);
        Assert.Equal(10000, record.GrossKg);   // 一次磅重量暂存 gross_kg
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
            Fld<TextBox>(dlg, "_txtVehicle").Text = vehicle;
            PushStable(manager, 50000);
            Call(dlg, "Save");

            Assert.Equal(DialogResult.OK, dlg.DialogResult);
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
            Fld<TextBox>(dlg, "_txtVehicle").Text = vehicle;
            PushStable(manager, 30000);
            Call(dlg, "Save");

            Assert.Equal(DialogResult.OK, dlg.DialogResult);
        });
        manager.Dispose();

        var record = _repo.Query(null, null, vehicle, "", "", "").Single();
        Assert.Equal("已完成", record.Status);
        Assert.Equal(50000, record.GrossKg);
        Assert.Equal(30000, record.TareKg);
        Assert.Equal(20000, record.NetKg);
    }

    [Fact]
    public void 未录车号时保存_提示请选择车号且不保存()
    {
        _lastInfo = null;
        WeighManager manager = new();
        RunOnSta(() =>
        {
            using var dlg = new WeighEntryDialog(manager, isTare: true);
            SilenceDialogs(dlg);
            PushStable(manager, 10000);
            Call(dlg, "Save");

            Assert.NotEqual(DialogResult.OK, dlg.DialogResult);
        });
        manager.Dispose();

        Assert.Contains("请选择车号", _lastInfo);
    }

    [Fact]
    public void 重量未稳定时保存_提示未稳定且不保存()
    {
        string vehicle = NewVehicle("N");
        _lastInfo = null;
        WeighManager manager = new();
        RunOnSta(() =>
        {
            using var dlg = new WeighEntryDialog(manager, isTare: false);
            SilenceDialogs(dlg);
            Fld<TextBox>(dlg, "_txtVehicle").Text = vehicle;
            Call(dlg, "Save");   // 未推送稳定采样 → IsStable=false

            Assert.NotEqual(DialogResult.OK, dlg.DialogResult);
        });
        manager.Dispose();

        Assert.Contains("未稳定", _lastInfo);
        Assert.Empty(_repo.Query(null, null, vehicle, "", "", ""));
    }

    [Fact]
    public void 录入待配对车号_显示配对提示并切换保存并结算()
    {
        string vehicle = NewVehicle("O");
        _repo.InsertFirstWeigh(new WeighRecord { ScaleNo = "04", VehicleNo = vehicle, GrossKg = 10000 });
        WeighManager manager = new();
        RunOnSta(() =>
        {
            using var dlg = new WeighEntryDialog(manager, isTare: false);
            // 车号变化触发 TextChanged → UpdateWeight 刷新配对信息
            Fld<TextBox>(dlg, "_txtVehicle").Text = vehicle;

            Assert.Contains("未完成的二次磅", Fld<Label>(dlg, "_lblPairHint").Text);
            Assert.Equal("保存并结算", Fld<Button>(dlg, "_btnSave").Text);
            Assert.Equal("10000 kg", Fld<Label>(dlg, "_lblLastWeight").Text);
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
}
