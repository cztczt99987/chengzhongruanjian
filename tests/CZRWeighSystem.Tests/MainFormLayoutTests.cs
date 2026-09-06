using CZRWeighSystem.UI;
using System.Reflection;

namespace CZRWeighSystem.Tests;

/// <summary>
/// 首页布局回归测试（回归：底部新增内嵌视频监控区后，
/// 采集信息列表与视频画面不得互相遮挡，其余原有元素不得缺失）。
/// 注意：控件数据必须在窗体释放前的 STA 线程内采集（释放后子控件集合会被清空）。
/// </summary>
public class MainFormLayoutTests
{
    private sealed record LayoutSnapshot(
        string VideoText, Rectangle VideoBounds, int VideoControlCount,
        Rectangle CaptureBounds, int CaptureColumnCount,
        string FirstColumnHeader, string LastColumnHeader);

    private static LayoutSnapshot BuildAndMeasure()
    {
        LayoutSnapshot snap = null!;
        var thread = new Thread(() =>
        {
            using var form = new MainForm();
            var video = (GroupBox)typeof(MainForm).GetField("_videoPanel",
                BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(form)!;
            var capture = (GroupBox)typeof(MainForm).GetField("_capturePanel",
                BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(form)!;
            var grid = (DataGridView)capture.Controls[0]!;
            snap = new LayoutSnapshot(
                video.Text, video.Bounds, video.Controls.Count,
                capture.Bounds, grid.Columns.Count,
                grid.Columns[0].HeaderText, grid.Columns[grid.Columns.Count - 1].HeaderText);
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        return snap;
    }

    [Fact]
    public void 底部区域_视频监控与采集信息并排且不重叠()
    {
        var s = BuildAndMeasure();

        Assert.Equal("视频监控", s.VideoText);
        // 同一底部行：顶边/高度一致，视频区在采集信息区右侧，无重叠
        Assert.Equal(s.CaptureBounds.Top, s.VideoBounds.Top);
        Assert.Equal(s.CaptureBounds.Height, s.VideoBounds.Height);
        Assert.True(s.VideoBounds.Left >= s.CaptureBounds.Right,
            $"视频区({s.VideoBounds.Left})与采集信息区(右边={s.CaptureBounds.Right})重叠");
        // 底部两区总宽覆盖：视频区右缘应明显超出采集信息区（窗体 1380 宽）
        Assert.True(s.VideoBounds.Width > 300, $"视频区宽度异常：{s.VideoBounds.Width}");
    }

    [Fact]
    public void 未配置摄像头时_视频区显示提示而非空白()
    {
        // 测试环境未配置摄像头源 → 至少有占位提示控件
        Assert.True(BuildAndMeasure().VideoControlCount > 0, "视频监控区为空，缺少未配置提示");
    }

    [Fact]
    public void 采集信息列表_原有八列完整保留()
    {
        var s = BuildAndMeasure();
        Assert.Equal(8, s.CaptureColumnCount);
        Assert.Equal("流水号", s.FirstColumnHeader);
        Assert.Equal("状态", s.LastColumnHeader);
    }

    [Theory]
    [InlineData(1, 1, 1)]   // 1 路：独占整块区域
    [InlineData(2, 1, 2)]   // 2 路：纵向两行（窄长区域单路画面更宽）
    [InlineData(3, 2, 2)]   // 3 路起：两列网格，避免每格过扁
    [InlineData(4, 2, 2)]
    [InlineData(5, 2, 3)]
    public void 视频网格分布_按路数自适应(int count, int expectCols, int expectRows)
    {
        var (cols, rows) = MainForm.VideoGridSize(count);
        Assert.Equal((expectCols, expectRows), (cols, rows));
    }
}
