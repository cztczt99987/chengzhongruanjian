using CZRWeighSystem.Comm;
using CZRWeighSystem.Core;
using CZRWeighSystem.Database;
using CZRWeighSystem.UI;
using System.Reflection;

namespace CZRWeighSystem.Tests;

/// <summary>
/// 首页手动皮/毛选择保存回归测试（2026-09-30 首页改版）：
/// 「空车去皮 / 重车过磅」两按钮手动声明本次重量类型；
/// 车号已有待配对记录时保存即自动二次磅结算（毛/皮按大小分配，
/// 兼容先皮后毛与先毛后皮）；未录车号/重量未稳定时不保存。
/// 对话框通过 internal ConfirmBox/InfoBox 注入静默实现，避免测试阻塞。
/// 与 FirstWeighExpireTests 同集合串行执行，避免共享 SQLite 库互相干扰。
/// </summary>
[Collection("数据库顺序")]
public class MainFormWeighDisplayTests
{
    private readonly WeighRecordRepository _repo = new();

    /// <summary>InfoBox 静默实现记录的最后一条提示（断言提示内容用）。</summary>
    private static string? _lastInfo;

    public MainFormWeighDisplayTests() => Db.Initialize();   // 幂等建表

    /// <summary>读取 MainForm 私有字段（控件/管理器）。</summary>
    private static T Fld<T>(MainForm form, string name) =>
        (T)typeof(MainForm).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(form)!;

    /// <summary>调用 MainForm 私有方法。</summary>
    private static void Call(MainForm form, string name) =>
        typeof(MainForm).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(form, null);

    /// <summary>在 STA 线程上构建窗体并执行测试体（WinForms 控件只能在 STA 线程访问）。</summary>
    private static void RunOnSta(Action<MainForm> act)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                using var form = new MainForm();
                act(form);
            }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error != null) throw error;
    }

    /// <summary>注入静默对话框：「确认操作」标题自动确认（打印询问返回 false），提示记录到 _lastInfo。</summary>
    private static void SilenceDialogs(MainForm form)
    {
        form.ConfirmBox = (msg, title) => title == "确认操作";
        form.InfoBox = (msg, title, ok) => _lastInfo = msg;
    }

    /// <summary>
    /// UI 保存用独立磅号：流水号按"日期+磅号+计数"生成且删除会使计数回落，
    /// 默认磅号 01 会与 FirstWeighExpireTests 插删的记录串号（UNIQUE 冲突）。
    /// </summary>
    private static void UseTestScaleNo() => AppConfig.Current.ScaleNo = "91";

    /// <summary>向管理器的稳定判定器推送 N 次相同采样，使 IsStable=true、StableKg=kg。</summary>
    private static void PushStable(MainForm form, double kg)
    {
        var manager = Fld<WeighManager>(form, "_manager");
        var filter = (StabilityFilter)typeof(WeighManager)
            .GetField("_filter", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(manager)!;
        for (int i = 0; i < 3; i++) filter.Push(kg);   // 默认 StableCount=3
    }

    [Fact]
    public void 空车去皮_无待配对记录_保存为第一次称重()
    {
        string vehicle = "测F10001";
        RunOnSta(form =>
        {
            SilenceDialogs(form);
            UseTestScaleNo();
            Fld<TextBox>(form, "_txtVehicle").Text = vehicle;
            PushStable(form, 10000);
            Call(form, "SaveTare");

            // 保存成功后录入区清空
            Assert.Equal("", Fld<TextBox>(form, "_txtVehicle").Text);
        });

        var record = _repo.Query(null, null, vehicle, "", "", "").Single();
        Assert.Equal("未完成", record.Status);
        Assert.Equal(10000, record.GrossKg);   // 一次磅重量暂存 gross_kg
    }

    [Fact]
    public void 重车过磅_车号有待配对记录_自动二次磅结算()
    {
        string vehicle = "测G10002";
        _repo.InsertFirstWeigh(new WeighRecord { ScaleNo = "02", VehicleNo = vehicle, GrossKg = 30000 });

        RunOnSta(form =>
        {
            SilenceDialogs(form);
            Fld<TextBox>(form, "_txtVehicle").Text = vehicle;
            PushStable(form, 50000);
            Call(form, "SaveGross");
        });

        var record = _repo.Query(null, null, vehicle, "", "", "").Single();
        Assert.Equal("已完成", record.Status);
        Assert.Equal(50000, record.GrossKg);   // 毛重 = 大值
        Assert.Equal(30000, record.TareKg);    // 皮重 = 小值
        Assert.Equal(20000, record.NetKg);
    }

    [Fact]
    public void 空车去皮_先毛后皮_结算净重一致()
    {
        string vehicle = "测H10003";
        _repo.InsertFirstWeigh(new WeighRecord { ScaleNo = "03", VehicleNo = vehicle, GrossKg = 50000 });

        RunOnSta(form =>
        {
            SilenceDialogs(form);
            Fld<TextBox>(form, "_txtVehicle").Text = vehicle;
            PushStable(form, 30000);
            Call(form, "SaveTare");
        });

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
        RunOnSta(form =>
        {
            SilenceDialogs(form);
            PushStable(form, 10000);
            Call(form, "SaveTare");
        });

        Assert.Contains("请选择车号", _lastInfo);
    }

    [Fact]
    public void 重量未稳定时保存_提示未稳定且不保存()
    {
        string vehicle = "测I10005";
        _lastInfo = null;
        RunOnSta(form =>
        {
            SilenceDialogs(form);
            Fld<TextBox>(form, "_txtVehicle").Text = vehicle;
            Call(form, "SaveGross");   // 未推送稳定采样 → IsStable=false
        });

        Assert.Contains("未稳定", _lastInfo);
        Assert.Empty(_repo.Query(null, null, vehicle, "", "", ""));
    }

    [Fact]
    public void 录入待配对车号_显示配对提示()
    {
        string vehicle = "测J10006";
        _repo.InsertFirstWeigh(new WeighRecord { ScaleNo = "04", VehicleNo = vehicle, GrossKg = 10000 });

        RunOnSta(form =>
        {
            // 车号变化触发 TextChanged → 配对提示
            Fld<TextBox>(form, "_txtVehicle").Text = vehicle;
            Assert.Contains("未完成的二次磅", Fld<Label>(form, "_lblTareHint").Text);
        });
    }
}
