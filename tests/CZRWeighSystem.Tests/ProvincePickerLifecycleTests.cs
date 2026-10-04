using CZRWeighSystem.UI;

namespace CZRWeighSystem.Tests;

/// <summary>
/// 省份选择器生命周期回归（2026-10-04 用户实机反馈反复弹
/// ObjectDisposedException: 'System.Windows.Forms.ToolStripDropDown'）。
/// 复现路径：车牌省份弹层处于打开状态时关闭宿主窗体/对话框，
/// 随后消息泵处理残余消息不得再访问已释放的 ToolStripDropDown。
/// </summary>
public class ProvincePickerLifecycleTests
{
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

    /// <summary>捕获消息循环内的异步异常（否则 WinForms 可能弹默认错误框阻塞测试宿主）。</summary>
    private static List<Exception> CatchThreadExceptions()
    {
        var errors = new List<Exception>();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => errors.Add(e.Exception);
        return errors;
    }

    [Fact]
    public void 弹层打开时关闭宿主窗体_后续消息泵不得访问已释放弹层()
    {
        RunOnSta(() =>
        {
            var errors = CatchThreadExceptions();

            using var host = new Form { ShowInTaskbar = false };
            var picker = new ProvincePicker();
            host.Controls.Add(picker);
            host.Show();
            Application.DoEvents();

            picker.CreateDropDown();          // 车牌省份弹层打开
            host.Close();                     // 弹层未关，宿主窗体先关闭（过磅弹窗即此场景）

            for (int i = 0; i < 30; i++) Application.DoEvents();   // 泵出残余消息

            Assert.True(errors.Count == 0,
                "宿主关闭后消息泵出现异常：" +
                string.Join("\r\n----\r\n", errors.Select(e => e.ToString())));
        });
    }

    [Fact]
    public void 弹层打开时直接释放选择器_不得访问已释放对象()
    {
        RunOnSta(() =>
        {
            var errors = CatchThreadExceptions();

            using (var host = new Form { ShowInTaskbar = false })
            {
                var picker = new ProvincePicker();
                host.Controls.Add(picker);
                host.Show();
                Application.DoEvents();

                picker.CreateDropDown();
                picker.Dispose();             // 弹层打开状态下直接释放选择器（对话框被 Dispose）
            }
            for (int i = 0; i < 30; i++) Application.DoEvents();

            Assert.True(errors.Count == 0,
                "释放选择器后消息泵出现异常：" +
                string.Join(" | ", errors.Select(e => e.GetType().Name + ": " + e.Message)));
        });
    }
}