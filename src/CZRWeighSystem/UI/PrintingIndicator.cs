using System.Drawing;
using System.Windows.Forms;

namespace CZRWeighSystem.UI;

/// <summary>
/// "正在打印…"非模态小提示窗。
/// 磅单打印为同步阻塞（打印期间 UI 消息循环被占用），因此 Show 后立即强制重绘一次，
/// 保证提示文字先画出来、打印期间保持可见；打印完成由调用方 Close。
/// </summary>
public sealed class PrintingIndicator : IDisposable
{
    /// <summary>测试隔离：置 true 时 Show/Close 不创建窗口（不弹提示）。</summary>
    internal static bool Suppressed = false;

    private readonly Form _form;

    public PrintingIndicator()
    {
        _form = new Form
        {
            Text = "打印",
            FormBorderStyle = FormBorderStyle.FixedToolWindow,
            StartPosition = FormStartPosition.CenterParent,
            ClientSize = new Size(200, 50),
            ShowInTaskbar = false,
        };
        _form.Controls.Add(new Label
        {
            Text = "正在打印，请稍候…",
            AutoSize = false,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Microsoft YaHei UI", 11F),
        });
    }

    /// <summary>显示提示并强制重绘一次（打印冻结前先让文字可见）。</summary>
    public void Show(Form owner)
    {
        if (Suppressed) return;
        _form.Show(owner);
        _form.Refresh();
    }

    public void Close()
    {
        if (Suppressed) return;
        _form.Close();
    }

    public void Dispose() => Close();
}
