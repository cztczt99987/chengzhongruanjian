using CZRWeighSystem.Video;

namespace CZRWeighSystem.Tests;

/// <summary>
/// 模拟摄像头画面源回归测试（2026-09-27）：
/// 原 Start() 只订阅了 Tick 事件、漏调 _timer.Start()，导致"取流地址留空的模拟画面"
/// 永远不刷新（做说明书截图时被迫反射手调 RenderFrame 才发现）。
/// 此测试锁定：Start 前不出帧、Start 后自动出帧、重复 Start 不叠加回调、Stop 后停刷。
/// </summary>
public class SimulatedCameraSourceTests
{
    /// <summary>在 STA 线程上执行（WinForms Timer 依赖消息循环，需该线程抽消息）。</summary>
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

    /// <summary>抽消息泵 ms 毫秒，让 WinForms Timer 有机会触发 Tick。</summary>
    private static void Pump(int ms)
    {
        var end = DateTime.UtcNow.AddMilliseconds(ms);
        while (DateTime.UtcNow < end)
        {
            Application.DoEvents();
            Thread.Sleep(5);
        }
    }

    [Fact]
    public void Start后自动按帧刷新_重复Start不叠加_Stop后停刷()
    {
        RunOnSta(() =>
        {
            using var source = new SimulatedCameraSource("CAM1");
            int frames = 0;
            source.PreviewFrameUpdated += () => frames++;

            Pump(300);
            Assert.Equal(0, frames);                     // 未 Start 时不渲染
            Assert.Null(source.LastFrameUtc);

            // 重复 Start（相机重建/重复调用场景）不得叠加回调
            source.Start();
            source.Start();
            Pump(600);                                   // 帧间隔 100ms
            // 下限 2 帧证明定时器确实跑起来了；上限 9 帧可排除"重复订阅导致每 Tick 渲染多次"
            Assert.InRange(frames, 2, 9);
            Assert.NotNull(source.LastFrameUtc);

            source.Stop();
            int before = frames;
            Pump(400);
            Assert.True(frames - before <= 1, $"Stop() 后不应继续刷新，又多出 {frames - before} 帧");
        });
    }
}