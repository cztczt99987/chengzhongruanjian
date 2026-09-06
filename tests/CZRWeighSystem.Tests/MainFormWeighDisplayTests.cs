using CZRWeighSystem.Comm;
using CZRWeighSystem.Core;
using CZRWeighSystem.Database;
using CZRWeighSystem.UI;
using System.Reflection;

namespace CZRWeighSystem.Tests;

/// <summary>
/// 首页毛重显示与保存按钮回归测试（2026-09-06 用户反馈）：
/// 1. 毛重栏只显示已保存的记录值，不得镜像实时重量（未选中记录时显示 "--"）；
/// 2. 二次磅保存完成后，保存按钮文字必须从"保存二次磅（结算）"复位为"保存"。
/// 与 FirstWeighExpireTests 同集合串行执行，避免共享 SQLite 库互相干扰。
/// </summary>
[Collection("数据库顺序")]
public class MainFormWeighDisplayTests
{
    public MainFormWeighDisplayTests() => Db.Initialize();   // 幂等建表（RefreshGrid 会查询未完成列表）

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

    /// <summary>向管理器的稳定判定器推送 N 次相同采样，使 IsStable=true、StableKg=kg。</summary>
    private static void PushStable(MainForm form, double kg)
    {
        var manager = Fld<WeighManager>(form, "_manager");
        var filter = (StabilityFilter)typeof(WeighManager)
            .GetField("_filter", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(manager)!;
        for (int i = 0; i < 3; i++) filter.Push(kg);   // 默认 StableCount=3
    }

    [Fact]
    public void 未选中记录时_毛重栏不镜像实时重量()
    {
        RunOnSta(form =>
        {
            // 模拟仪表送上稳定重量 60000kg（旧缺陷：毛重栏会显示该实时值并随称量变化）
            PushStable(form, 60000);
            Call(form, "RefreshWeightBoxes");

            var gross = Fld<Label>(form, "_lblGross");
            Assert.Equal("--", gross.Text);
        });
    }

    [Fact]
    public void 选中未完成记录时_毛重栏显示已保存的一次磅重量()
    {
        RunOnSta(form =>
        {
            var grid = Fld<DataGridView>(form, "_grid");
            int i = grid.Rows.Add("测A12345", "60000 kg", "09-06 10:00", "煤");
            grid.Rows[i].Tag = new WeighRecord { VehicleNo = "测A12345", GrossKg = 60000, Status = "未完成" };
            grid.CurrentCell = grid.Rows[i].Cells[0];
            Call(form, "RefreshWeightBoxes");

            var gross = Fld<Label>(form, "_lblGross");
            Assert.Equal("60000 kg", gross.Text);   // 毛重=已保存记录值，而非实时重量
        });
    }

    [Fact]
    public void 二次磅保存完成后_保存按钮文字复位为保存()
    {
        RunOnSta(form =>
        {
            // 清空共享测试库：模拟该一次磅已完成结算、从"未完成列表"消失
            using (var conn = Db.CreateConnection())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "DELETE FROM t_weigh_record";
                cmd.ExecuteNonQuery();
            }

            // 选中未完成记录并录入匹配车号 → 保存按钮进入二次磅模式
            var grid = Fld<DataGridView>(form, "_grid");
            int i = grid.Rows.Add("测B12345", "60000 kg", "09-06 10:00", "煤");
            grid.Rows[i].Tag = new WeighRecord { VehicleNo = "测B12345", GrossKg = 60000, Status = "未完成" };
            grid.CurrentCell = grid.Rows[i].Cells[0];
            Fld<TextBox>(form, "_txtVehicle").Text = "测B12345";
            var save = Fld<Button>(form, "_btnSave");
            Assert.Equal("保存二次磅（结算）", save.Text);   // 前置：保存前确为二次磅模式

            // 保存成功后刷新未完成列表（记录已完成 → 消失），按钮必须复位
            Call(form, "RefreshGrid");
            Assert.Equal("保存", save.Text);
        });
    }
}
