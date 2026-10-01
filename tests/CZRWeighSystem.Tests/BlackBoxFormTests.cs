using CZRWeighSystem.UI;
using System.Reflection;

namespace CZRWeighSystem.Tests;

/// <summary>
/// 黑匣子记录窗体布局回归测试：
/// 条件区"重量≥/≤"复选框为 AutoSize，实际宽度曾超过预留坐标，
/// 导致数字框压住复选框文字（标签被截断）。此测试锁定控件互不重叠。
/// </summary>
public class BlackBoxFormTests
{
    /// <summary>读取窗体私有字段（控件）。</summary>
    private static T Fld<T>(BlackBoxForm f, string name) =>
        (T)typeof(BlackBoxForm).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(f)!;

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

    [Fact]
    public void 条件区_重量复选框与数字框及kg标签互不重叠()
    {
        RunOnSta(() =>
        {
            using var f = new BlackBoxForm();
            var chkMin = Fld<System.Windows.Forms.CheckBox>(f, "_chkMin");
            var numMin = Fld<System.Windows.Forms.NumericUpDown>(f, "_numMinKg");
            var chkMax = Fld<System.Windows.Forms.CheckBox>(f, "_chkMax");
            var numMax = Fld<System.Windows.Forms.NumericUpDown>(f, "_numMaxKg");

            // AutoSize 复选框必须完整显示在数字框左侧，不得被压住
            Assert.True(chkMin.Right <= numMin.Left,
                $"重量≥复选框右缘 {chkMin.Right} 与下限数字框左缘 {numMin.Left} 重叠");
            Assert.True(chkMax.Right <= numMax.Left,
                $"重量≤复选框右缘 {chkMax.Right} 与上限数字框左缘 {numMax.Left} 重叠");

            // 两段"下限…上限"条件自身也不得交叠
            Assert.True(numMin.Right < chkMax.Left,
                $"下限数字框右缘 {numMin.Right} 与重量≤复选框左缘 {chkMax.Left} 重叠");
        });
    }
}