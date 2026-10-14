using CZRWeighSystem.UI;
using CZRWeighSystem.Video;

namespace CZRWeighSystem.Tests;

/// <summary>
/// 模拟摄像头画面源回归测试（2026-09-27）：
/// 原 Start() 只订阅了 Tick 事件、漏调 _timer.Start()，导致"取流地址留空的模拟画面"
/// 永远不刷新（做说明书截图时被迫反射手调 RenderFrame 才发现）。
/// 此测试锁定：Start 前不出帧、Start 后自动出帧、重复 Start 不叠加回调、Stop 后停刷。
/// 另含：抓拍存盘与画面绘制并发使用同一帧位图的互斥回归（2026-10-14 目标机错误框）。
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

    /// <summary>
    /// 抓拍存盘与画面绘制并发使用同一帧位图，不得抛 "Object is currently in use elsewhere"。
    /// 该异常在目标机上由全局异常钩子弹了错误框（2026-10-14：「操作出现异常…」）。
    /// 约定：抓拍先在帧锁内拷贝像素再在锁外编码，绘制（MainForm.DrawFrameFit）也在同一把帧锁内，
    /// 与取流写帧一起三者串行 → GDI+ 同一图像不会被多线程并发使用。
    /// </summary>
    [Fact]
    public void 抓拍与画面绘制并发_不抛图像占用异常()
    {
        using var source = new SimulatedCameraSource("CAM1");
        var frame = source.CurrentFrame!;   // 构造函数已建好帧位图，无需 Start
        string file = Path.Combine(Path.GetTempPath(), $"czr_capture_{Guid.NewGuid():N}.jpg");

        var errors = new List<Exception>();
        bool stop = false;
        // 绘制线程：走首页画面槽位 Paint 的同一条绘制路径
        var drawer = new Thread(() =>
        {
            using var bmp = new Bitmap(160, 90);
            using var g = Graphics.FromImage(bmp);
            while (!Volatile.Read(ref stop))
            {
                try { MainForm.DrawFrameFit(g, frame, new Size(160, 90)); }
                catch (Exception ex) { lock (errors) errors.Add(ex); }
            }
        }) { IsBackground = true };
        drawer.Start();

        try
        {
            for (int i = 0; i < 40; i++)
                Assert.True(source.CaptureTo(file), "抓拍应成功保存");
        }
        finally
        {
            Volatile.Write(ref stop, true);
            drawer.Join();
            File.Delete(file);
        }

        lock (errors)
            Assert.Empty(errors);   // 修复前：存盘与绘制并发占用同一图像 → GDI+ 抛对象占用异常
    }
}