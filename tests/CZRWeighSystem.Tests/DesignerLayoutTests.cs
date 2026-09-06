using CZRWeighSystem.UI;
using System.Reflection;

namespace CZRWeighSystem.Tests;

/// <summary>设计器画布布局回归测试（回归：画布未设 Dock 导致 0×0 不可见，点击元素无反应）。</summary>
public class DesignerLayoutTests
{
    [Fact]
    public void Canvas_填充工具栏侧栏之外的剩余区域()
    {
        DockStyle dock = DockStyle.None;
        Rectangle bounds = Rectangle.Empty;
        int clientWidth = 0, clientHeight = 0;

        var thread = new Thread(() =>
        {
            using var form = new TemplateDesignerForm("");
            form.Show();
            Application.DoEvents();     // 触发布局

            var canvas = (Control)typeof(TemplateDesignerForm).GetField("_canvas",
                BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(form)!;
            dock = canvas.Dock;
            bounds = canvas.Bounds;
            clientWidth = form.ClientSize.Width;
            clientHeight = form.ClientSize.Height;
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.Equal(DockStyle.Fill, dock);
        // 画布占满中间剩余区域：左侧工具箱 210 + 右侧属性栏 260，不可为 0×0
        Assert.True(bounds.Width > 500, $"画布宽度异常：{bounds}");
        Assert.True(bounds.Height > 400, $"画布高度异常：{bounds}");
        Assert.True(bounds.X >= 200 && bounds.Right <= clientWidth - 250,
            $"画布未落在左右侧栏之间：{bounds}，客户区 {clientWidth}×{clientHeight}");
        Assert.Equal(clientHeight - 54, bounds.Height);   // 顶部工具栏 32 + 底部状态栏 22
    }
}
