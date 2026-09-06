using System.Diagnostics;
using System.Runtime.InteropServices;
using LibVLCSharp.Shared;

namespace CZRWeighSystem.Video;

/// <summary>
/// 网络摄像头实时流源（对应 PRD F7-01/F7-04，真实摄像头接入）。
/// 基于 LibVLCSharp（VLC 内核）解码，支持萤石/慧眼智巡平台生成的
/// HTTP-FLV、HLS(m3u8)、RTMP 取流地址，以及摄像头本机 RTSP 地址。
///
/// 实现方式：VLC 视频回调（离屏取帧，无需窗口）→ 前后台双缓冲 Bitmap
/// （后台写帧、前台供 UI 绘制与抓拍，避免 VLC 写帧与 GDI+ 并发冲突）→
/// 触发 PreviewFrameUpdated。抓拍即保存前台帧为 JPEG（与 SimulatedCameraSource 行为一致）。
/// </summary>
public class RtspCameraSource : ICameraSource
{
    private static readonly object VlcLock = new();
    private static LibVLC? _libVlc;   // 进程级共享：多路取流复用同一 VLC 实例

    public string Name { get; }
    private readonly string _url;

    private MediaPlayer? _player;
    private Media? _media;
    private readonly System.Threading.Timer _retryTimer;
    private int _retrying;                       // 防止重试重入
    private bool _disposed;

    // 前后台双缓冲：仅 VLC 线程写"后台"，前台交替复用（UI 与抓拍读前台）
    private readonly object _swapLock = new();
    private Bitmap? _front, _back;
    private byte[] _pixels = [];                 // RV32(BGRA) 像素缓冲
    private GCHandle _pixelsHandle;
    private bool _pixelsPinned;
    private int _width, _height;
    private readonly Stopwatch _displayClock = Stopwatch.StartNew();

    public Image? CurrentFrame => _front;

    public event Action? PreviewFrameUpdated;

    public RtspCameraSource(string name, string url)
    {
        Name = name;
        _url = url;
        _retryTimer = new System.Threading.Timer(_ => RetryPlay(), null,
            Timeout.Infinite, Timeout.Infinite);
    }

    public void Start()
    {
        try
        {
            var libVlc = GetLibVlc();

            _media = new Media(libVlc, _url, FromType.FromLocation);
            _media.AddOption(":network-caching=400");   // 降低预览延迟（默认约 1s）
            _media.AddOption(":rtsp-tcp");              // RTSP 走 TCP 防花屏（其他协议自动忽略）

            _player = new MediaPlayer(libVlc);
            _player.EncounteredError += OnPlayerError;
            _player.EndReached += OnPlayerEnd;
            // 必须在 Play 之前设置视频回调（离屏取帧，不依赖窗口）
            _player.SetVideoFormatCallbacks(FormatVideo, CleanupVideo);
            _player.SetVideoCallbacks(LockVideo, UnlockVideo, DisplayVideo);
            _player.Play();

            Log.Info($"[{Name}] 开始取流：{_url}");
        }
        catch (Exception ex)
        {
            Log.Error($"[{Name}] 取流启动失败（请检查地址/网络，及输出目录 libvlc\\win-x64 是否完整）：{_url}", ex);
        }
    }

    public void Stop()
    {
        try
        {
            if (_player != null)
            {
                _player.EncounteredError -= OnPlayerError;
                _player.EndReached -= OnPlayerEnd;
                _player.Stop();
            }
            Log.Info($"[{Name}] 已停止取流");
        }
        catch (Exception ex)
        {
            Log.Error($"[{Name}] 停止取流异常", ex);
        }
    }

    /// <summary>抓拍当前前台帧到文件（JPEG）。</summary>
    public bool CaptureTo(string filePath)
    {
        try
        {
            var frame = _front;
            if (frame == null)
            {
                Log.Info($"[{Name}] 暂无画面，抓拍跳过");
                return false;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            lock (frame)   // 与该位图写入互斥（抓拍发生在保存称重时，与显示线程竞争极少）
            {
                frame.Save(filePath, System.Drawing.Imaging.ImageFormat.Jpeg);
            }
            Log.Info($"抓拍保存：{filePath}");
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("抓拍失败：" + filePath, ex);
            return false;
        }
    }

    /// <summary>取共享的 VLC 实例（惰性初始化，进程内仅创建一次）。</summary>
    private static LibVLC GetLibVlc()
    {
        lock (VlcLock)
        {
            if (_libVlc == null)
            {
                LibVLCSharp.Shared.Core.Initialize();   // 定位输出目录下 libvlc\win-x64 原生库
                _libVlc = new LibVLC(enableDebugLogs: false, "--no-osd");
                Log.Info("LibVLC 初始化完成");
            }
            return _libVlc;
        }
    }

    // ===== VLC 取流出错后的自动重试（5 秒后重播，网络抖动兜底） =====

    private void OnPlayerError(object? sender, EventArgs e)
    {
        Log.Error($"[{Name}] 取流出错（地址无效/token 过期/网络不通等），5 秒后自动重试：{_url}");
        ScheduleRetry();
    }

    private void OnPlayerEnd(object? sender, EventArgs e)
    {
        Log.Info($"[{Name}] 取流结束，5 秒后自动重试");
        ScheduleRetry();
    }

    private void ScheduleRetry()
    {
        if (_disposed) return;
        try
        {
            _retryTimer.Change(5000, Timeout.Infinite);
        }
        catch (ObjectDisposedException) { /* 停止瞬间的竞争，忽略 */ }
    }

    private void RetryPlay()
    {
        if (Interlocked.Exchange(ref _retrying, 1) != 0) return;
        try
        {
            if (_disposed || _player == null) return;
            if (!_player.IsPlaying) _player.Play();
        }
        catch (Exception ex)
        {
            Log.Error($"[{Name}] 重试取流失败", ex);
        }
        finally
        {
            Interlocked.Exchange(ref _retrying, 0);
        }
    }

    // ===== VLC 视频回调（在 VLC 解码线程上执行，内部不得向外抛异常） =====

    /// <summary>
    /// 格式协商：写入 RV32 像素格式（= GDI Format32bppRgb 的 BGRA 字节序），
    /// 分配像素缓冲与双缓冲位图，返回缓冲数（0 表示协商失败）。
    /// </summary>
    private uint FormatVideo(ref IntPtr opaque, IntPtr chroma, ref uint width, ref uint height,
        ref uint pitches, ref uint lines)
    {
        try
        {
            lock (_swapLock)
            {
                ReleaseBuffersLocked();
                // 限制缓冲上限，防止异常大流占满内存
                _width = (int)Math.Min(width, 1920u);
                _height = (int)Math.Min(height, 1080u);
                width = (uint)_width;
                height = (uint)_height;
                pitches = (uint)(_width * 4);   // RV32 每行字节数
                lines = (uint)_height;

                _pixels = new byte[_width * _height * 4];
                _pixelsHandle = GCHandle.Alloc(_pixels, GCHandleType.Pinned);
                _pixelsPinned = true;
                _front = new Bitmap(_width, _height, System.Drawing.Imaging.PixelFormat.Format32bppRgb);
                _back = new Bitmap(_width, _height, System.Drawing.Imaging.PixelFormat.Format32bppRgb);
            }
            // chroma 是 VLC 提供的格式缓冲区（至少 32 字节），写入 "RV32\0"
            Marshal.Copy([(byte)'R', (byte)'V', (byte)'3', (byte)'2', (byte)0], 0, chroma, 5);
            return 1;
        }
        catch (Exception ex)
        {
            Log.Error($"[{Name}] 视频缓冲分配失败", ex);
            return 0;
        }
    }

    /// <summary>锁定像素缓冲：把缓冲指针写入 planes[0]，返回 picture 指针。</summary>
    private IntPtr LockVideo(IntPtr opaque, IntPtr planes)
    {
        if (_pixelsPinned)
        {
            IntPtr p = _pixelsHandle.AddrOfPinnedObject();
            Marshal.WriteIntPtr(planes, p);
            return p;
        }
        return IntPtr.Zero;
    }

    private void UnlockVideo(IntPtr opaque, IntPtr picture, IntPtr planes) { }

    /// <summary>一帧解码完成：限频 10fps，写后台帧 → 前后台交换 → 触发事件。</summary>
    private void DisplayVideo(IntPtr opaque, IntPtr picture)
    {
        if (_displayClock.ElapsedMilliseconds < 100) return;   // 预览 10fps 足够
        _displayClock.Restart();

        try
        {
            Bitmap? target;
            lock (_swapLock) target = _back;
            if (target == null || _pixels.Length == 0) return;

            // 写后台帧（后台帧仅本线程触碰，UI 永不绘制）
            lock (target)
            {
                var data = target.LockBits(
                    new Rectangle(0, 0, _width, _height),
                    System.Drawing.Imaging.ImageLockMode.WriteOnly,
                    System.Drawing.Imaging.PixelFormat.Format32bppRgb);
                try
                {
                    Marshal.Copy(_pixels, 0, data.Scan0,
                        Math.Min(_pixels.Length, Math.Abs(data.Stride) * _height));
                }
                finally
                {
                    target.UnlockBits(data);
                }
            }

            lock (_swapLock)
            {
                _back = _front;   // 旧前台转后台，下下帧才复用（给 UI 绘制留足时间）
                _front = target;
            }

            PreviewFrameUpdated?.Invoke();
        }
        catch (Exception ex)
        {
            Log.Error($"[{Name}] 帧处理异常", ex);
        }
    }

    private void CleanupVideo(ref IntPtr opaque)
    {
        lock (_swapLock) ReleaseBuffersLocked();
    }

    /// <summary>释放像素缓冲与双缓冲位图（须持有 _swapLock）。</summary>
    private void ReleaseBuffersLocked()
    {
        if (_pixelsPinned)
        {
            if (_pixelsHandle.IsAllocated) _pixelsHandle.Free();
            _pixelsPinned = false;
        }
        _pixels = [];
        _front?.Dispose();
        _back?.Dispose();
        _front = null;
        _back = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _retryTimer.Dispose();
        Stop();
        _player?.Dispose();
        _player = null;
        _media?.Dispose();
        _media = null;
        lock (_swapLock) ReleaseBuffersLocked();
    }
}
